using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Gui;
using MermaidDiagramExporter.Gui.Design;
using SkiaSharp;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for the marquee + move-scope feature (UIContract §5 + §11):
/// MoveScopeResolver neighbor computation, DesignCanvasController marquee
/// SetSelectionFromIds, and multi-drag behavior in both SelectedOnly and
/// SelectedPlusRelated1D scopes.
/// </summary>
public class MarqueeAndMoveScopeTests
{
    // ── MoveScopeResolver ──

    [Fact]
    public void ResolveMoveSet_SelectedOnly_ReturnsJustSelectionWhenDraggedIsSelected()
    {
        var edges = new[] { ("a", "b"), ("b", "c") };
        var set = MoveScopeResolver.ResolveMoveSet(
            "a", new[] { "a", "b" }, MoveScope.SelectedOnly, edges);
        Assert.Equal(new HashSet<string> { "a", "b" }, set);
    }

    [Fact]
    public void ResolveMoveSet_SelectedOnly_ReturnsJustDraggedWhenNotInSelection()
    {
        var edges = new[] { ("a", "b") };
        var set = MoveScopeResolver.ResolveMoveSet(
            "a", new[] { "x", "y" }, MoveScope.SelectedOnly, edges);
        Assert.Equal(new HashSet<string> { "a" }, set);
    }

    [Fact]
    public void ResolveMoveSet_PlusRelated1D_AddsDirectUndirectedNeighbors()
    {
        // a—b—c ; selecting {a} and dragging a should add b (1D neighbor).
        var edges = new[] { ("a", "b"), ("b", "c") };
        var set = MoveScopeResolver.ResolveMoveSet(
            "a", new[] { "a" }, MoveScope.SelectedPlusRelated1D, edges);
        Assert.Equal(new HashSet<string> { "a", "b" }, set);
    }

    [Fact]
    public void ResolveMoveSet_PlusRelated1D_DoesNotAddTwoHopNeighbors()
    {
        var edges = new[] { ("a", "b"), ("b", "c") };
        var set = MoveScopeResolver.ResolveMoveSet(
            "a", new[] { "a" }, MoveScope.SelectedPlusRelated1D, edges);
        // c is two hops away — must NOT be included.
        Assert.DoesNotContain("c", set);
    }

    [Fact]
    public void ResolveMoveSet_PlusRelated1D_IncludesIncomingNeighbors()
    {
        // Undirected: d->a means a's neighbor set includes d.
        var edges = new[] { ("d", "a"), ("a", "b") };
        var set = MoveScopeResolver.ResolveMoveSet(
            "a", new[] { "a" }, MoveScope.SelectedPlusRelated1D, edges);
        Assert.Equal(new HashSet<string> { "a", "b", "d" }, set);
    }

    [Fact]
    public void ResolveMoveSet_PlusRelated1D_AppliesToWholeSelection()
    {
        // Selecting {a, c} with a-b and c-e edges → adds b and e.
        var edges = new[] { ("a", "b"), ("c", "e") };
        var set = MoveScopeResolver.ResolveMoveSet(
            "a", new[] { "a", "c" }, MoveScope.SelectedPlusRelated1D, edges);
        Assert.Equal(new HashSet<string> { "a", "c", "b", "e" }, set);
    }

    // ── DesignCanvasController.SetSelectionFromIds (marquee) ──

    private static DesignGraph CreateGraphWith(params DesignClass[] classes)
    {
        var graph = new DesignGraph { Title = "Test" };
        foreach (var cls in classes)
            graph.Classes.Add(cls);
        return graph;
    }

    private static DesignCanvasController CreateController()
    {
        var modeController = new DesignModeController();
        return new DesignCanvasController(modeController);
    }

    [Fact]
    public void SetSelectionFromIds_ReplacesSelectionByDefault()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 0, Y = 0, Width = 100, Height = 60 },
            new DesignClass { Id = "b", Name = "B", X = 200, Y = 0, Width = 100, Height = 60 },
            new DesignClass { Id = "c", Name = "C", X = 400, Y = 0, Width = 100, Height = 60 }
        );
        var controller = CreateController();
        controller.SelectById("a");

        controller.SetSelectionFromIds(new[] { "b", "c" }, additive: false);

        Assert.Equal(new[] { "b", "c" }, controller.Selection.SelectedClassIds.OrderBy(x => x));
    }

    [Fact]
    public void SetSelectionFromIds_AdditiveTogglesIntoSelection()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 0, Y = 0, Width = 100, Height = 60 },
            new DesignClass { Id = "b", Name = "B", X = 200, Y = 0, Width = 100, Height = 60 },
            new DesignClass { Id = "c", Name = "C", X = 400, Y = 0, Width = 100, Height = 60 }
        );
        var controller = CreateController();
        controller.SelectById("a");

        // Additive marquee over {a, b}: a toggles OFF (already selected), b toggles ON.
        controller.SetSelectionFromIds(new[] { "a", "b" }, additive: true);

        Assert.Equal(new[] { "b" }, controller.Selection.SelectedClassIds);
    }

    [Fact]
    public void SetSelectionFromIds_EmptyReplaces_ClearsSelection()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 0, Y = 0, Width = 100, Height = 60 }
        );
        var controller = CreateController();
        controller.SelectById("a");

        controller.SetSelectionFromIds(System.Array.Empty<string>(), additive: false);

        Assert.Empty(controller.Selection.SelectedClassIds);
    }

    // ── Multi-drag in Design Mode ──

    [Fact]
    public void MultiDrag_SelectedOnly_MovesEntireSelectionTogether()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.CurrentMoveScope = MoveScope.SelectedOnly;

        // Establish multi-selection via body clicks (header clicks only drag;
        // they do not select). Body is below the header (y > Y + HeaderHeight).
        controller.HandlePointerPressed(new SKPoint(150, 180), graph, new List<SKPoint>());
        controller.HandlePointerPressed(new SKPoint(450, 180), graph, new List<SKPoint>(), extendSelection: true);
        Assert.Equal(2, controller.Selection.SelectedClassIds.Count);

        // Start a header drag on "a" (inside the selection) and move by (50, 30).
        controller.HandlePointerPressed(new SKPoint(150, 110), graph, new List<SKPoint>());
        controller.HandlePointerMoved(new SKPoint(200, 140));

        var a = graph.Classes.First(c => c.Id == "a");
        var b = graph.Classes.First(c => c.Id == "b");
        Assert.Equal(150f, a.X); // 100 + 50
        Assert.Equal(130f, a.Y); // 100 + 30
        Assert.Equal(450f, b.X); // 400 + 50
        Assert.Equal(130f, b.Y); // 100 + 30
    }

    [Fact]
    public void MultiDrag_PlusRelated1D_AlsoMovesDirectNeighbors()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "c", Name = "C", X = 700, Y = 100, Width = 200, Height = 100 }
        );
        // Edges: a—b—c . Selecting {a} with +Related 1D should also move b.
        graph.Edges.Add(new DesignEdge { FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Association });
        graph.Edges.Add(new DesignEdge { FromClassId = "b", ToClassId = "c", Kind = EdgeKind.Association });

        var controller = CreateController();
        controller.CurrentMoveScope = MoveScope.SelectedPlusRelated1D;

        // Select just "a" via a body click.
        controller.HandlePointerPressed(new SKPoint(150, 180), graph, new List<SKPoint>());
        Assert.Single(controller.Selection.SelectedClassIds);

        // Header-drag "a" by (50, 30). With +Related 1D, b should move too; c should NOT.
        controller.HandlePointerPressed(new SKPoint(150, 110), graph, new List<SKPoint>());
        controller.HandlePointerMoved(new SKPoint(200, 140));

        var a = graph.Classes.First(c => c.Id == "a");
        var b = graph.Classes.First(c => c.Id == "b");
        var c = graph.Classes.First(c => c.Id == "c");
        Assert.Equal(150f, a.X);
        Assert.Equal(130f, a.Y);
        Assert.Equal(450f, b.X); // b is a 1D neighbor → moved
        Assert.Equal(130f, b.Y);
        Assert.Equal(700f, c.X); // c is two hops → NOT moved
        Assert.Equal(100f, c.Y);
    }

    [Fact]
    public void MultiDrag_Release_PushesMoveClassCommandPerClass()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.CurrentMoveScope = MoveScope.SelectedOnly;

        // Select a and b via body clicks.
        controller.HandlePointerPressed(new SKPoint(150, 180), graph, new List<SKPoint>());
        controller.HandlePointerPressed(new SKPoint(450, 180), graph, new List<SKPoint>(), extendSelection: true);
        // Header-drag "a" and release.
        controller.HandlePointerPressed(new SKPoint(150, 110), graph, new List<SKPoint>());
        controller.HandlePointerMoved(new SKPoint(200, 140));
        controller.HandlePointerReleased(graph, new SKPoint(200, 140));

        // Both classes moved; undo should restore both (one MoveClass per class).
        Assert.True(controller.Undo(graph));
        Assert.True(controller.Undo(graph));
        var a = graph.Classes.First(c => c.Id == "a");
        var b = graph.Classes.First(c => c.Id == "b");
        Assert.Equal(100f, a.X);
        Assert.Equal(100f, a.Y);
        Assert.Equal(400f, b.X);
        Assert.Equal(100f, b.Y);
    }

    [Fact]
    public void SingleDrag_WhenSelectionHasOneClass_DoesNotEnterMultiDrag()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.CurrentMoveScope = MoveScope.SelectedOnly;

        controller.HandlePointerPressed(new SKPoint(150, 110), graph, new List<SKPoint>());
        controller.HandlePointerMoved(new SKPoint(200, 140));

        // Single-class drag: IsMultiDragging should be false.
        Assert.False(controller.IsMultiDragging);
        var a = graph.Classes.First(c => c.Id == "a");
        Assert.Equal(150f, a.X);
    }

    [Fact]
    public void GetDraggedClassIds_ReturnsAllMultiDragIdsDuringDrag()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.CurrentMoveScope = MoveScope.SelectedOnly;

        // Select a and b via body clicks, then header-drag "a".
        controller.HandlePointerPressed(new SKPoint(150, 180), graph, new List<SKPoint>());
        controller.HandlePointerPressed(new SKPoint(450, 180), graph, new List<SKPoint>(), extendSelection: true);
        controller.HandlePointerPressed(new SKPoint(150, 110), graph, new List<SKPoint>());

        var ids = controller.GetDraggedClassIds().OrderBy(x => x).ToList();
        Assert.Equal(new[] { "a", "b" }, ids);
    }
}
