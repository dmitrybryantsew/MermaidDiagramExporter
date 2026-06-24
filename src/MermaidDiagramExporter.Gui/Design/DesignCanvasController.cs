using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Design;
using SkiaSharp;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Owns Design Mode canvas interaction. Handles pointer events (click,
/// drag, resize, delete), maintains selection, and publishes changes to the
/// <see cref="DesignGraph"/>. Wired into <c>GraphCanvas</c>'s existing pointer
/// handlers via mode-aware branches (per docs/design/04).
/// </summary>
public sealed class DesignCanvasController
{
    private readonly DesignModeController _modeController;
    private readonly HashSet<string> _selectedClassIds = new();
    private DesignSelection _selection = new DesignSelection(Array.Empty<string>());
    private ClassRectangle? _draggingRectangle;
    private ClassRectangle? _resizingRectangle;
    private SKPoint _dragStartWorld;
    private SKPoint _resizeStartWorld;
    private float _resizeStartWidth;
    private float _resizeStartHeight;

    /// <summary>
    /// Fired after any mutation to the design graph. Subscribers can rebuild
    /// the <see cref="ClassRectangle"/> list and trigger a redraw.
    /// </summary>
    public event EventHandler? GraphMutated;

    /// <summary>
    /// Fired when the selection set changes.
    /// </summary>
    public event EventHandler<DesignSelection>? SelectionChanged;

    public DesignCanvasController(DesignModeController modeController)
    {
        _modeController = modeController;
    }

    /// <summary>
    /// Current selection (immutable snapshot).
    /// </summary>
    public DesignSelection Selection => _selection;

    /// <summary>
    /// True if the controller is currently in an active drag operation.
    /// Used by <c>GraphCanvas</c> to suppress keyboard shortcuts during drag.
    /// </summary>
    public bool IsDragging => _draggingRectangle != null;

    /// <summary>
    /// True if the controller is currently in an active resize operation.
    /// </summary>
    public bool IsResizing => _resizingRectangle != null;

    /// <summary>
    /// Builds the list of <see cref="ClassRectangle"/> objects from the current
    /// design graph. Called by the canvas controller on every render frame
    /// in Design Mode.
    /// </summary>
    public IReadOnlyList<ClassRectangle> BuildRectangles(DesignGraph graph)
    {
        var list = new List<ClassRectangle>(graph.Classes.Count);
        foreach (var cls in graph.Classes)
        {
            list.Add(new ClassRectangle(cls.Id, graph)
            {
                X = cls.X,
                Y = cls.Y,
                Width = cls.Width,
                Height = cls.Height,
                IsSelected = _selectedClassIds.Contains(cls.Id)
            });
        }
        return list;
    }

    /// <summary>
    /// Handles a pointer press in Design Mode. Returns true if the event was
    /// handled (caller should set <c>e.Handled = true</c> and skip Analyze
    /// Mode fallthrough). Returns false if the event should fall through to
    /// Analyze Mode behavior (pan, etc.).
    /// </summary>
    public bool HandlePointerPressed(SKPoint worldPos, DesignGraph? graph, List<SKPoint> classRectanglesForHitTest)
    {
        if (graph == null) return false;

        var hit = DesignHitTestService.HitTest(worldPos, BuildRectangles(graph));

        switch (hit.Kind)
        {
            case ClassRectangleHitTest.ResizeHandle:
                StartResize(hit.Rectangle!, worldPos);
                return true;

            case ClassRectangleHitTest.LeftPort:
            case ClassRectangleHitTest.RightPort:
                // M4 — edge creation. For M2, just select the class.
                Select(hit.Rectangle!);
                return true;

            case ClassRectangleHitTest.Header:
                StartDrag(hit.Rectangle!, worldPos);
                return true;

            case ClassRectangleHitTest.Body:
            case ClassRectangleHitTest.Member:
                Select(hit.Rectangle!);
                return true;

            case ClassRectangleHitTest.None:
                // Click on empty canvas: add a new class at this position
                AddClassAt(graph, worldPos);
                return true;
        }
        return false;
    }

    /// <summary>
    /// Handles a pointer move during a drag or resize operation.
    /// </summary>
    public void HandlePointerMoved(SKPoint worldPos)
    {
        if (_draggingRectangle != null)
        {
            float dx = worldPos.X - _dragStartWorld.X;
            float dy = worldPos.Y - _dragStartWorld.Y;
            _draggingRectangle.X = _dragStartWorld.X + dx;
            _draggingRectangle.Y = _dragStartWorld.Y + dy;
            // Mirror to graph
            var cls = _draggingRectangle.Graph.Classes.FirstOrDefault(c => c.Id == _draggingRectangle.ClassId);
            if (cls != null)
            {
                cls.X = _draggingRectangle.X;
                cls.Y = _draggingRectangle.Y;
            }
        }
        else if (_resizingRectangle != null)
        {
            float dx = worldPos.X - _resizeStartWorld.X;
            float dy = worldPos.Y - _resizeStartWorld.Y;
            float newWidth = Math.Clamp(_resizeStartWidth + dx, DesignGeometry.MinClassWidth, DesignGeometry.MaxClassWidth);
            float newHeight = Math.Clamp(_resizeStartHeight + dy, DesignGeometry.MinClassHeight, DesignGeometry.MaxClassHeight);
            _resizingRectangle.Width = newWidth;
            _resizingRectangle.Height = newHeight;
            var cls = _resizingRectangle.Graph.Classes.FirstOrDefault(c => c.Id == _resizingRectangle.ClassId);
            if (cls != null)
            {
                cls.Width = newWidth;
                cls.Height = newHeight;
            }
        }
    }

    /// <summary>
    /// Handles a pointer release — commits drag/resize.
    /// </summary>
    public void HandlePointerReleased()
    {
        if (_draggingRectangle != null)
        {
            _draggingRectangle.IsDragging = false;
            _draggingRectangle = null;
            GraphMutated?.Invoke(this, EventArgs.Empty);
        }
        if (_resizingRectangle != null)
        {
            _resizingRectangle.IsResizing = false;
            _resizingRectangle = null;
            GraphMutated?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Handles the Delete key — removes selected classes (and their edges).
    /// </summary>
    public bool HandleDeleteKey(DesignGraph graph)
    {
        if (_selectedClassIds.Count == 0) return false;
        var toRemove = _selectedClassIds.ToList();

        // Remove edges that reference any removed class
        graph.Edges.RemoveAll(e => toRemove.Contains(e.FromClassId) || toRemove.Contains(e.ToClassId));
        // Remove the classes themselves
        graph.Classes.RemoveAll(c => toRemove.Contains(c.Id));

        _selectedClassIds.Clear();
        UpdateSelection();
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void StartDrag(ClassRectangle rect, SKPoint worldPos)
    {
        _draggingRectangle = rect;
        _dragStartWorld = worldPos;
        rect.IsDragging = true;
    }

    private void StartResize(ClassRectangle rect, SKPoint worldPos)
    {
        _resizingRectangle = rect;
        _resizeStartWorld = worldPos;
        _resizeStartWidth = rect.Width;
        _resizeStartHeight = rect.Height;
        rect.IsResizing = true;
    }

    private void Select(ClassRectangle rect)
    {
        _selectedClassIds.Clear();
        _selectedClassIds.Add(rect.ClassId);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _selection = new DesignSelection(_selectedClassIds.ToList());
        SelectionChanged?.Invoke(this, _selection);
    }

    private void AddClassAt(DesignGraph graph, SKPoint worldPos)
    {
        var newClass = new DesignClass
        {
            Name = "NewClass",
            X = worldPos.X - 100f, // center on click
            Y = worldPos.Y - 20f,
            Width = 200f,
            Height = 60f
        };
        graph.Classes.Add(newClass);
        _selectedClassIds.Clear();
        _selectedClassIds.Add(newClass.Id);
        UpdateSelection();
        GraphMutated?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// Immutable snapshot of the current selection.
/// </summary>
public sealed record DesignSelection(IReadOnlyList<string> SelectedClassIds);
