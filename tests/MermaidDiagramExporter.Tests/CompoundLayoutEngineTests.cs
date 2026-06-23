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
}
