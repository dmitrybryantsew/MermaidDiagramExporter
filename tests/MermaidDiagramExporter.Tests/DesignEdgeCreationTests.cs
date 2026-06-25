using System.Linq;
using MermaidDiagramExporter.Gui.Design;
using SkiaSharp;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for M4 edge creation operations on DesignCanvasController.
/// Per docs/design/07-implementation-phases.md M4 acceptance criteria.
/// </summary>
public class DesignEdgeCreationTests
{
    private static (DesignGraph, DesignCanvasController) CreateGraphWithTwoClasses()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass
        {
            Id = "c1",
            Name = "Animal",
            X = 100, Y = 100, Width = 200, Height = 100
        });
        graph.Classes.Add(new DesignClass
        {
            Id = "c2",
            Name = "Dog",
            X = 400, Y = 100, Width = 200, Height = 100
        });

        var modeController = new DesignModeController();
        var canvasController = new DesignCanvasController(modeController);
        return (graph, canvasController);
    }

    // ── AddEdge ──

    [Fact]
    public void AddEdge_BetweenExistingClasses_AddsEdge()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();

        var added = controller.AddEdge(graph, "c1", "c2", EdgeKind.Association);

        Assert.True(added);
        Assert.Single(graph.Edges);
        Assert.Equal("c1", graph.Edges[0].FromClassId);
        Assert.Equal("c2", graph.Edges[0].ToClassId);
        Assert.Equal(EdgeKind.Association, graph.Edges[0].Kind);
    }

    [Fact]
    public void AddEdge_InheritanceEdge_AddedWithCorrectKind()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();

        controller.AddEdge(graph, "c2", "c1", EdgeKind.Inheritance);

        Assert.Single(graph.Edges);
        Assert.Equal(EdgeKind.Inheritance, graph.Edges[0].Kind);
    }

    [Fact]
    public void AddEdge_SelfLoop_ReturnsFalse()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();

        var added = controller.AddEdge(graph, "c1", "c1", EdgeKind.Association);

        Assert.False(added);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void AddEdge_MissingSourceClass_ReturnsFalse()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();

        var added = controller.AddEdge(graph, "nonexistent", "c2", EdgeKind.Association);

        Assert.False(added);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void AddEdge_MissingTargetClass_ReturnsFalse()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();

        var added = controller.AddEdge(graph, "c1", "nonexistent", EdgeKind.Association);

        Assert.False(added);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void AddEdge_FiresGraphMutated()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        int fireCount = 0;
        controller.GraphMutated += (_, _) => fireCount++;

        controller.AddEdge(graph, "c1", "c2", EdgeKind.Association);

        Assert.True(fireCount >= 1);
    }

    [Fact]
    public void AddEdge_FailedAdd_DoesNotFireGraphMutated()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        int fireCount = 0;
        controller.GraphMutated += (_, _) => fireCount++;

        controller.AddEdge(graph, "nonexistent", "c2", EdgeKind.Association);

        Assert.Equal(0, fireCount);
    }

    // ── RemoveEdge ──

    [Fact]
    public void RemoveEdge_ExistingEdge_RemovesIt()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.AddEdge(graph, "c1", "c2", EdgeKind.Association);
        var edgeId = graph.Edges[0].Id;

        var removed = controller.RemoveEdge(graph, edgeId);

        Assert.True(removed);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void RemoveEdge_NonExistentId_ReturnsFalse()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.AddEdge(graph, "c1", "c2", EdgeKind.Association);

        var removed = controller.RemoveEdge(graph, "nonexistent");

        Assert.False(removed);
        Assert.Single(graph.Edges);
    }

    // ── ChangeEdgeType ──

    [Fact]
    public void ChangeEdgeType_AssociationToInheritance_UpdatesKind()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.AddEdge(graph, "c1", "c2", EdgeKind.Association);
        var edgeId = graph.Edges[0].Id;

        var changed = controller.ChangeEdgeType(graph, edgeId, EdgeKind.Inheritance);

        Assert.True(changed);
        Assert.Equal(EdgeKind.Inheritance, graph.Edges[0].Kind);
    }

    [Fact]
    public void ChangeEdgeType_NonExistentId_ReturnsFalse()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();

        var changed = controller.ChangeEdgeType(graph, "nonexistent", EdgeKind.Inheritance);

        Assert.False(changed);
    }

    // ── FindEdgeBetween ──

    [Fact]
    public void FindEdgeBetween_ExistingEdge_ReturnsEdge()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.AddEdge(graph, "c1", "c2", EdgeKind.Association);

        var edge = controller.FindEdgeBetween(graph, "c1", "c2");

        Assert.NotNull(edge);
        Assert.Equal(EdgeKind.Association, edge.Kind);
    }

    [Fact]
    public void FindEdgeBetween_NonExistent_ReturnsNull()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();

        var edge = controller.FindEdgeBetween(graph, "c1", "c2");

        Assert.Null(edge);
    }

    [Fact]
    public void FindEdgeBetween_ReverseDirection_ReturnsNull()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.AddEdge(graph, "c1", "c2", EdgeKind.Association);

        // Edge is c1→c2; reverse c2→c1 should not match
        var edge = controller.FindEdgeBetween(graph, "c2", "c1");

        Assert.Null(edge);
    }

    // ── Edge creation state machine ──

    [Fact]
    public void IsCreatingEdge_DefaultFalse()
    {
        var (_, controller) = CreateGraphWithTwoClasses();
        Assert.False(controller.IsCreatingEdge);
    }

    [Fact]
    public void IsCreatingEdge_AfterPortClick_True()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        // Click on right port of c1 (at world pos 300, 150)
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.True(controller.IsCreatingEdge);
    }

    [Fact]
    public void IsCreatingEdge_AfterPointerMove_StillTrue()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());
        controller.HandlePointerMoved(new SKPoint(400, 200));

        Assert.True(controller.IsCreatingEdge);
    }

    [Fact]
    public void CancelEdgeCreation_ClearsState()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());
        Assert.True(controller.IsCreatingEdge);

        controller.CancelEdgeCreation();

        Assert.False(controller.IsCreatingEdge);
    }

    [Fact]
    public void GetEdgeCreationPreview_AfterPortClick_ReturnsState()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());

        var preview = controller.GetEdgeCreationPreview();

        Assert.NotNull(preview);
        Assert.Equal("c1", preview.SourceRectangle.ClassId);
        Assert.True(preview.SourceIsRightPort);
    }

    [Fact]
    public void GetEdgeCreationPreview_NoActiveEdge_ReturnsNull()
    {
        var (_, controller) = CreateGraphWithTwoClasses();
        Assert.Null(controller.GetEdgeCreationPreview());
    }

    [Fact]
    public void HandlePointerReleased_OnTargetPort_CreatesEdge()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        // Start edge from right port of c1
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());
        // Release on left port of c2 (at world pos 400, 150)
        controller.HandlePointerReleased(graph, new SKPoint(400, 150));

        Assert.Single(graph.Edges);
        Assert.Equal("c1", graph.Edges[0].FromClassId);
        Assert.Equal("c2", graph.Edges[0].ToClassId);
        Assert.False(controller.IsCreatingEdge);
    }

    [Fact]
    public void HandlePointerReleased_OnEmptyCanvas_DoesNotCreateEdge()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());
        // Release on empty canvas (far from any class)
        controller.HandlePointerReleased(graph, new SKPoint(1000, 1000));

        Assert.Empty(graph.Edges);
        Assert.False(controller.IsCreatingEdge);
    }

    [Fact]
    public void HandlePointerReleased_OnSameClass_DoesNotCreateEdge()
    {
        var (graph, controller) = CreateGraphWithTwoClasses();
        // Start from right port of c1
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());
        // Release on left port of c1 (self-edge attempt)
        controller.HandlePointerReleased(graph, new SKPoint(100, 150));

        Assert.Empty(graph.Edges);
        Assert.False(controller.IsCreatingEdge);
    }
}
