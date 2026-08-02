using System;
using System.Collections.Generic;
using SkiaSharp;
using Xunit;
using MermaidDiagramExporter.Gui;

namespace MermaidDiagramExporter.Tests;

public class HitTestServiceTests
{
    [Fact]
    public void HitTest_ReturnsNull_WhenNodeListIsEmpty()
    {
        var result = HitTestService.HitTest(new SKPoint(10, 10), new List<GraphNode>());
        Assert.Null(result);
    }

    [Fact]
    public void HitTest_ReturnsNode_WhenPointInside()
    {
        var node = new GraphNode { Id = "1", X = 10, Y = 10, Width = 100, Height = 50 };
        var nodes = new List<GraphNode> { node };

        var result = HitTestService.HitTest(new SKPoint(50, 30), nodes);

        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
    }

    [Theory]
    [InlineData(10, 30)] // Left edge
    [InlineData(110, 30)] // Right edge
    [InlineData(50, 10)] // Top edge
    [InlineData(50, 60)] // Bottom edge
    [InlineData(10, 10)] // Top-left corner
    [InlineData(110, 10)] // Top-right corner
    [InlineData(10, 60)] // Bottom-left corner
    [InlineData(110, 60)] // Bottom-right corner
    public void HitTest_ReturnsNode_WhenPointOnEdges(float hitX, float hitY)
    {
        var node = new GraphNode { Id = "1", X = 10, Y = 10, Width = 100, Height = 50 };
        var nodes = new List<GraphNode> { node };

        var result = HitTestService.HitTest(new SKPoint(hitX, hitY), nodes);

        Assert.NotNull(result);
        Assert.Equal("1", result.Id);
    }

    [Theory]
    [InlineData(9.9f, 30)] // Just left
    [InlineData(110.1f, 30)] // Just right
    [InlineData(50, 9.9f)] // Just above
    [InlineData(50, 60.1f)] // Just below
    public void HitTest_ReturnsNull_WhenPointJustOutsideEdges(float hitX, float hitY)
    {
        var node = new GraphNode { Id = "1", X = 10, Y = 10, Width = 100, Height = 50 };
        var nodes = new List<GraphNode> { node };

        var result = HitTestService.HitTest(new SKPoint(hitX, hitY), nodes);

        Assert.Null(result);
    }

    [Fact]
    public void HitTest_ReturnsTopmostNode_WhenNodesOverlap()
    {
        var node1 = new GraphNode { Id = "1", X = 10, Y = 10, Width = 100, Height = 100 };
        var node2 = new GraphNode { Id = "2", X = 50, Y = 50, Width = 100, Height = 100 };
        var node3 = new GraphNode { Id = "3", X = 50, Y = 50, Width = 100, Height = 100 };
        // node3 is drawn last, so it should be visually on top and hit first.

        var nodes = new List<GraphNode> { node1, node2, node3 };

        // Point is in the overlap of all three nodes
        var result = HitTestService.HitTest(new SKPoint(75, 75), nodes);

        Assert.NotNull(result);
        Assert.Equal("3", result.Id);
    }
}
