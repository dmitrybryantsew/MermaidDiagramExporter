using System.Linq;
using MermaidDiagramExporter.Gui.Design;
using SkiaSharp;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for M2 Design Mode interaction: ClassRectangle hit-testing,
/// DesignHitTestService sub-region detection, and DesignCanvasController
/// add/select/delete operations. Per docs/design/07-implementation-phases.md
/// M2 acceptance criteria.
/// </summary>
public class DesignCanvasControllerTests
{
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

    // ── ClassRectangle hit-testing ──

    [Fact]
    public void ClassRectangle_PointInsideBody_ReturnsBody()
    {
        var graph = new DesignGraph();
        var rect = new ClassRectangle("c1", graph) { X = 100, Y = 100, Width = 200, Height = 100 };
        var hit = rect.HitTest(new SKPoint(150, 200)); // inside body
        Assert.Equal(ClassRectangleHitTest.Body, hit);
    }

    [Fact]
    public void ClassRectangle_PointInsideHeader_ReturnsHeader()
    {
        var graph = new DesignGraph();
        var rect = new ClassRectangle("c1", graph) { X = 100, Y = 100, Width = 200, Height = 100 };
        var hit = rect.HitTest(new SKPoint(150, 110)); // inside header (top 24px)
        Assert.Equal(ClassRectangleHitTest.Header, hit);
    }

    [Fact]
    public void ClassRectangle_PointOutside_ReturnsNone()
    {
        var graph = new DesignGraph();
        var rect = new ClassRectangle("c1", graph) { X = 100, Y = 100, Width = 200, Height = 100 };
        var hit = rect.HitTest(new SKPoint(50, 50)); // outside
        Assert.Equal(ClassRectangleHitTest.None, hit);
    }

    [Fact]
    public void ClassRectangle_PointOnResizeHandle_ReturnsResizeHandle()
    {
        var graph = new DesignGraph();
        var rect = new ClassRectangle("c1", graph) { X = 100, Y = 100, Width = 200, Height = 100 };
        // Resize handle is bottom-right 12x12 corner
        var hit = rect.HitTest(new SKPoint(295, 195)); // in the corner
        Assert.Equal(ClassRectangleHitTest.ResizeHandle, hit);
    }

    [Fact]
    public void ClassRectangle_PointOnLeftPort_ReturnsLeftPort()
    {
        var graph = new DesignGraph();
        var rect = new ClassRectangle("c1", graph) { X = 100, Y = 100, Width = 200, Height = 100 };
        // Left port is at left-center (X=100, Y=150)
        var hit = rect.HitTest(new SKPoint(100, 150));
        Assert.Equal(ClassRectangleHitTest.LeftPort, hit);
    }

    [Fact]
    public void ClassRectangle_PointOnRightPort_ReturnsRightPort()
    {
        var graph = new DesignGraph();
        var rect = new ClassRectangle("c1", graph) { X = 100, Y = 100, Width = 200, Height = 100 };
        // Right port is at right-center (X=300, Y=150)
        var hit = rect.HitTest(new SKPoint(300, 150));
        Assert.Equal(ClassRectangleHitTest.RightPort, hit);
    }

    // ── DesignHitTestService ──

    [Fact]
    public void DesignHitTestService_TopmostRectangleWins()
    {
        var graph = new DesignGraph();
        var rects = new[]
        {
            new ClassRectangle("back", graph) { X = 0, Y = 0, Width = 100, Height = 100 },
            new ClassRectangle("front", graph) { X = 0, Y = 0, Width = 100, Height = 100 }
        };
        // Reverse iteration: front (last in list) is checked first
        var hit = DesignHitTestService.HitTest(new SKPoint(50, 50), rects);
        Assert.Equal("front", hit.Rectangle?.ClassId);
    }

    [Fact]
    public void DesignHitTestService_NoHit_ReturnsNone()
    {
        var graph = new DesignGraph();
        var rects = new[] { new ClassRectangle("c1", graph) { X = 0, Y = 0, Width = 100, Height = 100 } };
        var hit = DesignHitTestService.HitTest(new SKPoint(500, 500), rects);
        Assert.Equal(ClassRectangleHitTest.None, hit.Kind);
        Assert.Null(hit.Rectangle);
    }

    [Fact]
    public void DesignHitTestService_EmptyList_ReturnsNone()
    {
        var hit = DesignHitTestService.HitTest(new SKPoint(50, 50), new ClassRectangle[0]);
        Assert.Equal(ClassRectangleHitTest.None, hit.Kind);
    }

    // ── DesignCanvasController ──

    [Fact]
    public void Controller_DefaultSelection_IsEmpty()
    {
        var controller = CreateController();
        Assert.Empty(controller.Selection.SelectedClassIds);
    }

    [Fact]
    public void Controller_DefaultIsDragging_IsFalse()
    {
        var controller = CreateController();
        Assert.False(controller.IsDragging);
        Assert.False(controller.IsResizing);
    }

    [Fact]
    public void Controller_BuildRectangles_CreatesOnePerClass()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 10, Y = 20, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 300, Y = 20, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        var rects = controller.BuildRectangles(graph);
        Assert.Equal(2, rects.Count);
        Assert.Equal(10f, rects[0].X);
        Assert.Equal(200f, rects[0].Width);
        Assert.Equal("a", rects[0].ClassId);
    }

    [Fact]
    public void Controller_BuildRectangles_MarksSelectedClasses()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        // Select class "a" by clicking on it (center = 200, 150)
        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());

        var rects = controller.BuildRectangles(graph);
        var rectA = rects.First(r => r.ClassId == "a");
        var rectB = rects.First(r => r.ClassId == "b");
        Assert.True(rectA.IsSelected);
        Assert.False(rectB.IsSelected);
    }

    [Fact]
    public void Controller_HandleDeleteKey_NoSelection_ReturnsFalse()
    {
        var graph = CreateGraphWith(new DesignClass { Id = "a", Name = "A" });
        var controller = CreateController();
        var result = controller.HandleDeleteKey(graph);
        Assert.False(result);
        Assert.Single(graph.Classes); // nothing deleted
    }

    [Fact]
    public void Controller_HandleDeleteKey_WithSelection_RemovesClass()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        // Click inside class "a" (at center 200, 150) to select it
        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());

        // Verify "a" is selected
        Assert.Single(controller.Selection.SelectedClassIds);
        Assert.Equal("a", controller.Selection.SelectedClassIds[0]);

        // Now delete
        var deleted = controller.HandleDeleteKey(graph);
        Assert.True(deleted);
        Assert.Single(graph.Classes);
        Assert.Equal("b", graph.Classes[0].Id);
    }

    [Fact]
    public void Controller_HandleDeleteKey_RemovesReferencingEdges()
    {
        var graph = new DesignGraph();
        var classA = new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 };
        var classB = new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 };
        graph.Classes.Add(classA);
        graph.Classes.Add(classB);
        graph.Edges.Add(new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Association });

        var controller = CreateController();
        // Click inside class "a" to select it
        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());

        controller.HandleDeleteKey(graph);

        Assert.Single(graph.Classes); // b remains
        Assert.Empty(graph.Edges); // edge referencing a is removed
    }

    [Fact]
    public void Controller_AddClassAt_AddsClassToGraph()
    {
        var graph = new DesignGraph();
        var controller = CreateController();
        int initialCount = graph.Classes.Count;

        // Must arm Class tool first (UIContract §4: tool-first creation)
        controller.ArmTool(DesignTool.Class, sticky: false);
        controller.HandlePointerPressed(new SKPoint(300, 200), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.Equal(initialCount + 1, graph.Classes.Count);
        Assert.Equal("NewClass", graph.Classes[0].Name);
    }

    [Fact]
    public void Controller_AddClassAt_SelectsNewClass()
    {
        var graph = new DesignGraph();
        var controller = CreateController();
        controller.ArmTool(DesignTool.Class, sticky: false);
        controller.HandlePointerPressed(new SKPoint(300, 200), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.Single(controller.Selection.SelectedClassIds);
        Assert.Equal(graph.Classes[0].Id, controller.Selection.SelectedClassIds[0]);
    }

    [Fact]
    public void Controller_GraphMutated_FiresOnAdd()
    {
        var graph = new DesignGraph();
        var controller = CreateController();
        int fireCount = 0;
        controller.GraphMutated += (_, _) => fireCount++;

        controller.ArmTool(DesignTool.Class, sticky: false);
        controller.HandlePointerPressed(new SKPoint(300, 200), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.True(fireCount >= 1);
    }

    [Fact]
    public void Controller_EmptyClickWithSelectTool_DoesNotCreateClass()
    {
        // UIContract §4: Select tool on empty canvas should NOT create a class
        var graph = new DesignGraph();
        var controller = CreateController();
        int initialCount = graph.Classes.Count;

        controller.ArmTool(DesignTool.Select, sticky: false);
        controller.HandlePointerPressed(new SKPoint(300, 200), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.Equal(initialCount, graph.Classes.Count);
    }

    // ── Edge tool source selection (UIContract §6 Method A) ──

    [Fact]
    public void EdgeTool_FirstClickOnClass_SetsSource()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.ArmTool(DesignTool.EdgeAssociation, sticky: false);

        // Click on class "a" (center = 200, 150) — should set as source
        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.True(controller.IsCreatingEdge);
        Assert.Equal("a", controller.EdgeSourceClassId);
        Assert.Empty(graph.Edges); // no edge created yet
    }

    [Fact]
    public void EdgeTool_SecondClickOnDifferentClass_CreatesEdge()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.ArmTool(DesignTool.EdgeInheritance, sticky: false);

        // Click class "a" → sets source
        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());
        // Click class "b" (center = 500, 150) → creates edge
        controller.HandlePointerPressed(new SKPoint(500, 150), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.Single(graph.Edges);
        Assert.Equal("a", graph.Edges[0].FromClassId);
        Assert.Equal("b", graph.Edges[0].ToClassId);
        Assert.Equal(EdgeKind.Inheritance, graph.Edges[0].Kind);
        Assert.False(controller.IsCreatingEdge);
    }

    [Fact]
    public void EdgeTool_ClickEmptyCanvas_CancelsAndRevertsToSelect()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.ArmTool(DesignTool.EdgeAssociation, sticky: false);

        // Click empty canvas → should cancel and revert to Select
        controller.HandlePointerPressed(new SKPoint(1000, 1000), graph, new System.Collections.Generic.List<SKPoint>());

        Assert.Equal(DesignTool.Select, controller.CurrentTool);
        Assert.False(controller.IsCreatingEdge);
    }

    // ── ConnectSelected (Connect button) ──

    [Fact]
    public void ConnectSelected_TwoClassesSelected_CreatesEdge()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        // Select both classes (shift+click for multi-select)
        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());
        controller.HandlePointerPressed(new SKPoint(500, 150), graph, new System.Collections.Generic.List<SKPoint>(), extendSelection: true);

        Assert.Equal(2, controller.Selection.SelectedClassIds.Count);

        var connected = controller.ConnectSelected(graph);
        Assert.True(connected);
        Assert.Single(graph.Edges);
        Assert.Equal("a", graph.Edges[0].FromClassId);
        Assert.Equal("b", graph.Edges[0].ToClassId);
    }

    [Fact]
    public void ConnectSelected_OneClassSelected_ReturnsFalse()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());

        var connected = controller.ConnectSelected(graph);
        Assert.False(connected);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void ConnectSelected_ZeroClassesSelected_ReturnsFalse()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();

        var connected = controller.ConnectSelected(graph);
        Assert.False(connected);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void ConnectSelected_UsesDefaultEdgeKind()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.DefaultEdgeKind = EdgeKind.Composition;

        controller.HandlePointerPressed(new SKPoint(200, 150), graph, new System.Collections.Generic.List<SKPoint>());
        controller.HandlePointerPressed(new SKPoint(500, 150), graph, new System.Collections.Generic.List<SKPoint>(), extendSelection: true);
        controller.ConnectSelected(graph);

        Assert.Equal(EdgeKind.Composition, graph.Edges[0].Kind);
    }

    // ── Drag offset preservation ──

    [Fact]
    public void Drag_PreservesGrabOffset_NotSnapToCursor()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        // Click on header (top of class, y=110 is in header zone) to start drag
        controller.HandlePointerPressed(new SKPoint(150, 110), graph, new System.Collections.Generic.List<SKPoint>());

        // Move cursor by (50, 30) — the class should move by exactly (50, 30),
        // not snap its top-left to (150, 110)
        controller.HandlePointerMoved(new SKPoint(200, 140));

        var cls = graph.Classes[0];
        Assert.Equal(150f, cls.X); // 100 + 50
        Assert.Equal(130f, cls.Y); // 100 + 30
    }

    // ── ChangeEdgeEndpoint ──

    [Fact]
    public void ChangeEdgeEndpoint_ChangesTarget()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 });
        graph.Classes.Add(new DesignClass { Id = "c", Name = "C", X = 700, Y = 100, Width = 200, Height = 100 });
        var controller = CreateController();
        controller.AddEdge(graph, "a", "b", EdgeKind.Association);
        var edgeId = graph.Edges[0].Id;

        // Change target from b to c
        var changed = controller.ChangeEdgeEndpoint(graph, edgeId, "c", isSource: false);
        Assert.True(changed);
        Assert.Equal("a", graph.Edges[0].FromClassId);
        Assert.Equal("c", graph.Edges[0].ToClassId);
    }

    [Fact]
    public void ChangeEdgeEndpoint_PreventsSelfLoop()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 });
        var controller = CreateController();
        controller.AddEdge(graph, "a", "b", EdgeKind.Association);
        var edgeId = graph.Edges[0].Id;

        // Try to change source to b (would create self-loop b→b)
        var changed = controller.ChangeEdgeEndpoint(graph, edgeId, "b", isSource: true);
        Assert.False(changed);
        Assert.Equal("a", graph.Edges[0].FromClassId);
        Assert.Equal("b", graph.Edges[0].ToClassId);
    }

    [Fact]
    public void ChangeEdgeEndpoint_IsUndoable()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 });
        graph.Classes.Add(new DesignClass { Id = "c", Name = "C", X = 700, Y = 100, Width = 200, Height = 100 });
        var controller = CreateController();
        controller.AddEdge(graph, "a", "b", EdgeKind.Association);
        var edgeId = graph.Edges[0].Id;

        controller.ChangeEdgeEndpoint(graph, edgeId, "c", isSource: false);
        Assert.Equal("c", graph.Edges[0].ToClassId);

        controller.Undo(graph);
        Assert.Equal("b", graph.Edges[0].ToClassId);
    }

    // ── Port-drag uses DefaultEdgeKind ──

    [Fact]
    public void PortDrag_UsesDefaultEdgeKind_NotHardcodedAssociation()
    {
        var graph = CreateGraphWith(
            new DesignClass { Id = "a", Name = "A", X = 100, Y = 100, Width = 200, Height = 100 },
            new DesignClass { Id = "b", Name = "B", X = 400, Y = 100, Width = 200, Height = 100 }
        );
        var controller = CreateController();
        controller.DefaultEdgeKind = EdgeKind.Composition;

        // Start edge from right port of "a" (at 300, 150)
        controller.HandlePointerPressed(new SKPoint(300, 150), graph, new System.Collections.Generic.List<SKPoint>());
        // Release on left port of "b" (at 400, 150)
        controller.HandlePointerReleased(graph, new SKPoint(400, 150));

        Assert.Single(graph.Edges);
        Assert.Equal(EdgeKind.Composition, graph.Edges[0].Kind);
    }
}
