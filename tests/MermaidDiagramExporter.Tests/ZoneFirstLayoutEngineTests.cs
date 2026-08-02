using System;
using System.IO;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Gui.Layout;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Behavior tests for the zone-first hybrid engine
/// (<see cref="LayoutEngineKind.ZoneFirst"/>): macro placement of namespace
/// zones by coupling + independent micro layout inside each zone.
/// </summary>
public class ZoneFirstLayoutEngineTests
{
    private const string MultiNamespaceSource = """
        namespace Game.Data
        {
            public interface IWeapon { void Fire(); }
            public class Weapon : IWeapon { public void Fire() { } }
            public class Sword : Weapon { }
            public class WeaponStats { public int Damage; }
        }
        namespace Game.Systems
        {
            public class InventorySystem { public Game.Data.Weapon Current; }
            public class CombatSystem { public Game.Data.IWeapon Weapon; public Game.Systems.InventorySystem Inventory; }
        }
        namespace Game.UI
        {
            public class HudPanel { public Game.Systems.CombatSystem Combat; }
            public class DamageNumbers { public Game.Systems.CombatSystem Combat; }
        }
        """;

    /// <summary>
    /// Four single-class namespaces, coupled as Alpha→Beta and Gamma→Delta;
    /// the two pairs are uncoupled. Used for zone-level proximity assertions.
    /// </summary>
    private const string TwoZonePairsSource = """
        namespace PairOne.AlphaNs { public class Alpha { public PairOne.BetaNs.Beta B; } }
        namespace PairOne.BetaNs { public class Beta { } }
        namespace PairTwo.GammaNs { public class Gamma { public PairTwo.DeltaNs.Delta D; } }
        namespace PairTwo.DeltaNs { public class Delta { } }
        """;

    private static Core.TypeGraph ScanSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mermaid_zonefirst_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Test.cs"), source);
            var scanner = new RoslynTypeScanner();
            return scanner.ScanFolder(tempDir, new GraphBuildOptions());
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    private static LayoutResult RunLayout(Core.TypeGraph graph, ZoneFirstMicroEngine micro = ZoneFirstMicroEngine.Sugiyama)
    {
        var coordinator = new GraphLayoutCoordinator();
        return coordinator.CreateLayout(graph, new LayoutOptions
        {
            Engine = LayoutEngineKind.ZoneFirst,
            ZoneFirst = new ZoneFirstEngineOptions { MicroEngine = micro },
        });
    }

    [Fact]
    public void ZoneFirst_SugiyamaMicro_ProducesBoundsForAllNodes()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph);

        Assert.Equal(graph.Nodes.Count, result.NodeBounds.Count);
        foreach (var node in graph.Nodes)
        {
            Assert.True(result.NodeBounds.TryGetValue(node.Id, out var r), $"Missing bounds for {node.Id}");
            Assert.False(float.IsNaN(r.X) || float.IsNaN(r.Y), $"NaN position for {node.Id}");
            Assert.True(r.Width > 0 && r.Height > 0, $"Degenerate bounds for {node.Id}");
        }
        Assert.True(result.ContentSize.X > 0 && result.ContentSize.Y > 0, "Content size must be positive");
        Assert.NotEmpty(result.ClusterBounds);
    }

    [Fact]
    public void ZoneFirst_ForceMicro_ProducesBoundsForAllNodes()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph, ZoneFirstMicroEngine.Force);

        Assert.Equal(graph.Nodes.Count, result.NodeBounds.Count);
        Assert.NotEmpty(result.ClusterBounds);
    }

    [Fact]
    public void ZoneFirst_ClusterRectsNeverOverlap()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph);

        // Any two cluster rects that overlap by more than a rounding epsilon
        // must be in a containment relationship (parent encloses child). With
        // the zone-first engine this must hold by construction at every level.
        var rects = result.ClusterBounds.Values.ToList();
        for (int i = 0; i < rects.Count; i++)
        for (int j = i + 1; j < rects.Count; j++)
        {
            var a = rects[i];
            var b = rects[j];
            float overlapW = Math.Min(a.xMax, b.xMax) - Math.Max(a.xMin, b.xMin);
            float overlapH = Math.Min(a.yMax, b.yMax) - Math.Max(a.yMin, b.yMin);
            if (overlapW <= 2f || overlapH <= 2f) continue;

            bool containment =
                (a.xMin <= b.xMin + 2f && a.yMin <= b.yMin + 2f && a.xMax >= b.xMax - 2f && a.yMax >= b.yMax - 2f) ||
                (b.xMin <= a.xMin + 2f && b.yMin <= a.yMin + 2f && b.xMax >= a.xMax - 2f && b.yMax >= a.yMax - 2f);
            Assert.True(containment,
                $"Cluster rects overlap without containment: {a.X},{a.Y} {a.Width}x{a.Height} vs {b.X},{b.Y} {b.Width}x{b.Height}");
        }
    }

    [Fact]
    public void ZoneFirst_NodesAreInsideTheirClusterRect()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph);

        // Compose-math invariant: the reserved zone box (what the macro step
        // placed) equals the final cluster rect, so every member node lands
        // strictly inside its cluster.
        foreach (var (nodeId, clusterId) in result.NodeClusterIds)
        {
            if (string.IsNullOrEmpty(clusterId)) continue;
            Assert.True(result.NodeBounds.TryGetValue(nodeId, out var nodeRect), $"Missing node rect for {nodeId}");
            Assert.True(result.ClusterBounds.TryGetValue(clusterId, out var clusterRect), $"Missing cluster rect {clusterId}");

            const float eps = 2f;
            Assert.True(nodeRect.xMin >= clusterRect.xMin - eps && nodeRect.xMax <= clusterRect.xMax + eps &&
                        nodeRect.yMin >= clusterRect.yMin - eps && nodeRect.yMax <= clusterRect.yMax + eps,
                $"Node {nodeId} not inside its cluster {clusterId}: node {nodeRect.X},{nodeRect.Y} {nodeRect.Width}x{nodeRect.Height}, cluster {clusterRect.X},{clusterRect.Y} {clusterRect.Width}x{clusterRect.Height}");
        }
    }

    [Fact]
    public void ZoneFirst_IsDeterministic_SameGraphSameLayout()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var first = RunLayout(graph);
        var second = RunLayout(graph);

        Assert.Equal(first.NodeBounds.Count, second.NodeBounds.Count);
        foreach (var (id, r1) in first.NodeBounds)
        {
            var r2 = second.NodeBounds[id];
            Assert.Equal(r1.X, r2.X, 3);
            Assert.Equal(r1.Y, r2.Y, 3);
        }
    }

    [Fact]
    public void ZoneFirst_CoupledZonesCloserThanUncoupled()
    {
        var graph = ScanSource(TwoZonePairsSource);
        var result = RunLayout(graph);

        Vector2 ZoneCenter(string ns)
        {
            string clusterId = result.ClusterBounds.Keys.Single(k => k.Contains(ns));
            return result.ClusterBounds[clusterId].center;
        }

        float coupled = Vector2.Distance(ZoneCenter("AlphaNs"), ZoneCenter("BetaNs"));
        float across = Vector2.Distance(ZoneCenter("AlphaNs"), ZoneCenter("GammaNs"));

        Assert.True(coupled < across,
            $"Expected coupled AlphaNs–BetaNs zones ({coupled:F1}) closer than uncoupled AlphaNs–GammaNs ({across:F1})");
    }
}
