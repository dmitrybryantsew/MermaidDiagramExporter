using System;
using System.IO;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Gui.Layout;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Smoke and behavior tests for the MSAGL engine variants
/// (<see cref="LayoutEngineKind.Msagl"/>, <see cref="LayoutEngineKind.MsaglMds"/>)
/// and the own force-directed engine (<see cref="LayoutEngineKind.Force"/>).
/// </summary>
public class MsaglEngineVariantTests
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

    private const string TwoPairsSource = """
        namespace PairOne
        {
            public class Alpha { public Beta B; }
            public class Beta { }
        }
        namespace PairTwo
        {
            public class Gamma { public Delta D; }
            public class Delta { }
        }
        """;

    /// <summary>
    /// Scans a C# source string and returns a real TypeGraph (same pattern as
    /// CompoundLayoutEngineTests.ScanSource).
    /// </summary>
    private static Core.TypeGraph ScanSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mermaid_msagl_test_" + Guid.NewGuid().ToString("N"));
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

    private static LayoutResult RunLayout(Core.TypeGraph graph, LayoutEngineKind kind)
    {
        var coordinator = new GraphLayoutCoordinator();
        return coordinator.CreateLayout(graph, new LayoutOptions { Engine = kind });
    }

    private static void AssertValidLayoutForAllNodes(Core.TypeGraph graph, LayoutResult result)
    {
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
    public void Sugiyama_ProducesBoundsForAllNodes()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph, LayoutEngineKind.Msagl);
        AssertValidLayoutForAllNodes(graph, result);
    }

    [Fact]
    public void Mds_ProducesBoundsForAllNodes()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph, LayoutEngineKind.MsaglMds);
        AssertValidLayoutForAllNodes(graph, result);
    }

    [Fact]
    public void Force_ProducesBoundsForAllNodes()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph, LayoutEngineKind.Force);
        AssertValidLayoutForAllNodes(graph, result);
    }

    [Fact]
    public void Force_PreventClusterOverlap_ClusterRectsDoNotOverlap()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var result = RunLayout(graph, LayoutEngineKind.Force);

        // Any two cluster rects that overlap by more than a rounding epsilon
        // must be in a containment relationship (parent encloses child).
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
    public void Force_IsDeterministic_SameSeedSameLayout()
    {
        var graph = ScanSource(MultiNamespaceSource);
        var first = RunLayout(graph, LayoutEngineKind.Force);
        var second = RunLayout(graph, LayoutEngineKind.Force);

        Assert.Equal(first.NodeBounds.Count, second.NodeBounds.Count);
        foreach (var (id, r1) in first.NodeBounds)
        {
            var r2 = second.NodeBounds[id];
            Assert.Equal(r1.X, r2.X, 3);
            Assert.Equal(r1.Y, r2.Y, 3);
        }
    }

    [Theory]
    [InlineData(LayoutEngineKind.MsaglMds)]
    [InlineData(LayoutEngineKind.Force)]
    public void ConnectedPair_IsCloserThanUnconnectedPair(LayoutEngineKind kind)
    {
        var graph = ScanSource(TwoPairsSource);
        var result = RunLayout(graph, kind);

        string IdOf(string name) => graph.Nodes.Single(n => n.DisplayName == name).Id;
        Vector2 Center(string name) => result.NodeBounds[IdOf(name)].center;

        float withinPair = Vector2.Distance(Center("Alpha"), Center("Beta"));
        float acrossPairs = Vector2.Distance(Center("Alpha"), Center("Gamma"));

        Assert.True(withinPair < acrossPairs,
            $"Expected connected Alpha–Beta ({withinPair:F1}) closer than unconnected Alpha–Gamma ({acrossPairs:F1})");
    }

    [Fact]
    public void EdgeWeights_IntWeights_PreserveOrdering()
    {
        int inheritance = LayoutEdgeWeights.GetIntWeight(TypeEdgeKind.Inheritance);
        int implements = LayoutEdgeWeights.GetIntWeight(TypeEdgeKind.Implements);
        int association = LayoutEdgeWeights.GetIntWeight(TypeEdgeKind.Association);

        Assert.True(inheritance > implements, $"Inheritance ({inheritance}) should outweigh Implements ({implements})");
        Assert.True(implements > association, $"Implements ({implements}) should outweigh Association ({association})");
    }

    [Fact]
    public void EngineKindClassification_IsConsistent()
    {
        Assert.True(LayoutEngineKind.Msagl.IsMsaglFamily());
        Assert.True(LayoutEngineKind.MsaglMds.IsMsaglFamily());
        Assert.False(LayoutEngineKind.Force.IsMsaglFamily());
        Assert.False(LayoutEngineKind.Layered.IsMsaglFamily());
        Assert.False(LayoutEngineKind.Compound.IsMsaglFamily());
        Assert.False(LayoutEngineKind.ZoneFirst.IsMsaglFamily());

        Assert.True(LayoutEngineKind.Force.UsesMinimalPrepPipeline());
        Assert.True(LayoutEngineKind.Msagl.UsesMinimalPrepPipeline());
        Assert.True(LayoutEngineKind.ZoneFirst.UsesMinimalPrepPipeline());
        Assert.False(LayoutEngineKind.Layered.UsesMinimalPrepPipeline());
    }
}
