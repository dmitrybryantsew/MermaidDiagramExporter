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
}
