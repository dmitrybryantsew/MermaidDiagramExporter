using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Design;
using SkiaSharp;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Tools available in Design Mode. Maps to UIContract §4 tool table.
/// </summary>
public enum DesignTool
{
    Select,
    Class,
    Interface,
    Enum,
    Struct,
    AbstractClass,
    StaticClass,
    Namespace,
    EdgeInheritance,
    EdgeImplements,
    EdgeAssociation,
    EdgeDependency,
    EdgeAggregation,
    EdgeComposition,
    Pan
}

/// <summary>
/// Whether a tool is armed for single use or sticky mode.
/// </summary>
public enum ToolArmingMode
{
    SingleUse,
    Sticky
}

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
    private float _dragStartRectX;
    private float _dragStartRectY;
    private SKPoint _resizeStartWorld;
    private float _resizeStartWidth;
    private float _resizeStartHeight;
    // Capture the class ID and original position at drag/resize start for undo
    private string? _dragStartClassId;
    private string? _resizeStartClassId;
    private float _resizeStartRectX;
    private float _resizeStartRectY;
    private EdgeKind _defaultEdgeKind = EdgeKind.Association;

    // ── Tool state (UIContract §4) ──
    private DesignTool _currentTool = DesignTool.Select;
    private bool _isToolSticky;

    // ── Edge creation state (M4) ──
    private ClassRectangle? _edgeSourceRectangle;
    private bool _edgeSourceIsRightPort;
    private SKPoint _edgeCurrentCursor;
    private string? _edgeSourceClassId; // for keyboard-initiated edge creation

    /// <summary>
    /// The currently armed tool.
    /// </summary>
    public DesignTool CurrentTool => _currentTool;

    /// <summary>
    /// True if the current tool is in sticky mode (won't auto-revert to Select after use).
    /// </summary>
    public bool IsToolSticky => _isToolSticky;

    /// <summary>
    /// The edge kind used when creating edges via port-drag or the Connect
    /// button. Set by arming an edge tool or by selecting a type in the
    /// edge-type dropdown. Defaults to Association.
    /// </summary>
    public EdgeKind DefaultEdgeKind
    {
        get => _defaultEdgeKind;
        set => _defaultEdgeKind = value;
    }

    /// <summary>
    /// Fired when the current tool changes, so the status bar can update.
    /// </summary>
    public event EventHandler? ToolChanged;

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
    /// Returns the ClassId of the class currently being dragged or resized,
    /// or null if no drag/resize is active. Used by GraphCanvas to sync
    /// GraphNode positions during live-redraw drag operations.
    /// </summary>
    public string? GetDraggedOrResizingClassId()
        => _draggingRectangle?.ClassId ?? _resizingRectangle?.ClassId;

    // ── Tool management (UIContract §4) ──

    /// <summary>
    /// Arms a tool. If <paramref name="sticky"/> is false (default), the tool
    /// reverts to Select after one use. If true, the tool stays armed until Esc
    /// or another tool is chosen.
    /// </summary>
    public void ArmTool(DesignTool tool, bool sticky = false)
    {
        _currentTool = tool;
        _isToolSticky = sticky;
        _edgeSourceClassId = null;
        // When arming an edge tool, update the default edge kind so port-drag
        // and Connect button use the selected type (UIContract §6 Method B).
        if (IsEdgeTool(tool))
            _defaultEdgeKind = ToolToEdgeKind(tool);
        // Cancelling any in-progress edge creation when switching tools
        CancelEdgeCreation();
        ToolChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reverts the tool to Select (called on Esc).
    /// </summary>
    public void ResetTool()
    {
        ArmTool(DesignTool.Select, false);
    }

    /// <summary>
    /// Returns true if <paramref name="tool"/> is a creation tool (Class, Interface, etc.).
    /// </summary>
    public static bool IsCreationTool(DesignTool tool) => tool switch
    {
        DesignTool.Class => true,
        DesignTool.Interface => true,
        DesignTool.Enum => true,
        DesignTool.Struct => true,
        DesignTool.AbstractClass => true,
        DesignTool.StaticClass => true,
        DesignTool.Namespace => true,
        _ => false
    };

    /// <summary>
    /// Returns true if <paramref name="tool"/> is an edge tool.
    /// </summary>
    public static bool IsEdgeTool(DesignTool tool) => tool switch
    {
        DesignTool.EdgeInheritance => true,
        DesignTool.EdgeImplements => true,
        DesignTool.EdgeAssociation => true,
        DesignTool.EdgeDependency => true,
        DesignTool.EdgeAggregation => true,
        DesignTool.EdgeComposition => true,
        _ => false
    };

    /// <summary>
    /// Maps DesignTool to ClassKind for creation tools.
    /// </summary>
    private static ClassKind ToolToClassKind(DesignTool tool) => tool switch
    {
        DesignTool.Class => ClassKind.Class,
        DesignTool.Interface => ClassKind.Interface,
        DesignTool.Enum => ClassKind.Enum,
        DesignTool.Struct => ClassKind.Struct,
        DesignTool.AbstractClass => ClassKind.AbstractClass,
        DesignTool.StaticClass => ClassKind.StaticClass,
        _ => ClassKind.Class
    };

    /// <summary>
    /// Maps DesignTool to EdgeKind.
    /// </summary>
    public static EdgeKind ToolToEdgeKind(DesignTool tool) => tool switch
    {
        DesignTool.EdgeInheritance => EdgeKind.Inheritance,
        DesignTool.EdgeImplements => EdgeKind.Implements,
        DesignTool.EdgeAssociation => EdgeKind.Association,
        DesignTool.EdgeDependency => EdgeKind.Dependency,
        DesignTool.EdgeAggregation => EdgeKind.Aggregation,
        DesignTool.EdgeComposition => EdgeKind.Composition,
        _ => EdgeKind.Association
    };

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
                IsSelected = _selectedClassIds.Contains(cls.Id),
                IsDragging = _draggingRectangle != null && _draggingRectangle.ClassId == cls.Id,
                IsResizing = _resizingRectangle != null && _resizingRectangle.ClassId == cls.Id,
            });
        }
        return list;
    }

    /// <summary>
    /// Hit-tests a world-space point and returns a <see cref="DesignContextTarget"/>
    /// describing what was hit (class, member, edge, or empty canvas). Used
    /// by the context menu handler to decide which actions to show.
    /// </summary>
    public DesignContextTarget HitTestForContextMenu(SKPoint worldPos, SKPoint screenPos, DesignGraph graph)
    {
        var hit = DesignHitTestService.HitTest(worldPos, BuildRectangles(graph));

        if (hit.Kind == ClassRectangleHitTest.Body && hit.Rectangle != null)
        {
            return new DesignContextTarget(
                DesignContextTargetKind.Class,
                worldPos,
                screenPos,
                ClassId: hit.Rectangle.ClassId);
        }

        if (hit.Kind == ClassRectangleHitTest.Member && hit.Rectangle != null)
        {
            return new DesignContextTarget(
                DesignContextTargetKind.Member,
                worldPos,
                screenPos,
                ClassId: hit.Rectangle.ClassId,
                MemberIndex: hit.MemberIndex);
        }

        // Edges aren't hit-tested yet (would need edge hit-testing service).
        // For now, treat anything else as empty canvas.
        return new DesignContextTarget(DesignContextTargetKind.EmptyCanvas, worldPos, screenPos);
    }

    /// <summary>
    /// Handles a pointer press in Design Mode. Returns true if the event was
    /// handled (caller should set <c>e.Handled = true</c> and skip Analyze
    /// Mode fallthrough). Returns false if the event should fall through to
    /// Analyze Mode behavior (pan, etc.).
    /// </summary>
    public bool HandlePointerPressed(SKPoint worldPos, DesignGraph? graph, List<SKPoint> classRectanglesForHitTest, bool extendSelection = false)
    {
        if (graph == null) return false;

        var hit = DesignHitTestService.HitTest(worldPos, BuildRectangles(graph));

        // ── Edge tool flow (UIContract §6 Method A) ──
        // When an edge tool is armed:
        //   1. If no source selected yet → clicking a class sets it as source.
        //   2. If source already set → clicking a different class completes the edge.
        //   3. Clicking empty canvas cancels.
        if (IsEdgeTool(_currentTool) && _edgeSourceClassId != null)
        {
            var srcCls = graph.Classes.FirstOrDefault(c => c.Id == _edgeSourceClassId);
            if (srcCls != null)
            {
                if (hit.Rectangle != null && hit.Rectangle.ClassId != _edgeSourceClassId)
                {
                    var edgeKind = ToolToEdgeKind(_currentTool);
                    AddEdge(graph, _edgeSourceClassId, hit.Rectangle.ClassId, edgeKind);
                    _edgeSourceClassId = null;
                    if (!_isToolSticky)
                        ArmTool(DesignTool.Select, false);
                    return true;
                }
                // Clicked same class or empty — cancel edge creation
                _edgeSourceClassId = null;
                return true;
            }
        }

        // Edge tool with no source yet: clicking a class sets it as source
        if (IsEdgeTool(_currentTool) && _edgeSourceClassId == null)
        {
            if (hit.Rectangle != null)
            {
                _edgeSourceClassId = hit.Rectangle.ClassId;
                // Visually select the source class so the user sees which one they picked
                Select(hit.Rectangle, false);
                return true;
            }
            // Clicked empty canvas — cancel edge tool
            if (!_isToolSticky)
                ArmTool(DesignTool.Select, false);
            return true;
        }

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
                // If an edge tool is armed, header click selects as source
                if (IsEdgeTool(_currentTool))
                {
                    _edgeSourceClassId = hit.Rectangle!.ClassId;
                    Select(hit.Rectangle, false);
                    return true;
                }
                StartDrag(hit.Rectangle!, worldPos);
                return true;

            case ClassRectangleHitTest.Body:
            case ClassRectangleHitTest.Member:
                // If an edge tool is armed, body click selects as source
                if (IsEdgeTool(_currentTool))
                {
                    _edgeSourceClassId = hit.Rectangle!.ClassId;
                    Select(hit.Rectangle, false);
                    return true;
                }
                Select(hit.Rectangle!, extendSelection);
                return true;

            case ClassRectangleHitTest.None:
                // ── UIContract §4: empty canvas behavior depends on current tool ──
                if (IsCreationTool(_currentTool))
                {
                    CreateAt(graph, worldPos, _currentTool);
                    if (!_isToolSticky)
                        ArmTool(DesignTool.Select, false);
                }
                else
                {
                    // Select tool: clear selection on empty canvas click
                    _selectedClassIds.Clear();
                    UpdateSelection();
                }
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
            // Preserve the grab offset: move by the delta from drag start,
            // not snap the top-left corner to the cursor. This matches Analyze
            // Mode behavior (which records _dragStartNodeX/Y and adds the delta).
            float dx = worldPos.X - _dragStartWorld.X;
            float dy = worldPos.Y - _dragStartWorld.Y;
            _draggingRectangle.X = _dragStartRectX + dx;
            _draggingRectangle.Y = _dragStartRectY + dy;
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
        if (_draggingRectangle != null && graph != null && _dragStartClassId != null)
        {
            // Create undo command for the completed drag
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _dragStartClassId);
            if (cls != null)
            {
                UndoManager.Execute(
                    new DesignCommands.MoveClass(_dragStartClassId,
                        _dragStartRectX, _dragStartRectY, cls.X, cls.Y),
                    graph);
            }
            _draggingRectangle.IsDragging = false;
            _draggingRectangle = null;
            _dragStartClassId = null;
            GraphMutated?.Invoke(this, EventArgs.Empty);
        }
        else if (_draggingRectangle != null)
        {
            _draggingRectangle.IsDragging = false;
            _draggingRectangle = null;
            _dragStartClassId = null;
            GraphMutated?.Invoke(this, EventArgs.Empty);
        }
        if (_resizingRectangle != null && graph != null && _resizeStartClassId != null)
        {
            // Create undo command for the completed resize
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _resizeStartClassId);
            if (cls != null)
            {
                UndoManager.Execute(
                    new DesignCommands.ResizeClass(_resizeStartClassId,
                        _resizeStartWidth, _resizeStartHeight, cls.Width, cls.Height),
                    graph);
            }
            _resizingRectangle.IsResizing = false;
            _resizingRectangle = null;
            _resizeStartClassId = null;
            GraphMutated?.Invoke(this, EventArgs.Empty);
        }
        else if (_resizingRectangle != null)
        {
            _resizingRectangle.IsResizing = false;
            _resizingRectangle = null;
            _resizeStartClassId = null;
            GraphMutated?.Invoke(this, EventArgs.Empty);
        }
        if (_edgeSourceRectangle != null)
        {
            if (graph != null)
            {
                var hit = DesignHitTestService.HitTest(worldPos, BuildRectangles(graph));
                ClassRectangle? target = null;
                if ((hit.Kind == ClassRectangleHitTest.RightPort || hit.Kind == ClassRectangleHitTest.LeftPort) && hit.Rectangle != null && hit.Rectangle != _edgeSourceRectangle)
                {
                    target = hit.Rectangle;
                }

                if (target != null)
                {
                    AddEdge(graph, _edgeSourceRectangle.ClassId, target.ClassId, _defaultEdgeKind);
                }
            }
            _edgeSourceRectangle = null;
        }
    }

    /// <summary>
    /// Resets all interaction state (drags, selection, edge creation, tool).
    /// Called when switching away from Design Mode to prevent state leaks.
    /// </summary>
    public void ResetAllState()
    {
        _draggingRectangle = null;
        _resizingRectangle = null;
        _edgeSourceRectangle = null;
        _edgeSourceClassId = null;
        _selectedClassIds.Clear();
        UpdateSelection();
        ResetTool();
    }

    /// <summary>
    /// Cancels an in-progress edge creation (called on Escape).
    /// </summary>
    public void CancelEdgeCreation()
    {
        _edgeSourceRectangle = null;
        _edgeSourceClassId = null;
    }

    /// <summary>
    /// True while an edge is being created (rubber-band line visible or keyboard source set).
    /// </summary>
    public bool IsCreatingEdge => _edgeSourceRectangle != null || _edgeSourceClassId != null;

    /// <summary>
    /// If a keyboard-initiated edge creation is in progress, returns the source class ID.
    /// </summary>
    public string? EdgeSourceClassId => _edgeSourceClassId;

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

        // Push each removal through the undo manager so it's undoable
        foreach (var classId in toRemove)
        {
            var cmd = new DesignCommands.RemoveClass(graph, classId);
            UndoManager.Execute(cmd, graph);
        }

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

        UndoManager.Execute(new DesignCommands.AddMember(cls.Id, member), graph);
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

        UndoManager.Execute(new DesignCommands.RemoveMember(graph, classId, memberIndex), graph);
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

        var member = cls.Members[memberIndex];
        if (member.Name == newName) return false;
        UndoManager.Execute(new DesignCommands.RenameMember(classId, member.Id, member.Name, newName), graph);
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

        var member = cls.Members[memberIndex];
        if (member.TypeName == newType) return false;
        UndoManager.Execute(new DesignCommands.ChangeMemberType(classId, member.Id, member.TypeName ?? "", newType), graph);
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
        var newVis = member.Visibility switch
        {
            Visibility.Public => Visibility.Private,
            Visibility.Private => Visibility.Protected,
            Visibility.Protected => Visibility.Internal,
            Visibility.Internal => Visibility.Public,
            _ => Visibility.Public
        };
        UndoManager.Execute(new DesignCommands.CycleMemberVisibility(classId, member.Id, member.Visibility, newVis), graph);
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
        UndoManager.Execute(new DesignCommands.MoveMember(classId, member.Id, memberIndex, newIndex), graph);
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

    /// <summary>
    /// Changes a class's kind (Class/Interface/Enum/Struct/Static/Abstract).
    /// Per docs/design/10 — fills the gap where ClassKind was impossible
    /// to change after creation.
    /// </summary>
    public bool ChangeClassKind(DesignGraph graph, string classId, ClassKind newKind)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (cls.Kind == newKind) return false;

        var cmd = new DesignCommands.ChangeClassKind(classId, cls.Kind, newKind);
        UndoManager.Execute(cmd, graph);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Changes a class's namespace. Per docs/design/10 — fills the gap
    /// where Namespace was impossible to change after creation.
    /// </summary>
    public bool ChangeNamespace(DesignGraph graph, string classId, string newNamespace)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return false;
        if (string.IsNullOrEmpty(newNamespace)) newNamespace = "";
        if ((cls.Namespace ?? "") == newNamespace) return false;

        var cmd = new DesignCommands.ChangeNamespace(classId, cls.Namespace ?? "", newNamespace);
        UndoManager.Execute(cmd, graph);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Public Select method — selects a class by ID. Used by the inspector
    /// panel's "jump to class" button on relations.
    /// </summary>
    public void SelectById(string classId)
    {
        _selectedClassIds.Clear();
        _selectedClassIds.Add(classId);
        UpdateSelection();
    }

    private void StartDrag(ClassRectangle rect, SKPoint worldPos)
    {
        _draggingRectangle = rect;
        _dragStartWorld = worldPos;
        _dragStartRectX = rect.X;
        _dragStartRectY = rect.Y;
        _dragStartClassId = rect.ClassId;
        rect.IsDragging = true;
    }

    private void StartResize(ClassRectangle rect, SKPoint worldPos)
    {
        _resizingRectangle = rect;
        _resizeStartWorld = worldPos;
        _resizeStartWidth = rect.Width;
        _resizeStartHeight = rect.Height;
        _resizeStartClassId = rect.ClassId;
        _resizeStartRectX = rect.X;
        _resizeStartRectY = rect.Y;
        rect.IsResizing = true;
    }

    private void StartEdgeCreation(ClassRectangle rect, bool isRightPort, SKPoint worldPos)
    {
        _edgeSourceRectangle = rect;
        _edgeSourceIsRightPort = isRightPort;
        _edgeCurrentCursor = worldPos;
    }

    /// <summary>
    /// Arms an edge tool with a pre-selected source class. Used when the user
    /// selects a class then presses an edge shortcut key (UIContract §6 Method C).
    /// The next click on a target class completes the edge.
    /// </summary>
    public void BeginEdgeCreationWithSource(DesignGraph graph, string sourceClassId, DesignTool edgeTool, bool sticky = false)
    {
        if (!IsEdgeTool(edgeTool)) return;
        _edgeSourceClassId = sourceClassId;
        ArmTool(edgeTool, sticky);
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

        var edge = new DesignEdge
        {
            FromClassId = fromClassId,
            ToClassId = toClassId,
            Kind = kind
        };
        UndoManager.Execute(new DesignCommands.AddEdge(edge), graph);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Removes an edge by ID.
    /// </summary>
    public bool RemoveEdge(DesignGraph graph, string edgeId)
    {
        var edge = graph.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge == null) return false;
        UndoManager.Execute(new DesignCommands.RemoveEdge(graph, edgeId), graph);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Changes an edge's type.
    /// </summary>
    public bool ChangeEdgeType(DesignGraph graph, string edgeId, EdgeKind newKind)
    {
        var edge = graph.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge == null) return false;
        if (edge.Kind == newKind) return false;
        var cmd = new DesignCommands.ChangeEdgeType(edgeId, edge.Kind, newKind);
        UndoManager.Execute(cmd, graph);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Returns the edge that connects two classes (if any), for hit-testing.
    /// </summary>
    public DesignEdge? FindEdgeBetween(DesignGraph graph, string fromClassId, string toClassId)
        => graph.Edges.FirstOrDefault(e => e.FromClassId == fromClassId && e.ToClassId == toClassId);

    /// <summary>
    /// Creates an edge between the two currently selected classes using
    /// <see cref="DefaultEdgeKind"/>. Requires exactly two classes selected.
    /// Returns true if the edge was created. Used by the Connect button
    /// (UIContract §6 — select two classes, press Connect).
    /// </summary>
    public bool ConnectSelected(DesignGraph graph)
    {
        if (_selectedClassIds.Count != 2) return false;
        var fromId = _selectedClassIds.ElementAt(0);
        var toId = _selectedClassIds.ElementAt(1);
        return AddEdge(graph, fromId, toId, _defaultEdgeKind);
    }

    /// <summary>
    /// Changes an edge's source or target endpoint. Used when the user drags
    /// an edge endpoint from one class to another. The <paramref name="isSource"/>
    /// flag determines which endpoint is changed.
    /// </summary>
    public bool ChangeEdgeEndpoint(DesignGraph graph, string edgeId, string newClassId, bool isSource)
    {
        var edge = graph.Edges.FirstOrDefault(e => e.Id == edgeId);
        if (edge == null) return false;

        // Validate the new endpoint exists
        if (!graph.Classes.Any(c => c.Id == newClassId)) return false;

        // Prevent self-loops
        var otherEnd = isSource ? edge.ToClassId : edge.FromClassId;
        if (newClassId == otherEnd) return false;

        var cmd = new DesignCommands.ChangeEdgeEndpoint(edgeId, isSource, isSource ? edge.FromClassId : edge.ToClassId, newClassId);
        UndoManager.Execute(cmd, graph);
        GraphMutated?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Selects a class. If <paramref name="extendSelection"/> is true (shift/ctrl held),
    /// the class is added to the existing selection (or removed if already selected).
    /// Otherwise the selection is cleared and replaced with just this class.
    /// Per docs/design/09 GAP-2.
    /// </summary>
    private void Select(ClassRectangle rect, bool extendSelection = false)
    {
        if (!extendSelection)
            _selectedClassIds.Clear();

        if (_selectedClassIds.Contains(rect.ClassId))
            _selectedClassIds.Remove(rect.ClassId); // toggle off
        else
            _selectedClassIds.Add(rect.ClassId);

        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _selection = new DesignSelection(_selectedClassIds.ToList());
        SelectionChanged?.Invoke(this, _selection);
    }

    private void CreateAt(DesignGraph graph, SKPoint worldPos, DesignTool tool)
    {
        var kind = ToolToClassKind(tool);
        string defaultName = tool switch
        {
            DesignTool.Class => "NewClass",
            DesignTool.Interface => "INewInterface",
            DesignTool.Enum => "NewEnum",
            DesignTool.Struct => "NewStruct",
            DesignTool.AbstractClass => "NewAbstractClass",
            DesignTool.StaticClass => "NewStaticClass",
            _ => "NewClass"
        };

        var newClass = new DesignClass
        {
            Name = defaultName,
            Kind = kind,
            X = worldPos.X - 100f,
            Y = worldPos.Y - 30f,
            Width = 200f,
            Height = 60f
        };

        // Push through undo manager so class creation is undoable
        UndoManager.Execute(new DesignCommands.AddClass(newClass), graph);
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
    SKPoint ScreenPosition,
    string? ClassId = null,
    int? MemberIndex = null,
    string? EdgeId = null);
