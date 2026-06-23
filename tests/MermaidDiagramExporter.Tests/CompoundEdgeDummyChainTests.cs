using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Layout.Compound;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Unit-level tests for the edge dummy chain projection (docs/08 Part B).
/// Tests RankAssignment.InsertEdgeSegmentDummies and CompoundResultProjector
/// directly without going through the full coordinator pipeline.
/// </summary>
public class CompoundEdgeDummyChainTests
{
    [Fact]
    public void InsertEdgeSegmentDummies_CreatesChainForLongEdge()
    {
        // Build a compound graph with 4 nodes (A, B, C, D) and one long edge A→D
        // that spans 3 ranks (A=0, B=1, C=2, D=3)
        var compound = new CompoundGraph();
        compound.Nodes.Add(new CompoundNode { Id = "real:A", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "A", Width = 100, Height = 50, Rank = 0 });
        compound.Nodes.Add(new CompoundNode { Id = "real:B", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "B", Width = 100, Height = 50, Rank = 1 });
        compound.Nodes.Add(new CompoundNode { Id = "real:C", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "C", Width = 100, Height = 50, Rank = 2 });
        compound.Nodes.Add(new CompoundNode { Id = "real:D", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "D", Width = 100, Height = 50, Rank = 3 });

        // Long edge A→D spanning 3 ranks
        compound.Edges.Add(new CompoundEdge
        {
            FromId = "real:A",
            ToId = "real:D",
            Weight = 1,
            MinRankSpan = 1,
            Kind = TypeEdgeKind.Association,
            OriginalLayoutEdgeId = "A->D:Association"
        });

        // Call InsertEdgeSegmentDummies directly (RankAssignment.Run would reset ranks to 0)
        var method = typeof(RankAssignment).GetMethod("InsertEdgeSegmentDummies",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method!.Invoke(null, new object[] { compound });

        // After InsertEdgeSegmentDummies, a dummy chain should be recorded
        Assert.NotEmpty(compound.EdgeDummyChains);

        var chainEntry = compound.EdgeDummyChains.First();
        var chain = chainEntry.Value;
        Assert.Equal(4, chain.Count); // A, dummy1, dummy2, D (3 ranks apart = 2 dummies + 2 endpoints)
        Assert.Equal("real:A", chain.First());
        Assert.Equal("real:D", chain.Last());
    }

    [Fact]
    public void InsertEdgeSegmentDummies_NoChainForSingleRankEdge()
    {
        // Build a compound graph with 2 nodes and one edge spanning exactly 1 rank
        var compound = new CompoundGraph();
        compound.Nodes.Add(new CompoundNode { Id = "real:A", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "A", Width = 100, Height = 50, Rank = 0 });
        compound.Nodes.Add(new CompoundNode { Id = "real:B", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "B", Width = 100, Height = 50, Rank = 1 });

        compound.Edges.Add(new CompoundEdge
        {
            FromId = "real:A",
            ToId = "real:B",
            Weight = 1,
            MinRankSpan = 1,
            Kind = TypeEdgeKind.Association
        });

        RankAssignment.Run(compound, new LayoutOptions());

        // No dummy chains should be created for a single-rank edge
        Assert.Empty(compound.EdgeDummyChains);
    }

    [Fact]
    public void CompoundResultProjector_BuildsEdgeDummyPaths()
    {
        // Build a compound graph with a long edge, run the full pipeline,
        // and verify the projector produces edge dummy paths with correct positions
        var compound = new CompoundGraph();

        // Add cluster borders (required for the projector)
        compound.Nodes.Add(new CompoundNode { Id = "borderTop:C1", Kind = CompoundNodeKind.ClusterBorderTop, OwningClusterId = "C1", Width = 0, Height = 0 });
        compound.Nodes.Add(new CompoundNode { Id = "borderBottom:C1", Kind = CompoundNodeKind.ClusterBorderBottom, OwningClusterId = "C1", Width = 0, Height = 0 });
        compound.Nodes.Add(new CompoundNode { Id = "borderTop:C2", Kind = CompoundNodeKind.ClusterBorderTop, OwningClusterId = "C2", Width = 0, Height = 0 });
        compound.Nodes.Add(new CompoundNode { Id = "borderBottom:C2", Kind = CompoundNodeKind.ClusterBorderBottom, OwningClusterId = "C2", Width = 0, Height = 0 });
        compound.ClusterBorders["C1"] = new ClusterBorderChain { ClusterId = "C1" };
        compound.ClusterBorders["C2"] = new ClusterBorderChain { ClusterId = "C2" };
        compound.ClusterParent["C1"] = null;
        compound.ClusterParent["C2"] = null;
        compound.ClusterChildren["C1"] = new List<string>();
        compound.ClusterChildren["C2"] = new List<string>();

        // Add real nodes with positions
        compound.Nodes.Add(new CompoundNode { Id = "real:A", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "A", Width = 100, Height = 50, Rank = 0, OwningClusterId = "C1", X = 0, Y = 0 });
        compound.Nodes.Add(new CompoundNode { Id = "real:B", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "B", Width = 100, Height = 50, Rank = 1, OwningClusterId = "C1", X = 0, Y = 100 });
        compound.Nodes.Add(new CompoundNode { Id = "real:C", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "C", Width = 100, Height = 50, Rank = 2, OwningClusterId = "C2", X = 0, Y = 200 });
        compound.Nodes.Add(new CompoundNode { Id = "real:D", Kind = CompoundNodeKind.Real, SourceLayoutNodeId = "D", Width = 100, Height = 50, Rank = 3, OwningClusterId = "C2", X = 0, Y = 300 });

        // Pre-populate edge dummy chains (as if RankAssignment had run)
        compound.EdgeDummyChains["A->D:Association"] = new List<string> { "real:A", "edgeseg:real:A->real:D:1", "edgeseg:real:A->real:D:2", "real:D" };

        // Add the dummy nodes with positions
        compound.Nodes.Add(new CompoundNode { Id = "edgeseg:real:A->real:D:1", Kind = CompoundNodeKind.EdgeSegment, Rank = 1, Width = 0, Height = 0, X = 50, Y = 100 });
        compound.Nodes.Add(new CompoundNode { Id = "edgeseg:real:A->real:D:2", Kind = CompoundNodeKind.EdgeSegment, Rank = 2, Width = 0, Height = 0, X = 50, Y = 200 });

        // Create a minimal LayoutGraph for the projector
        var layoutGraph = new LayoutGraph
        {
            Clusters = new List<LayoutCluster>
            {
                new LayoutCluster { Id = "C1", Label = "C1" },
                new LayoutCluster { Id = "C2", Label = "C2" }
            }
        };

        // Run the projector
        var result = CompoundResultProjector.Project(compound, layoutGraph, new LayoutOptions());

        // Verify edge dummy paths were built
        Assert.NotNull(result.EdgeDummyPaths);
        Assert.True(result.EdgeDummyPaths.ContainsKey("A->D:Association"));

        var path = result.EdgeDummyPaths["A->D:Association"];
        Assert.Equal(4, path.Count); // A, dummy1, dummy2, D
        Assert.Equal(0f, path[0].X);  // A is at (0,0)
        Assert.Equal(0f, path[0].Y);
        Assert.Equal(50f, path[1].X); // dummy1 at (50,100)
        Assert.Equal(100f, path[1].Y);
        Assert.Equal(50f, path[2].X); // dummy2 at (50,200)
        Assert.Equal(200f, path[2].Y);
        Assert.Equal(0f, path[3].X);  // D at (0,300)
        Assert.Equal(300f, path[3].Y);
    }
}
