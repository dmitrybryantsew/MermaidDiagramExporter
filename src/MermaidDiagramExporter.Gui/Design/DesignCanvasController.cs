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
    public DesignUndoManager UndoManager { get; } = new();
    private readonly HashSet<string> _selectedClassIds = new();
    private DesignSelection _selection = new DesignSelection(Array.Empty<string>());
    private ClassRectangle? _draggingRectangle;
    private ClassRectangle? _resizingRectangle;
    private SKPoint _dragStartWorld;
    private SKPoint _resizeStartWorld;
    private float _resizeStartWidth;
    private float _resizeStartHeight;

    // ── Edge creation state (M4) ──
    private ClassRectangle? _edgeSourceRectangle;
    private bool _edgeSourceIsRightPort;
    private SKPoint _edgeCurrentCursor;

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
    /// Hit-tests a world-space point and returns a <see cref="DesignContextTarget"/>
    /// describing what was hit (class, member, edge, or empty canvas). Used
    /// by the context menu handler to decide which actions to show.
    /// </summary>
    public DesignContextTarget HitTestForContextMenu(SKPoint worldPos, DesignGraph graph)
    {
        var hit = DesignHitTestService.HitTest(worldPos, BuildRectangles(graph));

        if (hit.Kind == ClassRectangleHitTest.Body && hit.Rectangle != null)
        {
            return new DesignContextTarget(
                DesignContextTargetKind.Class,
                worldPos,
                ClassId: hit.Rectangle.ClassId);
        }

        if (hit.Kind == ClassRectangleHitTest.Member && hit.Rectangle != null)
        {
            return new DesignContextTarget(
                DesignContextTargetKind.Member,
                worldPos,
                ClassId: hit.Rectangle.ClassId,
                MemberIndex: hit.MemberIndex);
        }

        // Edges aren't hit-tested yet (would need edge hit-testing service).
        // For now, treat anything else as empty canvas.
        return new DesignContextTarget(DesignContextTargetKind.EmptyCanvas, worldPos);
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
                // M4: start edge creation. The rubber-band line follows the
                // cursor until released on a target port (or cancelled with Escape).
                StartEdgeCreation(hit.Rectangle!, hit.Kind == ClassRectangleHitTest.RightPort, worldPos);
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
        else if (_edgeSourceRectangle != null)
        {
            // Track cursor position for the rubber-band edge preview
            _edgeCurrentCursor = worldPos;
        }
    }

    /// <summary>
    /// Handles a pointer release — commits drag/resize, or completes edge
    /// creation if the release is on a target port.
    /// </summary>
    public void HandlePointerReleased(DesignGraph? graph, SKPoint worldPos)
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
        if (_edgeSourceRectangle != null && graph != null)
        {
            // Check if the release is on a target port
            var hit = DesignHitTestService.HitTest(worldPos, BuildRectangles(graph));
            ClassRectangle? target = null;
            bool targetIsRightPort = false;
            if (hit.Kind == ClassRectangleHitTest.RightPort && hit.Rectangle != null && hit.Rectangle != _edgeSourceRectangle)
            {
                target = hit.Rectangle;
                targetIsRightPort = true;
            }
            else if (hit.Kind == ClassRectangleHitTest.LeftPort && hit.Rectangle != null && hit.Rectangle != _edgeSourceRectangle)
            {
                target = hit.Rectangle;
                targetIsRightPort = false;
            }

            if (target != null)
            {
                // Create the edge. Default to Association; the UI will let the
                // user change the type via the edge type selector popup.
                AddEdge(graph, _edgeSourceRectangle.ClassId, target.ClassId, EdgeKind.Association);
            }

            // Clear edge-creation state either way
            _edgeSourceRectangle = null;
            _edgeCurrentCursor = worldPos;
        }
    }

    /// <summary>
    /// Cancels an in-progress edge creation (called on Escape).
    /// </summary>
    public void CancelEdgeCreation()
    {
        _edgeSourceRectangle = null;
    }

    /// <summary>
    /// True while an edge is being created (rubber-band line visible).
    /// </summary>
    public bool IsCreatingEdge => _edgeSourceRectangle != null;

    // ── Undo/Redo (M6) ──

    /// <summary>
    /// Undoes the most recent command. Returns true if a command was undone.
    /// </summary>
    public bool Undo(DesignGraph graph) => UndoManager.Undo(graph);

    /// <summary>
    /// Redoes the most recently undone command. Returns true if a command was redone.
    /// </summary>
    public bool Redo(DesignGraph graph) => UndoManager.Redo(graph);

    /// <summary>
    /// Executes a command (applies it and pushes onto the undo stack).
    /// Fires GraphMutated after applying.
    /// </summary>
    public void ExecuteCommand(DesignCommand command, DesignGraph graph)
    {
        UndoManager.Execute(command, graph);
        GraphMutated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Returns the current edge-creation preview state for rendering.
    /// Returns null if no edge is being created.
    /// </summary>
    public EdgeCreationPreview? GetEdgeCreationPreview()
    {
        if (_edgeSourceRectangle == null) return null;
        return new EdgeCreationPreview(
            _edgeSourceRectangle,
            _edgeSourceIsRightPort,
            _edgeCurrentCursor);
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

    // ── Member operations (M3) ──

    /// <summary>
    /// Adds a new member to the currently selected class. The new member is
    /// added at the end of the member list and is immediately selected for
    /// inline editing.
    /// </summary>
    public DesignMember? AddMemberToSelectedClass(DesignGraph graph, MemberKind kind)
    {
        if (_selectedClassIds.Count != 1) return null;
        var cls = graph.Classes.FirstOrDefault(c => c.Id == _selectedClassIds.First());
        if (cls == null) return null;

        var member = new DesignMember
        {
            Kind = kind,
            Name = kind switch
            {
                MemberKind.Field => "NewField",
                MemberKind.Property => "NewProperty",
                MemberKind.Method => "NewMethod",
                MemberKind.Constructor => cls.Name,
                MemberKind.Event => "NewEvent",
                _ => "NewMember"
            },
            TypeName = kind switch
            {
                MemberKind.Field => "object",
                MemberKind.Property => "object",
                MemberKind.Method => "void",
                MemberKind.Constructor => "",
                MemberKind.Event => "EventHandler",
                _ => "object"
            },
            Visibility = Visibility.Public
        };

        // Constructors get a void return type and no type
        if (kind == MemberKind.Constructor)
            member.TypeName = "";

        cls.Members.Add(member);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return member;
    }

    /// <summary>
    /// Removes a member from a class by index.
    /// </summary>
    public bool RemoveMember(DesignGraph graph, string classId, int memberIndex)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (memberIndex < 0 || memberIndex >= cls.Members.Count) return false;

        cls.Members.RemoveAt(memberIndex);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Renames a member.
    /// </summary>
    public bool RenameMember(DesignGraph graph, string classId, int memberIndex, string newName)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (memberIndex < 0 || memberIndex >= cls.Members.Count) return false;
        if (string.IsNullOrWhiteSpace(newName)) return false;

        cls.Members[memberIndex].Name = newName;
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Changes a member's type.
    /// </summary>
    public bool ChangeMemberType(DesignGraph graph, string classId, int memberIndex, string newType)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (memberIndex < 0 || memberIndex >= cls.Members.Count) return false;
        if (string.IsNullOrWhiteSpace(newType)) return false;

        cls.Members[memberIndex].TypeName = newType;
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Cycles a member's visibility: Public → Private → Protected → Internal → Public.
    /// </summary>
    public bool CycleMemberVisibility(DesignGraph graph, string classId, int memberIndex)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (memberIndex < 0 || memberIndex >= cls.Members.Count) return false;

        var member = cls.Members[memberIndex];
        member.Visibility = member.Visibility switch
        {
            Visibility.Public => Visibility.Private,
            Visibility.Private => Visibility.Protected,
            Visibility.Protected => Visibility.Internal,
            Visibility.Internal => Visibility.Public,
            _ => Visibility.Public
        };
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Moves a member up or down within its class. Returns true if the move succeeded.
    /// </summary>
    public bool MoveMember(DesignGraph graph, string classId, int memberIndex, int delta)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (memberIndex < 0 || memberIndex >= cls.Members.Count) return false;

        int newIndex = memberIndex + delta;
        if (newIndex < 0 || newIndex >= cls.Members.Count) return false;

        var member = cls.Members[memberIndex];
        cls.Members.RemoveAt(memberIndex);
        cls.Members.Insert(newIndex, member);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Renames a class.
    /// </summary>
    public bool RenameClass(DesignGraph graph, string classId, string newName)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (string.IsNullOrWhiteSpace(newName)) return false;

        cls.Name = newName;
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

    private void StartEdgeCreation(ClassRectangle rect, bool isRightPort, SKPoint worldPos)
    {
        _edgeSourceRectangle = rect;
        _edgeSourceIsRightPort = isRightPort;
        _edgeCurrentCursor = worldPos;
    }

    // ── Edge operations (M4) ──

    /// <summary>
    /// Adds a new edge between two classes. Validates that both endpoints exist.
    /// </summary>
    public bool AddEdge(DesignGraph graph, string fromClassId, string toClassId, EdgeKind kind)
    {
        if (fromClassId == toClassId) return false; // self-loop not allowed
        var classIds = new HashSet<string>(graph.Classes.Select(c => c.Id));
        if (!classIds.Contains(fromClassId)) return false;
        if (!classIds.Contains(toClassId)) return false;

        graph.Edges.Add(new DesignEdge
        {
            FromClassId = fromClassId,
            ToClassId = toClassId,
            Kind = kind
        });
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Removes an edge by ID.
    /// </summary>
    public bool RemoveEdge(DesignGraph graph, string edgeId)
    {
        int removed = graph.Edges.RemoveAll(e => e.Id == edgeId);
        if (removed > 0)
        {
            GraphMutated?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Changes an edge's type.
    /// </summary>
    public bool ChangeEdgeType(DesignGraph graph, string edgeId, EdgeKind newKind)
    {
        var edge = graph.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge == null) return false;
        edge.Kind = newKind;
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Returns the edge that connects two classes (if any), for hit-testing.
    /// </summary>
    public DesignEdge? FindEdgeBetween(DesignGraph graph, string fromClassId, string toClassId)
        => graph.Edges.FirstOrDefault(e => e.FromClassId == fromClassId && e.ToClassId == toClassId);

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

/// <summary>
/// State of an in-progress edge creation. The renderer uses this to draw the
/// rubber-band line from the source port to the current cursor position.
/// </summary>
public sealed record EdgeCreationPreview(
    ClassRectangle SourceRectangle,
    bool SourceIsRightPort,
    SKPoint CurrentCursor);

/// <summary>
/// What was hit when the user right-clicked in Design Mode. Used by the
/// context menu to decide which actions to show. Per docs/design/07 W6.
/// </summary>
public enum DesignContextTargetKind
{
    EmptyCanvas,
    Class,
    Member,
    Edge
}

/// <summary>
/// Describes what was right-clicked and where. Carries the relevant IDs so
/// the context menu handler can act on them.
/// </summary>
public sealed record DesignContextTarget(
    DesignContextTargetKind Kind,
    SKPoint WorldPosition,
    string? ClassId = null,
    int? MemberIndex = null,
    string? EdgeId = null);
