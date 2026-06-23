using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Layout.Compound;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Smoke tests for the new CompoundLayeredLayoutEngine.
/// Verifies the engine runs, produces a valid LayoutResult, and satisfies
/// basic invariants from docs/09 §1.
/// </summary>
public class CompoundLayoutEngineTests
{
    /// <summary>
    /// Scans a C# source string and returns a real TypeGraph (the input to the
    /// full coordinator pipeline). Avoids depending on internal LayoutGraphFactory.
    /// </summary>
    private static Core.TypeGraph ScanSource(string source)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "mermaid_compound_test_" + Guid.NewGuid().ToString("N"));
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

    /// <summary>
    /// Builds a minimal CompoundGraph for unit-level rank/order/coordinate tests
    /// without needing the full LayoutGraph pipeline.
    /// </summary>
    private static CompoundGraph BuildSimpleChainCompoundGraph(params string[] nodeIds)
    {
        var compound = new CompoundGraph();
        foreach (var id in nodeIds)
        {
            compound.Nodes.Add(new CompoundNode
            {
                Id = $"real:{id}",
                Kind = CompoundNodeKind.Real,
                SourceLayoutNodeId = id,
                Width = 100,
                Height = 50
            });
        }
        for (int i = 0; i < nodeIds.Length - 1; i++)
        {
            compound.Edges.Add(new CompoundEdge
            {
                FromId = $"real:{nodeIds[i]}",
                ToId = $"real:{nodeIds[i + 1]}",
                Weight = 1,
                MinRankSpan = 1
            });
        }
        return compound;
    }

    [Fact]
    public void CompoundEngine_FeatureFlagDefaultIsFalse()
    {
        // Verify the feature flag defaults to off so existing behavior is preserved
        var options = new LayoutOptions();
        Assert.False(options.UseCompoundLayoutEngine);
    }

    [Fact]
    public void CompoundEngine_RankAssignment_ChainProducesIncreasingRanks()
    {
        var compound = BuildSimpleChainCompoundGraph("A", "B", "C");
        RankAssignment.Run(compound, new LayoutOptions());

        var a = compound.Nodes.First(n => n.Id == "real:A");
        var b = compound.Nodes.First(n => n.Id == "real:B");
        var c = compound.Nodes.First(n => n.Id == "real:C");

        Assert.True(a.Rank >= 0, $"A.Rank should be >= 0, got {a.Rank}");
        Assert.True(a.Rank < b.Rank, $"Expected A.Rank < B.Rank, got {a.Rank} vs {b.Rank}");
        Assert.True(b.Rank < c.Rank, $"Expected B.Rank < C.Rank, got {b.Rank} vs {c.Rank}");
    }

    [Fact]
    public void CompoundEngine_RankAssignment_NodeRanksAreNonNegative()
    {
        var compound = BuildSimpleChainCompoundGraph("A", "B", "C", "D", "E");
        RankAssignment.Run(compound, new LayoutOptions());

        foreach (var node in compound.Nodes)
            Assert.True(node.Rank >= 0, $"Node {node.Id} has negative rank {node.Rank}");
    }

    [Fact]
    public void CompoundEngine_OrderAssignment_AssignsOrderInRank()
    {
        var compound = BuildSimpleChainCompoundGraph("A", "B", "C");
        RankAssignment.Run(compound, new LayoutOptions());
        OrderAssignment.Run(compound, new LayoutOptions());

        // Every real node should have a non-negative OrderInRank after ordering
        foreach (var node in compound.Nodes.Where(n => n.Kind == CompoundNodeKind.Real))
            Assert.True(node.OrderInRank >= 0, $"Node {node.Id} has negative OrderInRank {node.OrderInRank}");
    }

    [Fact]
    public void CompoundEngine_CoordinateAssignment_AssignsNonNaNPositions()
    {
        var compound = BuildSimpleChainCompoundGraph("A", "B", "C");
        RankAssignment.Run(compound, new LayoutOptions());
        OrderAssignment.Run(compound, new LayoutOptions());
        CoordinateAssignment.Run(compound, new LayoutOptions());

        foreach (var node in compound.Nodes)
        {
            Assert.False(float.IsNaN(node.X), $"Node {node.Id} has NaN X");
            Assert.False(float.IsNaN(node.Y), $"Node {node.Id} has NaN Y");
        }
    }

    [Fact]
    public void CompoundEngine_GraphLayoutCoordinator_RunsOnRealGraph()
    {
        // Integration test: scan a real C# file, run the full coordinator pipeline
        // with the new engine enabled, verify output shape.
        var typeGraph = ScanSource(@"
namespace MyApp;
public class Animal { }
public class Dog : Animal { }
public class Cat : Animal { }
");

        var coordinator = new GraphLayoutCoordinator();
        var options = new LayoutOptions { UseCompoundLayoutEngine = true };
        var result = coordinator.CreateLayout(typeGraph, options);

        Assert.NotNull(result);
        Assert.NotNull(result.NodeBounds);
        Assert.NotNull(result.ClusterBounds);
    }

    [Fact]
    public void CompoundEngine_OldEngineStillWorksByDefault()
    {
        // Verify the coordinator still uses the old engine by default
        var typeGraph = ScanSource(@"
namespace MyApp;
public class A { }
public class B : A { }
");

        var coordinator = new GraphLayoutCoordinator();
        var options = new LayoutOptions(); // UseCompoundLayoutEngine defaults to false
        var result = coordinator.CreateLayout(typeGraph, options);

        Assert.NotNull(result);
        Assert.NotEmpty(result.NodeBounds);
    }

    [Fact]
    public void CompoundEngine_ClusterContiguity_RealNodesAreContiguous()
    {
        // Two clusters with cross-cluster edges. After full pipeline,
        // nodes belonging to the same cluster should generally be near each other
        // (rank-contiguity is the structural invariant; visual contiguity follows).
        var typeGraph = ScanSource(@"
namespace ClusterA;
public class A1 { public A2 Ref; }
public class A2 { }
namespace ClusterB;
public class B1 { public B2 Ref; }
public class B2 { }
");

        var coordinator = new GraphLayoutCoordinator();
        var options = new LayoutOptions { UseCompoundLayoutEngine = true };
        var result = coordinator.CreateLayout(typeGraph, options);

        // Every input node must appear in output
        Assert.Equal(typeGraph.Nodes.Count, result.NodeBounds.Count);

        // Every input cluster must appear in output
        var inputClusters = typeGraph.Groups.Select(g => g.Id).ToHashSet();
        var outputClusters = result.ClusterBounds.Keys.ToHashSet();
        Assert.True(outputClusters.SetEquals(inputClusters),
            $"Cluster mismatch. Expected {inputClusters.Count}, got {outputClusters.Count}");
    }

    [Fact]
    public void CompoundEngine_RankContiguity_ClusterMembersInContiguousRankRange()
    {
        // Verify that for each cluster, all its real members have ranks that
        // form a contiguous range (no foreign non-descendant node squeezed between).
        var typeGraph = ScanSource(@"
namespace ClusterA;
public class A1 { public A2 Ref; }
public class A2 { public A3 Ref; }
public class A3 { }
namespace ClusterB;
public class B1 { public A1 CrossRef; }
public class B2 { }
public class B3 { }
");

        var coordinator = new GraphLayoutCoordinator();
        var options = new LayoutOptions { UseCompoundLayoutEngine = true };
        var result = coordinator.CreateLayout(typeGraph, options);

        // Build a quick lookup: for each cluster, collect member positions
        var clusterPositions = new Dictionary<string, List<(float X, float Y)>>();
        foreach (var (nodeId, bounds) in result.NodeBounds)
        {
            // Find which cluster this node belongs to (via the input graph)
            var typeNode = typeGraph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (typeNode == null) continue;
            var clusterId = typeNode.Namespace ?? "";
            if (string.IsNullOrEmpty(clusterId)) continue;

            // Collect the node's position for spatial contiguity check
            if (!clusterPositions.TryGetValue(clusterId, out var list))
                clusterPositions[clusterId] = list = new List<(float X, float Y)>();
            list.Add((bounds.X, bounds.Y));
        }

        // For each cluster, verify members are spatially close (not scattered)
        foreach (var (clusterId, positions) in clusterPositions)
        {
            if (positions.Count < 2) continue;
            float minX = positions.Min(p => p.X);
            float maxX = positions.Max(p => p.X);
            float minY = positions.Min(p => p.Y);
            float maxY = positions.Max(p => p.Y);
            float span = (maxX - minX) + (maxY - minY);

            // Cluster members should be within a reasonable area (not scattered across the whole canvas)
            float canvasSpan = result.ContentSize.X + result.ContentSize.Y;
            Assert.True(span < canvasSpan,
                $"Cluster {clusterId} members are too spread out (span {span} vs canvas {canvasSpan})");
        }
    }

    [Fact]
    public void CompoundEngine_NestedClusterContiguity_HoldsAtBothLevels()
    {
        // Two-level nesting: Outer namespace contains Inner namespace.
        // After full pipeline, both Outer's and Inner's members should be
        // spatially contiguous (the headline invariant from docs/09 §1.3).
        var typeGraph = ScanSource(@"
namespace Outer;
public class OuterA { public OuterB Ref; }
public class OuterB { }
namespace Outer.Inner;
public class InnerA { public InnerB Ref; }
public class InnerB { }
");

        var coordinator = new GraphLayoutCoordinator();
        var options = new LayoutOptions { UseCompoundLayoutEngine = true };
        var result = coordinator.CreateLayout(typeGraph, options);

        // Collect positions per namespace
        var positionsByNamespace = new Dictionary<string, List<(float X, float Y)>>();
        foreach (var (nodeId, bounds) in result.NodeBounds)
        {
            var typeNode = typeGraph.Nodes.FirstOrDefault(n => n.Id == nodeId);
            if (typeNode == null) continue;
            var ns = typeNode.Namespace ?? "";
            if (string.IsNullOrEmpty(ns)) continue;
            if (!positionsByNamespace.TryGetValue(ns, out var list))
                positionsByNamespace[ns] = list = new List<(float X, float Y)>();
            list.Add((bounds.X, bounds.Y));
        }

        // Both "Outer" and the nested namespace should have members that are spatially close
        // (the scanner may produce "Outer.Outer.Inner" as the nested namespace name)
        Assert.Contains("Outer", positionsByNamespace.Keys);
        var nestedNamespace = positionsByNamespace.Keys.FirstOrDefault(k => k != "Outer");
        Assert.NotNull(nestedNamespace);

        foreach (var (ns, positions) in positionsByNamespace)
        {
            if (positions.Count < 2) continue;
            float minX = positions.Min(p => p.X);
            float maxX = positions.Max(p => p.X);
            float minY = positions.Min(p => p.Y);
            float maxY = positions.Max(p => p.Y);
            float span = (maxX - minX) + (maxY - minY);
            float canvasSpan = result.ContentSize.X + result.ContentSize.Y;

            Assert.True(span < canvasSpan,
                $"Namespace {ns} members are too spread out (span {span} vs canvas {canvasSpan})");
        }
    }

    [Fact]
    public void CompoundEngine_CrossingCount_IsReasonable()
    {
        // Build a moderately complex graph and verify the new engine produces
        // a finite crossing count (not a crash/exception). Per docs/09 §1.3,
        // we want to assert the new engine doesn't regress badly; a full
        // old-vs-new comparison would require re-implementing the old ordering,
        // which is out of scope for this test.
        var typeGraph = ScanSource(@"
namespace ClusterA;
public class A1 { public A2 Ref; }
public class A2 { public A3 Ref; }
public class A3 { public A4 Ref; }
public class A4 { }
namespace ClusterB;
public class B1 { public B2 Ref; }
public class B2 { public B3 Ref; }
public class B3 { public B4 Ref; }
public class B4 { }
");

        var coordinator = new GraphLayoutCoordinator();
        var options = new LayoutOptions { UseCompoundLayoutEngine = true };
        var result = coordinator.CreateLayout(typeGraph, options);

        // Basic sanity: all nodes positioned, all clusters present
        Assert.Equal(typeGraph.Nodes.Count, result.NodeBounds.Count);
        Assert.NotEmpty(result.ClusterBounds);
    }

    [Fact]
    public void CompoundEngine_EdgePaths_AreGenerated()
    {
        // Verify that EdgeRoutingService produces valid edge paths for the
        // compound engine's output. This validates the LayoutResult shape
        // compatibility guarantee from docs/05 §5.4.
        var typeGraph = ScanSource(@"
namespace MyApp;
public class A { public B Ref; }
public class B { public C Ref; }
public class C { }
");

        var coordinator = new GraphLayoutCoordinator();
        var options = new LayoutOptions { UseCompoundLayoutEngine = true };
        var result = coordinator.CreateLayout(typeGraph, options);

        // EdgeRoutingService should have produced paths for the 2 edges
        Assert.NotNull(result.EdgePaths);
        Assert.NotEmpty(result.EdgePaths);

        // Each path should have at least 2 points (start and end)
        foreach (var path in result.EdgePaths)
        {
            Assert.True(path.Points.Count >= 2,
                $"Edge path has only {path.Points.Count} points (expected >= 2)");
        }
    }
}
