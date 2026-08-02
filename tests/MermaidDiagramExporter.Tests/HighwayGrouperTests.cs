using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Gui;
using MermaidDiagramExporter.Gui.Layout;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for <see cref="HighwayGrouper"/>: inter-zone edge grouping into
/// aggregate highways, stroke width scaling, and border anchor geometry.
/// </summary>
public class HighwayGrouperTests
{
    private static GraphNode Node(string id, string ns) => new() { Id = id, Namespace = ns };

    private static GraphEdge Edge(GraphNode from, GraphNode to) => new() { FromNode = from, ToNode = to };

    [Fact]
    public void Group_InterZoneEdges_GroupedByUnorderedPair_WithDirectionCounts()
    {
        var a1 = Node("a1", "Ns.A");
        var a2 = Node("a2", "Ns.A");
        var b1 = Node("b1", "Ns.B");
        var b2 = Node("b2", "Ns.B");

        var edges = new List<GraphEdge>
        {
            Edge(a1, b1), // A → B
            Edge(a2, b2), // A → B
            Edge(b1, a1), // B → A
        };

        var (intra, highways) = HighwayGrouper.Group(edges);

        Assert.Empty(intra);
        var hw = Assert.Single(highways);
        Assert.Equal("Ns.A", hw.ZoneA);
        Assert.Equal("Ns.B", hw.ZoneB);
        Assert.Equal(2, hw.ForwardCount);
        Assert.Equal(1, hw.BackwardCount);
        Assert.Equal(3, hw.TotalCount);
        Assert.Equal(3, hw.Edges.Count);
    }

    [Fact]
    public void Group_IntraZoneAndUngroupedEdges_StayIntra()
    {
        var a1 = Node("a1", "Ns.A");
        var a2 = Node("a2", "Ns.A");
        var b1 = Node("b1", "Ns.B");
        var loose = Node("loose", "");

        var edges = new List<GraphEdge>
        {
            Edge(a1, a2),   // same namespace
            Edge(a1, loose), // endpoint without namespace
            Edge(a1, b1),   // the only inter-zone edge
        };

        var (intra, highways) = HighwayGrouper.Group(edges);

        Assert.Equal(2, intra.Count);
        Assert.Single(highways);
    }

    [Fact]
    public void IsInterZone_MatchesGroupingCriterion()
    {
        var a = Node("a", "Ns.A");
        var b = Node("b", "Ns.B");
        var a2 = Node("a2", "Ns.A");
        var loose = Node("l", "");

        Assert.True(HighwayGrouper.IsInterZone(Edge(a, b)));
        Assert.False(HighwayGrouper.IsInterZone(Edge(a, a2)));
        Assert.False(HighwayGrouper.IsInterZone(Edge(a, loose)));
    }

    [Fact]
    public void IsInterZone_WithNullNodes_ReturnsFalse()
    {
        var a = Node("a", "Ns.A");

        var edgeNullFrom = new GraphEdge { FromNode = null, ToNode = a };
        var edgeNullTo = new GraphEdge { FromNode = a, ToNode = null };
        var edgeBothNull = new GraphEdge { FromNode = null, ToNode = null };

        Assert.False(HighwayGrouper.IsInterZone(edgeNullFrom));
        Assert.False(HighwayGrouper.IsInterZone(edgeNullTo));
        Assert.False(HighwayGrouper.IsInterZone(edgeBothNull));
    }

    [Fact]
    public void IsInterZone_WithNullOrEmptyNamespaces_ReturnsFalse()
    {
        var a = Node("a", "Ns.A");
        var nullNs = Node("nullNs", null!);
        var emptyNs = Node("emptyNs", "");

        Assert.False(HighwayGrouper.IsInterZone(Edge(a, nullNs)));
        Assert.False(HighwayGrouper.IsInterZone(Edge(nullNs, a)));
        Assert.False(HighwayGrouper.IsInterZone(Edge(nullNs, nullNs)));

        Assert.False(HighwayGrouper.IsInterZone(Edge(a, emptyNs)));
        Assert.False(HighwayGrouper.IsInterZone(Edge(emptyNs, a)));
        Assert.False(HighwayGrouper.IsInterZone(Edge(emptyNs, emptyNs)));
    }

    [Fact]
    public void ComputeStrokeWidth_GrowsLogarithmically_CappedAt10()
    {
        Assert.True(HighwayGrouper.ComputeStrokeWidth(1) < HighwayGrouper.ComputeStrokeWidth(4));
        Assert.True(HighwayGrouper.ComputeStrokeWidth(4) < HighwayGrouper.ComputeStrokeWidth(16));
        Assert.Equal(10f, HighwayGrouper.ComputeStrokeWidth(100000));
        Assert.Equal(2f, HighwayGrouper.ComputeStrokeWidth(0)); // degenerate input clamps to base width
    }

    [Fact]
    public void BorderPointToward_PointToTheRight_ExitsAtRightSide()
    {
        var rect = new Rect(100, 100, 200, 100); // center (200, 150)
        var p = HighwayGrouper.BorderPointToward(rect, new Vector2(500, 150));

        Assert.Equal(300f, p.X); // xMax
        Assert.Equal(150f, p.Y);
    }

    [Fact]
    public void BorderPointToward_PointAbove_ExitsAtTopSide()
    {
        var rect = new Rect(100, 100, 200, 100); // center (200, 150)
        var p = HighwayGrouper.BorderPointToward(rect, new Vector2(200, -50));

        Assert.Equal(200f, p.X);
        Assert.Equal(100f, p.Y); // yMin
    }

    [Fact]
    public void BorderPointToward_DiagonalPoint_ClipsOnCorrectSide()
    {
        var rect = new Rect(0, 0, 100, 100); // center (50, 50)
        var p = HighwayGrouper.BorderPointToward(rect, new Vector2(250, 150)); // direction (200,100)

        // Ray hits xMax=100 at t=0.25 → y = 50 + 100*0.25 = 75 (before yMax)
        Assert.Equal(100f, p.X);
        Assert.Equal(75f, p.Y);
    }

    [Fact]
    public void BorderPointToward_TargetInsideRect_ReturnsCenter()
    {
        var rect = new Rect(0, 0, 100, 100);
        var p = HighwayGrouper.BorderPointToward(rect, new Vector2(60, 60));

        Assert.Equal(50f, p.X);
        Assert.Equal(50f, p.Y);
    }
}
