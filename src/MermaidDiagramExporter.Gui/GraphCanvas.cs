using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MermaidDiagramExporter.Gui.Design;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Persistence;
using MermaidDiagramExporter.Gui.Layout;
using LayoutRect = MermaidDiagramExporter.Gui.Layout.Rect;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// High-performance SkiaSharp graph canvas with zoom, pan, and hit testing.
/// Uses ICustomDrawOperation for direct GPU-accelerated rendering.
/// </summary>
public class GraphCanvas : Control
{
    private List<GraphNode> _nodes = new();
    private List<GraphEdge> _edges = new();
    private float _zoom = 1.0f;
    private float _panX = 40f;
    private float _panY = 40f;
    private bool _isPanning;
    private float _lastMouseX;
    private float _lastMouseY;
    private GraphNode? _hoveredNode;
    private GraphNode? _selectedNode;
    private bool _needsRender = true;
    private bool _fitToScreenOnNextRender;
    private WriteableBitmap? _writeableBitmap;

    // ── Design Mode integration (M2) ──
    private DesignCanvasController? _designController;
    private DesignGraph? _designGraph;
    private HashSet<string> _designSelectedNodeIds = new();
    private HashSet<string> _designHoveredNodeIds = new();
    private EdgeCreationPreview? _edgeCreationPreview;

    // ── Multi-select + marquee + move-scope (UIContract §5) ──
    // Analyze Mode multi-select set. _selectedNode (below) stays as the
    // "primary" selection for inspector backward-compat; this set captures
    // every selected node so marquee/move-scope can act on all of them.
    private HashSet<string> _selectedNodeIds = new();
    private bool _isMarqueeSelecting;
    private SKPoint _marqueeStartWorld;
    private SKPoint _marqueeCurrentWorld;
    private bool _marqueeAdditive;
    // Generalized multi-node drag (used for move-scope + selection drag in
    // Analyze Mode). Replaces the cluster-drag path when more than one node
    // moves together. Design Mode multi-drag is handled in the controller.
    private bool _isMultiDragging;
    private HashSet<string> _multiDragNodeIds = new();
    private Dictionary<string, Vector2> _multiDragStartPositions = new();

    // ── Analyze Mode undo/redo for movement ──
    // Design Mode has its own DesignUndoManager; Analyze Mode is read-only
    // except for manual position overrides. This small stack records each
    // completed drag as a transaction of per-node delta changes so the user
    // can undo/redo position edits with Ctrl+Z / Ctrl+Y.
    private readonly Stack<AnalyzeMoveTransaction> _analyzeUndoStack = new();
    private readonly Stack<AnalyzeMoveTransaction> _analyzeRedoStack = new();
    private Dictionary<string, Vector2>? _dragStartDeltaSnapshot;
    /// <summary>
    /// Active move scope. When the user drags a selected class, the set of
    /// nodes that move together is resolved via <see cref="MoveScopeResolver"/>.
    /// </summary>
    public MoveScope CurrentMoveScope { get; set; } = MoveScope.SelectedOnly;

    /// <summary>
    /// Wires the Design Mode controller. Called from MainWindow when the mode
    /// toggle switches to Design. Pass null to disable Design Mode.
    /// </summary>
    public void SetDesignController(DesignCanvasController? controller)
    {
        _designController = controller;
        Invalidate();
    }

    /// <summary>
    /// Sets the current design graph for Design Mode. Called when entering
    /// Design Mode or when the design changes. Pass null to clear.
    /// </summary>
    public void SetDesignGraph(DesignGraph? graph)
    {
        _designGraph = graph;
        _staticContentDirty = true;
        Invalidate();
    }

    /// <summary>
    /// Sets the selected node IDs for Design Mode selection rendering.
    /// Called by MainWindow when the DesignMode selection changes.
    /// </summary>
    public void SetDesignSelection(HashSet<string> selectedIds)
    {
        _designSelectedNodeIds = selectedIds ?? new HashSet<string>();
        Invalidate();
    }

    public void SetDesignEdgePreview(EdgeCreationPreview? preview)
    {
        _edgeCreationPreview = preview;
    }

    // Dragging state
    private GraphNode? _draggedNode;
    private bool _isDraggingNode;
    private bool _isDraggingCluster;
    private string? _draggedClusterId;
    private float _dragStartMouseX;
    private float _dragStartMouseY;
    private float _dragStartNodeX;
    private float _dragStartNodeY;
    private Dictionary<string, Vector2> _clusterDragStartPositions = new();

    // Search highlighting
    private string _searchText = string.Empty;

    // ── SKPicture caching for static content (namespace groups + non-dragged edges) ──
    private SKPicture? _staticContentPicture;
    private bool _staticContentDirty = true;
    private float _staticContentMinX, _staticContentMinY, _staticContentMaxX, _staticContentMaxY;

    // ── Partial redraw during drag (Step 12) ──
    /// <summary>
    /// Set to the ID of the node currently being dragged. When non-null, the static content
    /// is recorded without this node and its connected edges, and only this node + edges
    /// are redrawn each frame on top of the cached picture.
    /// </summary>
    private string? _draggedNodeIdDuringRender;

    /// <summary>
    /// Tracks whether routed edge Points have been cleared for the current
    /// drag. False on drag start; set true on the first actual movement. This
    /// prevents a click-select (no movement) from permanently destroying the
    /// routed edge paths — the renderer keeps using the original polylines
    /// until the user actually drags the node.
    /// </summary>
    private bool _edgePointsClearedForDrag;

    // ── Extracted rendering and hit-test services (Step 17) ──
    private readonly CanvasRenderer _renderer = new();

    // Edge type visibility
    private bool _showInheritanceEdges = true;
    private bool _showImplementsEdges = true;
    private bool _showAssociationEdges = true;

    // Aggregate inter-namespace edges into highways (one thick line per pair)
    private bool _aggregateHighways;

    // Per-kind edge visual style (color + arrowhead). Null = use renderer defaults.
    private Settings.EdgeStyleSettings? _edgeStyles;

    // Search highlight color (bright yellow)
    private static readonly SKColor ColorNodeStrokeSearchMatch = new(0xFF, 0xE0, 0x40);

    // Colors (dark theme matching Unity)
    private static SKColor ColorBg => Theming.RenderPalette.Current.CanvasBg;
    private static readonly SKColor ColorNodeFill = new(0x2D, 0x33, 0x3F);
    private static readonly SKColor ColorNodeStroke = new(0x4A, 0x6A, 0x8A);
    private static readonly SKColor ColorNodeStrokeSelected = new(0xFF, 0x8C, 0x00);
    private static readonly SKColor ColorNodeStrokeHover = new(0x60, 0xA0, 0xE0);
    private static readonly SKColor ColorEdgeInheritance = new(0x50, 0x90, 0xD0);
    private static readonly SKColor ColorEdgeImplements = new(0x40, 0xB0, 0x70);
    private static readonly SKColor ColorEdgeAssociation = new(0x60, 0x60, 0x60);
    private static readonly SKColor ColorText = new(0xE0, 0xE6, 0xEC);
    private static readonly SKColor ColorTextMuted = new(0x88, 0x90, 0x98);
    private static readonly SKColor ColorNamespaceBg = new(0x25, 0x2A, 0x32);
    private static readonly SKColor ColorNamespaceBorder = new(0x3A, 0x42, 0x50);
    private static readonly SKColor ColorNamespaceText = new(0x70, 0x80, 0x90);
    private static readonly SKColor ColorBadgeInterface = new(0x40, 0x80, 0xC0);
    private static readonly SKColor ColorBadgeEnum = new(0xC0, 0x80, 0x30);
    private static readonly SKColor ColorBadgeStruct = new(0x80, 0x50, 0xC0);
    private static readonly SKColor ColorBadgeStatic = new(0xC0, 0x50, 0x50);
    private static readonly SKColor ColorBadgeAbstract = new(0x30, 0x80, 0x80);

    private const float NodePaddingX = 12;
    private const float NodeHeaderHeight = 28;
    private const float NodeMemberHeight = 16;
    private const float NamespacePadding = 24;
    private const float NamespaceTitleHeight = 24;

    // ── Named constants for magic numbers ──
    /// <summary>Maximum number of member names shown inside a node box before truncating.</summary>
    private const int MaxMembersShownPerNode = 6;
    /// <summary>Length, in canvas units, of edge arrowheads.</summary>
    private const float ArrowheadLength = 10f;
    /// <summary>Half-width, in canvas units, of edge arrowheads.</summary>
    private const float ArrowheadHalfWidth = 5f;

    // ── Cached SKPaint objects (reused across frames to reduce GC pressure) ──
    private static readonly SKPaint NamespaceBgPaint = new()
    {
        Color = ColorNamespaceBg, Style = SKPaintStyle.Fill, IsAntialias = true
    };
    private static readonly SKPaint NamespaceBorderPaint = new()
    {
        Color = ColorNamespaceBorder, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true
    };
    private static readonly SKPaint NamespaceTextPaint = new()
    {
        Color = ColorNamespaceText, IsAntialias = true, TextSize = 13
    };
    private static readonly SKPaint EdgeLabelPaint = new()
    {
        Color = ColorTextMuted, IsAntialias = true, TextSize = 9
    };
    private static readonly SKPaint NodeFillPaint = new()
    {
        Color = ColorNodeFill, Style = SKPaintStyle.Fill, IsAntialias = true
    };
    private static readonly SKPaint BadgeTextPaint = new()
    {
        Color = SKColors.White, IsAntialias = true, TextSize = 9
    };
    private static readonly SKPaint StereotypeBadgeTextPaint = new()
    {
        Color = SKColors.White, IsAntialias = true, TextSize = 8
    };
    private static readonly SKPaint NodeNamePaint = new()
    {
        Color = ColorText, IsAntialias = true, TextSize = 12
    };
    private static readonly SKPaint NodeMemberPaint = new()
    {
        Color = ColorTextMuted, IsAntialias = true, TextSize = 10
    };
    // Mutable paints for state-dependent rendering (reused, properties updated per-frame)
    private static readonly SKPaint EdgeStrokePaint = new()
    {
        Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round
    };
    private static readonly SKPaint ArrowheadPaint = new()
    {
        Style = SKPaintStyle.Fill, IsAntialias = true
    };
    private static readonly SKPaint NodeStrokePaint = new()
    {
        Style = SKPaintStyle.Stroke, IsAntialias = true
    };
    private static readonly SKPaint NodeHeaderPaint = new()
    {
        Style = SKPaintStyle.Fill, IsAntialias = true
    };
    private static readonly SKPaint BadgeFillPaint = new()
    {
        Style = SKPaintStyle.Fill, IsAntialias = true
    };
    private static readonly SKPaint StereotypeBadgeFillPaint = new()
    {
        Style = SKPaintStyle.Fill, IsAntialias = true
    };

    public event Action<GraphNode?>? SelectionChanged;

    /// <summary>
    /// Raised when the Analyze Mode multi-selection set changes (marquee
    /// select, shift-click toggle, or programmatic clear). Carries the full
    /// list of selected node IDs. MainWindow uses this to update the
    /// inspector's multi-select indicator.
    /// </summary>
    public event Action<IReadOnlyList<string>>? AnalyzeMultiSelectionChanged;

    /// <summary>
    /// Raised when zoom or pan changes. Used by the minimap to update its viewport rectangle.
    /// </summary>
    public event Action<float, float, float, float, float>? ViewportChanged;

    /// <summary>
    /// Manual position overrides. Set by MainWindow, modified by drag operations.
    /// </summary>
    public ManualLayoutOverrides ManualOverrides { get; set; } = new();

    /// <summary>
    /// Raised when the user finishes dragging a node. MainWindow should persist the overrides.
    /// </summary>
    public event Action? ManualLayoutChanged;

    /// <summary>
    /// Raised when a class header is double-clicked in Design Mode. The
    /// subscriber (MainWindow) handles inline editing by showing a TextBox
    /// overlay. Per docs/design/04 — the one real Avalonia Control in Design Mode.
    /// </summary>
    public event Action<string>? DesignClassDoubleClicked;

    /// <summary>
    /// Raised when the user right-clicks in Design Mode. The subscriber
    /// (MainWindow) shows a context menu appropriate for what was clicked
    /// (class, edge, or empty canvas). Per docs/design/07 W6.
    /// </summary>
    public event Action<DesignContextTarget>? DesignContextMenuRequested;

    /// <summary>
    /// Raised when the user right-clicks on a class node in Analyze Mode.
    /// The subscriber (MainWindow) shows a context menu with "get code" and
    /// focus actions for the clicked class (and any active multi-selection).
    /// </summary>
    public event Action<GraphNode, SKPoint>? AnalyzeContextMenuRequested;

    public GraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public GraphNode? SelectedNode => _selectedNode;

    /// <summary>
    /// The current Analyze Mode multi-selection set (snapshot). Empty when
    /// only the single-selection path is in use or nothing is selected.
    /// </summary>
    public IReadOnlyCollection<string> GetAnalyzeSelectedNodeIds() => _selectedNodeIds;

    /// <summary>
    /// Replaces the Analyze Mode selection set. The first ID (if any) becomes
    /// the primary <see cref="SelectedNode"/> for inspector backward-compat.
    /// Pass an empty set to clear. Used by MainWindow for programmatic
    /// selection changes (e.g. clearing on mode switch).
    /// </summary>
    public void SetAnalyzeMultiSelection(IReadOnlyCollection<string> ids)
    {
        _selectedNodeIds = new HashSet<string>(ids);
        var firstId = _selectedNodeIds.FirstOrDefault();
        GraphNode? primary = null;
        if (firstId != null)
            primary = _nodes.FirstOrDefault(n => n.Id == firstId);
        if (primary != _selectedNode)
        {
            _selectedNode = primary;
            SelectionChanged?.Invoke(primary);
        }
        AnalyzeMultiSelectionChanged?.Invoke(_selectedNodeIds.ToList());
        Invalidate();
    }

    /// <summary>
    /// Sets the pan position directly (used by minimap).
    /// </summary>
    public void SetPan(float panX, float panY)
    {
        _panX = panX;
        _panY = panY;
        Invalidate();
    }

    public void SetGraph(List<GraphNode> nodes, List<GraphEdge> edges, bool preserveViewport = false, bool preserveSelectionAndHistory = false)
    {
        _nodes = nodes;
        _edges = edges;
        if (!preserveSelectionAndHistory)
        {
            _selectedNode = null;
            _selectedNodeIds.Clear();
            ClearAnalyzeUndoHistory();
        }
        _hoveredNode = null;
        _staticContentDirty = true;
        if (!preserveViewport)
        {
            _fitToScreenOnNextRender = true;
            FitToScreen();
        }
        Invalidate();
    }

    /// <summary>
    /// Returns a snapshot of the current graph nodes (positions, sizes, IDs).
    /// Used by "Edit in Design Mode" to preserve canvas layout positions.
    /// </summary>
    public List<GraphNode> GetCurrentNodes() => new(_nodes);

    public void WaitForRender()
    {
        // If the control has been sized, ensure the bitmap is rendered
        if (_writeableBitmap == null && Bounds.Width > 1 && Bounds.Height > 1)
        {
            RenderNow();
        }
    }

    public void SaveToPng(string path)
    {
        int w = (int)Math.Max(1, Bounds.Width);
        int h = (int)Math.Max(1, Bounds.Height);
        if (w <= 1 || h <= 1) { w = 1920; h = 1080; }
        FitToScreenIfNeeded(w, h);

        using var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(ColorBg);
        canvas.Save();
        canvas.Translate(_panX, _panY);
        canvas.Scale(_zoom);

        // Draw cached static content (namespace groups + edges) via SKPicture
        if (_staticContentDirty || _staticContentPicture == null)
        {
            RecordStaticContent();
        }
        if (_staticContentPicture != null)
        {
            // Translate so the picture's local coords align with world coords
            canvas.Translate(_staticContentMinX, _staticContentMinY);
            canvas.DrawPicture(_staticContentPicture);
            canvas.Translate(-_staticContentMinX, -_staticContentMinY);
        }

        // Draw nodes every frame (they have per-frame state: hover, selection, search match)
        if (_draggedNodeIdDuringRender != null)
        {
            // During drag: draw only the dragged node on top of the cached static picture
            DrawSingleNode(canvas, _draggedNodeIdDuringRender);
        }
        else
        {
            DrawNodes(canvas);
        }
        canvas.Restore();

        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }

    private void RecalculateLayout(int viewW, int viewH)
    {
        if (_nodes.Count == 0) return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var node in _nodes)
        {
            minX = Math.Min(minX, node.X);
            minY = Math.Min(minY, node.Y);
            maxX = Math.Max(maxX, node.X + node.Width);
            maxY = Math.Max(maxY, node.Y + node.Height);
        }

        float graphW = maxX - minX + 100;
        float graphH = maxY - minY + 100;

        if (graphW <= 0 || graphH <= 0) return;

        float zoomX = viewW / graphW;
        float zoomY = viewH / graphH;
        _zoom = Math.Min(zoomX, zoomY) * 0.80f;
        _panX = (viewW - graphW * _zoom) / 2 - minX * _zoom + 50 * _zoom;
        _panY = (viewH - graphH * _zoom) / 2 - minY * _zoom + 50 * _zoom;
    }

    public void FitToScreen()
    {
        if (_nodes.Count == 0) return;
        int w = (int)Math.Max(1, Bounds.Width);
        int h = (int)Math.Max(1, Bounds.Height);
        if (w <= 1 || h <= 1)
        {
            _fitToScreenOnNextRender = true;
        }
        else
        {
            RecalculateLayout(w, h);
            _fitToScreenOnNextRender = false;
            _staticContentDirty = true;
        }
        NotifyViewportChanged();
        Invalidate();
    }

    public void ZoomBy(float factor)
    {
        float newZoom = Math.Clamp(_zoom * factor, 0.05f, 5.0f);
        float cx = (float)Bounds.Width / 2;
        float cy = (float)Bounds.Height / 2;
        float worldX = (cx - _panX) / _zoom;
        float worldY = (cy - _panY) / _zoom;
        _panX = cx - worldX * newZoom;
        _panY = cy - worldY * newZoom;
        _zoom = newZoom;
        NotifyViewportChanged();
        Invalidate();
    }

    public void SetSearchText(string searchText)
    {
        _searchText = searchText ?? string.Empty;
        Invalidate();
    }

    public void SetEdgeVisibility(bool inheritance, bool implements, bool associations)
    {
        _showInheritanceEdges = inheritance;
        _showImplementsEdges = implements;
        _showAssociationEdges = associations;
        _staticContentDirty = true;
        Invalidate();
    }

    /// <summary>
    /// Toggles aggregate highway edges (one thick labeled edge per namespace
    /// pair instead of N individual inter-namespace edges). Triggers a re-render.
    /// </summary>
    public void SetAggregateHighways(bool aggregate)
    {
        _aggregateHighways = aggregate;
        _staticContentDirty = true;
        Invalidate();
    }

    /// <summary>
    /// Sets the per-kind edge visual style (color + arrowhead). Null restores
    /// built-in UML defaults. Triggers a re-render.
    /// </summary>
    public void SetEdgeStyles(Settings.EdgeStyleSettings? styles)
    {
        _edgeStyles = styles;
        _staticContentDirty = true;
        Invalidate();
    }

    /// <summary>
    /// Pans the canvas so the given node is centered in the viewport.
    /// </summary>
    public void CenterOnNode(GraphNode node)
    {
        float nodeCenterX = node.X + node.Width / 2;
        float nodeCenterY = node.Y + node.Height / 2;
        float viewW = (float)Bounds.Width;
        float viewH = (float)Bounds.Height;

        _panX = viewW / 2 - nodeCenterX * _zoom;
        _panY = viewH / 2 - nodeCenterY * _zoom;
        Invalidate();
    }

    private void Invalidate()
    {
        _needsRender = true;
        InvalidateVisual();
    }

    /// <summary>
    /// Public wrapper to force a re-render. Used by external code (e.g. keyboard
    /// shortcut handlers) that needs to trigger a redraw without changing the graph.
    /// </summary>
    public void ForceRedraw()
    {
        Invalidate();
    }

    /// <summary>
    /// Returns the current viewport transform (panX, panY, zoom) so external
    /// code can convert world coordinates to screen coordinates. Used by the
    /// inline edit TextBox overlay to position itself over a class header.
    /// </summary>
    public (float PanX, float PanY, float Zoom) GetViewportTransform()
        => (_panX, _panY, _zoom);

    private void NotifyViewportChanged()
    {
        ViewportChanged?.Invoke(_zoom, _panX, _panY, (float)Bounds.Width, (float)Bounds.Height);
    }

    /// <summary>
    /// Builds an immutable snapshot of current viewport state for the renderer.
    /// </summary>
    private ViewportState GetViewportState() => new()
    {
        Zoom = _zoom,
        PanX = _panX,
        PanY = _panY,
        ShowInheritanceEdges = _showInheritanceEdges,
        ShowImplementsEdges = _showImplementsEdges,
        ShowAssociationEdges = _showAssociationEdges,
        AggregateHighways = _aggregateHighways,
        SelectedNode = _selectedNode,
        HoveredNode = _hoveredNode,
        SearchText = _searchText,
        SelectedDesignNodeIds = _designGraph != null ? _designSelectedNodeIds : null,
        HoveredDesignNodeIds = _designGraph != null ? _designHoveredNodeIds : null,
        SelectedAnalyzeNodeIds = _designGraph == null ? _selectedNodeIds : null,
        IsDesignMode = _designGraph != null,
        EdgeStyles = _edgeStyles,
    };

    private void ComputeContentBounds()
    {
        if (_nodes.Count == 0)
        {
            _staticContentMinX = 0; _staticContentMinY = 0;
            _staticContentMaxX = 100; _staticContentMaxY = 100;
            return;
        }
        _staticContentMinX = float.MaxValue; _staticContentMinY = float.MaxValue;
        _staticContentMaxX = float.MinValue; _staticContentMaxY = float.MinValue;
        foreach (var node in _nodes)
        {
            _staticContentMinX = Math.Min(_staticContentMinX, node.X);
            _staticContentMinY = Math.Min(_staticContentMinY, node.Y);
            _staticContentMaxX = Math.Max(_staticContentMaxX, node.X + node.Width);
            _staticContentMaxY = Math.Max(_staticContentMaxY, node.Y + node.Height);
        }
        // Add padding for namespace groups
        _staticContentMinX -= NamespacePadding;
        _staticContentMinY -= NamespacePadding + NamespaceTitleHeight;
        _staticContentMaxX += NamespacePadding;
        _staticContentMaxY += NamespacePadding + NamespaceTitleHeight;
    }

    private void RecordStaticContent()
    {
        _staticContentPicture?.Dispose();
        ComputeContentBounds();
        float picW = Math.Max(1, _staticContentMaxX - _staticContentMinX);
        float picH = Math.Max(1, _staticContentMaxY - _staticContentMinY);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(SKRect.Create(0, 0, picW, picH));
        // Offset so content is relative to (0,0) in the picture
        canvas.Translate(-_staticContentMinX, -_staticContentMinY);
        DrawNamespaceGroups(canvas);
        DrawEdges(canvas, excludeNodeId: _draggedNodeIdDuringRender);
        // Draw non-dragged nodes into the static picture so they don't disappear during drag
        _renderer.DrawNodes(canvas, _nodes, GetViewportState(), excludeNodeId: _draggedNodeIdDuringRender);
        _staticContentPicture = recorder.EndRecording();
        _staticContentDirty = false;
    }

    private void RenderNow()
    {
        int w = (int)Math.Max(1, Bounds.Width);
        int h = (int)Math.Max(1, Bounds.Height);
        if (w <= 1 || h <= 1) return;

        _needsRender = false;
        FitToScreenIfNeeded(w, h);

        _writeableBitmap?.Dispose();
        _writeableBitmap = new WriteableBitmap(
            new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        using var framebuffer = _writeableBitmap.Lock();
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
        var canvas = surface.Canvas;

        canvas.Clear(ColorBg);
        canvas.Save();
        canvas.Translate(_panX, _panY);
        canvas.Scale(_zoom);

        // Draw cached static content (namespace groups + edges) via SKPicture
        if (_staticContentDirty || _staticContentPicture == null)
        {
            RecordStaticContent();
        }
        if (_staticContentPicture != null)
        {
            canvas.Translate(_staticContentMinX, _staticContentMinY);
            canvas.DrawPicture(_staticContentPicture);
            canvas.Translate(-_staticContentMinX, -_staticContentMinY);
        }

        if (_draggedNodeIdDuringRender != null)
        {
            DrawSingleNode(canvas, _draggedNodeIdDuringRender);
        }
        else
        {
            DrawNodes(canvas);
        }

        // ── Design Mode edge creation previews ──
        if (_designGraph != null && _designController != null)
        {
            var preview = _designController.GetEdgeCreationPreview();
            if (preview != null)
            {
                var srcRect = preview.SourceRectangle;
                float portX = preview.SourceIsRightPort ? srcRect.X + srcRect.Width : srcRect.X;
                float portY = srcRect.Y + srcRect.Height / 2f;
                CanvasRenderer.DrawEdgeCreationPreview(canvas, portX, portY, preview.CurrentCursor, preview.SourceIsRightPort);

                var mouseWorld = new SKPoint(preview.CurrentCursor.X, preview.CurrentCursor.Y);
                var designRects = _designController.BuildRectangles(_designGraph);
                var hit = DesignHitTestService.HitTest(mouseWorld, designRects);
                if (hit.Rectangle != null && hit.Rectangle != preview.SourceRectangle)
                {
                    CanvasRenderer.DrawEdgeTargetHighlight(canvas,
                        hit.Rectangle.X, hit.Rectangle.Y,
                        hit.Rectangle.Width, hit.Rectangle.Height);
                }
            }
        }

        // ── Marquee selection rectangle (both modes) ──
        if (_isMarqueeSelecting)
        {
            CanvasRenderer.DrawMarquee(canvas,
                _marqueeStartWorld.X, _marqueeStartWorld.Y,
                _marqueeCurrentWorld.X, _marqueeCurrentWorld.Y);
        }
        canvas.Restore();
        canvas.Flush();
        surface.Flush();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);

        int w = (int)Math.Max(1, Bounds.Width);
        int h = (int)Math.Max(1, Bounds.Height);
        if (w <= 1 || h <= 1) return;

        // Only re-render if the bitmap is stale (size changed or invalidate was called)
        if (_writeableBitmap == null ||
            _writeableBitmap.PixelSize.Width != w ||
            _writeableBitmap.PixelSize.Height != h ||
            _needsRender)
        {
            RenderNow();
        }

        if (_writeableBitmap != null)
        {
            var srcRect = new Avalonia.Rect(0, 0, _writeableBitmap.PixelSize.Width, _writeableBitmap.PixelSize.Height);
            var destRect = new Avalonia.Rect(0, 0, w, h);
            context.DrawImage(_writeableBitmap, srcRect, destRect);
        }
    }

    private void FitToScreenIfNeeded(int viewW, int viewH)
    {
        if (!_fitToScreenOnNextRender) return;
        RecalculateLayout(viewW, viewH);
        _fitToScreenOnNextRender = false;
    }

    private void DrawNamespaceGroups(SKCanvas canvas)
    {
        _renderer.DrawNamespaceGroups(canvas, _nodes);
    }

    private void DrawEdges(SKCanvas canvas, string? excludeNodeId = null)
    {
        _renderer.DrawEdges(canvas, _nodes, _edges, GetViewportState(), excludeNodeId);
    }

    private void DrawArrowhead(SKCanvas canvas, float x, float y, SKColor color)
    {
        // Delegated to CanvasRenderer — kept for compatibility with DrawSingleNode in GraphCanvas
        // This method is no longer called directly; DrawSingleNode now uses the renderer.
    }

    private void DrawNodes(SKCanvas canvas)
    {
        _renderer.DrawNodes(canvas, _nodes, GetViewportState());
    }

    /// <summary>
    /// Draws a single node (and its connected edges) during drag operations.
    /// This is used for partial redraw optimization — the static picture contains
    /// everything except the dragged node, so we only draw this one node on top.
    /// </summary>
    private void DrawSingleNode(SKCanvas canvas, string nodeId)
    {
        var node = _nodes.FirstOrDefault(n => n.Id == nodeId);
        if (node == null) return;
        _renderer.DrawSingleNode(canvas, _edges, node, GetViewportState());
    }

    private static string? GetBadgeText(GraphNode node) => CanvasRenderer.GetBadgeText(node);
    private static SKColor GetBadgeColor(GraphNode node) => CanvasRenderer.GetBadgeColor(node);

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        float zoomDelta = (float)(e.Delta.Y * 0.12f);
        float newZoom = Math.Clamp(_zoom * (1 + zoomDelta), 0.05f, 5.0f);

        var cursorPos = e.GetPosition(this);
        float worldX = (float)(cursorPos.X - _panX) / _zoom;
        float worldY = (float)(cursorPos.Y - _panY) / _zoom;
        _panX = (float)cursorPos.X - worldX * newZoom;
        _panY = (float)cursorPos.Y - worldY * newZoom;
        _zoom = newZoom;
        e.Handled = true;
        // Bug 03 Fix A: notify minimap that viewport changed on scroll-wheel zoom
        NotifyViewportChanged();
        Invalidate();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        // Right-click in Design Mode → fire context menu event (W6)
        if (_designController != null && _designGraph != null)
        {
            var props = e.GetCurrentPoint(this).Properties;
            if (props.IsRightButtonPressed)
            {
                var rcPos = e.GetPosition(this);
                var rcWorldPos = ScreenToWorld((float)rcPos.X, (float)rcPos.Y);
                var target = _designController.HitTestForContextMenu(rcWorldPos, new SKPoint((float)rcPos.X, (float)rcPos.Y), _designGraph);
                DesignContextMenuRequested?.Invoke(target);
                e.Handled = true;
                return;
            }
        }

        var pos = e.GetPosition(this);
        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);

        // Right-click on a class in Analyze Mode → fire context menu event.
        // Right-click on empty canvas falls through to the pan handler below so
        // users can still pan with right-drag. Per the RMB-context-menu feature.
        if (_designGraph == null && e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            var hit = HitTest(worldPos);
            if (hit != null)
            {
                AnalyzeContextMenuRequested?.Invoke(hit, new SKPoint((float)pos.X, (float)pos.Y));
                e.Handled = true;
                return;
            }
        }

        // Right-drag = pan in Analyze Mode (Design Mode reserves right-click for
        // the context menu). LMB-drag on empty canvas is now marquee selection,
        // so this gives users a discoverable pan alternative. Per UIContract §5.
        if (_designGraph == null && e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
        {
            _isPanning = true;
            _lastMouseX = (float)pos.X;
            _lastMouseY = (float)pos.Y;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // ── Design Mode routing (M2) ──
        // Guard: require both _designController AND _designGraph.
        // Without _designGraph, we fall through to Analyze Mode behavior.
        if (_designController != null && _designGraph != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            // ── Marquee pre-check (UIContract §5) ──
            // With the Select tool active and a press on empty canvas, start a
            // marquee instead of delegating to the controller (which would just
            // clear the selection). Shift = additive marquee.
            if (_designController.CurrentTool == DesignTool.Select)
            {
                var designHit = DesignHitTestService.HitTest(worldPos, _designController.BuildRectangles(_designGraph));
                if (designHit.Kind == ClassRectangleHitTest.None)
                {
                    bool additive = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control)) != 0;
                    StartMarquee(worldPos, additive);
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }

            // Shift/ctrl held → extend selection (multi-select). Per docs/design/09 GAP-2.
            bool extendSelection = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control)) != 0;
            if (_designController.HandlePointerPressed(worldPos, _designGraph, new List<SKPoint>(), extendSelection))
            {
                e.Pointer.Capture(this);
                e.Handled = true;
                // ── Partial redraw optimization for smooth Design Mode drag ──
                if (_designController.IsDragging || _designController.IsResizing)
                {
                    var classId = _designController.GetDraggedOrResizingClassId();
                    if (classId != null)
                    {
                        var node = _nodes.FirstOrDefault(n => n.Id == classId);
                        if (node != null)
                        {
                            _draggedNodeIdDuringRender = node.Id;
                            _staticContentDirty = true;
                        }
                    }
                }
                Invalidate();
                return;
            }
        }

        // Middle button or Ctrl+Left = pan (existing behavior)
        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
            (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
             (e.KeyModifiers & KeyModifiers.Control) != 0))
        {
            _isPanning = true;
            _lastMouseX = (float)pos.X;
            _lastMouseY = (float)pos.Y;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Left click on node = start drag (if enabled)
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            var hit = HitTest(worldPos);

            // Shift+click on a node = select entire cluster for dragging
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0 && hit != null)
            {
                string? clusterId = GetNodeClusterId(hit);
                if (clusterId != null)
                {
                    StartClusterDrag(clusterId, worldPos, (float)pos.X, (float)pos.Y);
                    e.Pointer.Capture(this);
                    e.Handled = true;
                    return;
                }
            }

            // Normal node drag — may become a multi-node drag when the grabbed
            // node is part of an existing selection, or when the move-scope
            // includes related neighbors. Per UIContract §5 + marquee feature.
            if (hit != null)
            {
                StartNodeDrag(hit, worldPos, (float)pos.X, (float)pos.Y);
                // Plain click on a node replaces the multi-selection with just
                // this node (Shift+click cluster path is handled above).
                if (_selectedNodeIds.Count > 1 && _selectedNodeIds.Contains(hit.Id))
                {
                    // Dragging inside an existing multi-selection: keep the set.
                    // Primary stays as-is for inspector continuity.
                }
                else
                {
                    _selectedNodeIds.Clear();
                    _selectedNodeIds.Add(hit.Id);
                    if (hit != _selectedNode)
                    {
                        _selectedNode = hit;
                        SelectionChanged?.Invoke(hit);
                    }
                    AnalyzeMultiSelectionChanged?.Invoke(_selectedNodeIds.ToList());
                }
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // Click/drag on empty space = marquee selection (UIContract §5).
            // A click without movement will clear the selection on release.
            bool additive = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control)) != 0;
            StartMarquee(worldPos, additive);
            e.Pointer.Capture(this);
            e.Handled = true;
        }
    }

    /// <summary>
    /// Handles double-click in Design Mode — fires the DesignClassDoubleClicked
    /// event when the user double-clicks on a class header. Per docs/design/04
    /// inline editing flow.
    /// </summary>
    protected override void OnDoubleTapped(TappedEventArgs e)
    {
        base.OnDoubleTapped(e);

        // Only handle in Design Mode
        if (_designController == null) return;

        var pos = e.GetPosition(this);
        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);

        // Hit-test to find which class was double-clicked
        if (_designGraph == null) return;
        var rectangles = _designController.BuildRectangles(_designGraph);
        var hit = DesignHitTestService.HitTest(worldPos, rectangles);

        if (hit.Kind == ClassRectangleHitTest.Header && hit.Rectangle != null)
        {
            DesignClassDoubleClicked?.Invoke(hit.Rectangle.ClassId);
            e.Handled = true;
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        var designWorldPos = ScreenToWorld((float)pos.X, (float)pos.Y);

        // ── Marquee selection (both modes) — update the rubber-band rect ──
        if (_isMarqueeSelecting)
        {
            _marqueeCurrentWorld = designWorldPos;
            e.Handled = true;
            Invalidate();
            return;
        }

        // Design Mode drag/resize/edge-creation routing (M2)
        // Guard: require both _designController AND _designGraph
        if (_designController != null && _designGraph != null && (_designController.IsDragging || _designController.IsResizing || _designController.IsCreatingEdge))
        {
            _designController.HandlePointerMoved(designWorldPos);
            // Sync GraphNode positions from DesignClass during drag for smooth live-redraw rendering.
            // The DesignCanvasController updates DesignClass.X/Y in HandlePointerMoved,
            // but the canvas renders from _nodes (GraphNode list). We must copy the
            // updated position to the matching GraphNode so the bitmap re-render shows
            // the class at its new position. For multi-drag, sync every moved class.
            var draggedIds = _designController.GetDraggedClassIds().ToList();
            if (draggedIds.Count > 0 && _designGraph != null)
            {
                foreach (var id in draggedIds)
                {
                    var cls = _designGraph.Classes.FirstOrDefault(c => c.Id == id);
                    var node = _nodes.FirstOrDefault(n => n.Id == id);
                    if (cls != null && node != null)
                    {
                        node.X = cls.X;
                        node.Y = cls.Y;
                        node.Width = cls.Width;
                        node.Height = cls.Height;
                    }
                }

                // Partial-redraw optimization only applies to a single dragged
                // class. For multi-drag, fall back to full re-render (correct
                // but slightly slower — fine for moderate selections).
                if (_designController.IsMultiDragging)
                {
                    if (_draggedNodeIdDuringRender != null)
                    {
                        _draggedNodeIdDuringRender = null;
                        _staticContentDirty = true;
                    }
                }
                else if (_draggedNodeIdDuringRender == null)
                {
                    _draggedNodeIdDuringRender = draggedIds[0];
                    _staticContentDirty = true;
                }
            }
            e.Handled = true;
            Invalidate();
            return;
        }

        // Panning (existing behavior)
        if (_isPanning)
        {
            float dx = (float)(pos.X - _lastMouseX);
            float dy = (float)(pos.Y - _lastMouseY);
            _panX += dx;
            _panY += dy;
            _lastMouseX = (float)pos.X;
            _lastMouseY = (float)pos.Y;
            e.Handled = true;
            NotifyViewportChanged();
            Invalidate();
            return;
        }

        // Node dragging
        if (_isDraggingNode && _draggedNode != null)
        {
            if (!_edgePointsClearedForDrag)
            {
                ClearEdgePointsForNodes(new HashSet<string> { _draggedNode.Id });
                _edgePointsClearedForDrag = true;
            }
            var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
            float deltaWorldX = worldPos.X - _dragStartMouseX;
            float deltaWorldY = worldPos.Y - _dragStartMouseY;

            float newX = _dragStartNodeX + deltaWorldX;
            float newY = _dragStartNodeY + deltaWorldY;

            // Apply delta as manual override (relative to engine position)
            Vector2 enginePos = GetEnginePosition(_draggedNode);
            Vector2 overrideDelta = new Vector2(newX - enginePos.X, newY - enginePos.Y);
            ManualOverrides.SetDelta(_draggedNode.Id, overrideDelta);

            _draggedNode.X = newX;
            _draggedNode.Y = newY;
            e.Handled = true;
            Invalidate();
            return;
        }

        // Cluster dragging
        if (_isDraggingCluster && _draggedClusterId != null)
        {
            if (!_edgePointsClearedForDrag)
            {
                ClearEdgePointsForNodes(_clusterDragStartPositions.Keys.ToHashSet());
                _edgePointsClearedForDrag = true;
            }
            var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
            float deltaWorldX = worldPos.X - _dragStartMouseX;
            float deltaWorldY = worldPos.Y - _dragStartMouseY;

            foreach (var node in _nodes)
            {
                if (_clusterDragStartPositions.TryGetValue(node.Id, out var startPos))
                {
                    float newX = startPos.X + deltaWorldX;
                    float newY = startPos.Y + deltaWorldY;

                    Vector2 enginePos = GetEnginePosition(node);
                    Vector2 overrideDelta = new Vector2(newX - enginePos.X, newY - enginePos.Y);
                    ManualOverrides.SetDelta(node.Id, overrideDelta);

                    node.X = newX;
                    node.Y = newY;
                }
            }
            e.Handled = true;
            Invalidate();
            return;
        }

        // Multi-node dragging (marquee/move-scope). Moves every node in
        // _multiDragNodeIds together by the same world delta. Each node's
        // manual override is updated so the move persists across re-layout.
        if (_isMultiDragging)
        {
            if (!_edgePointsClearedForDrag)
            {
                ClearEdgePointsForNodes(_multiDragNodeIds);
                _edgePointsClearedForDrag = true;
            }
            var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
            float deltaWorldX = worldPos.X - _dragStartMouseX;
            float deltaWorldY = worldPos.Y - _dragStartMouseY;

            foreach (var node in _nodes)
            {
                if (_multiDragStartPositions.TryGetValue(node.Id, out var startPos))
                {
                    float newX = startPos.X + deltaWorldX;
                    float newY = startPos.Y + deltaWorldY;

                    Vector2 enginePos = GetEnginePosition(node);
                    Vector2 overrideDelta = new Vector2(newX - enginePos.X, newY - enginePos.Y);
                    ManualOverrides.SetDelta(node.Id, overrideDelta);

                    node.X = newX;
                    node.Y = newY;
                }
            }
            e.Handled = true;
            Invalidate();
            return;
        }

        // Hover detection (existing behavior, but skip during drag)
        if (!_isDraggingNode && !_isDraggingCluster && !_isMultiDragging)
        {
            var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
            var hovered = HitTest(worldPos);
            if (hovered != _hoveredNode)
            {
                _hoveredNode = hovered;
                Invalidate();
            }
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        // ── Marquee release: compute selection (both modes) ──
        if (_isMarqueeSelecting)
        {
            _isMarqueeSelecting = false;
            FinishMarquee();
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);
            e.Handled = true;
            Invalidate();
            return;
        }

        // Design Mode drag/resize/edge commit (M2)
        // Guard: require both _designController AND _designGraph
        if (_designController != null && _designGraph != null && (_designController.IsDragging || _designController.IsResizing || _designController.IsCreatingEdge))
        {
            var pos = e.GetPosition(this);
            var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
            _designController.HandlePointerReleased(_designGraph, worldPos);
            _draggedNodeIdDuringRender = null;
            _staticContentDirty = true;
            e.Handled = true;
            Invalidate();
            return;
        }

        if (_isDraggingNode || _isDraggingCluster || _isMultiDragging)
        {
            bool moved = false;
            const float MovedEpsilon = 0.5f;
            if (_isMultiDragging)
            {
                foreach (var node in _nodes)
                {
                    if (_multiDragStartPositions.TryGetValue(node.Id, out var startPos))
                    {
                        if (Math.Abs(node.X - startPos.X) > MovedEpsilon
                         || Math.Abs(node.Y - startPos.Y) > MovedEpsilon)
                        {
                            moved = true;
                            break;
                        }
                    }
                }
            }
            else if (_draggedNode != null)
            {
                moved = Math.Abs(_draggedNode.X - _dragStartNodeX) > MovedEpsilon
                     || Math.Abs(_draggedNode.Y - _dragStartNodeY) > MovedEpsilon;
            }
            else if (_draggedClusterId != null)
            {
                // Cluster drag: check if any node moved
                foreach (var node in _nodes)
                {
                    if (_clusterDragStartPositions.TryGetValue(node.Id, out var startPos))
                    {
                        if (Math.Abs(node.X - startPos.X) > MovedEpsilon
                         || Math.Abs(node.Y - startPos.Y) > MovedEpsilon)
                        {
                            moved = true;
                            break;
                        }
                    }
                }
            }

            _isDraggingNode = false;
            _isDraggingCluster = false;
            _isMultiDragging = false;
            _multiDragNodeIds.Clear();
            _multiDragStartPositions.Clear();

            _draggedNode = null;
            _draggedClusterId = null;
            _draggedNodeIdDuringRender = null;
            _staticContentDirty = true; // Re-record static content with the node at its new position
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);

            if (moved)
            {
                _staticContentDirty = true;
                PushAnalyzeMoveTransactionIfChanged();
                ManualLayoutChanged?.Invoke();
            }

            e.Handled = true;
            return;
        }

        _isPanning = false;
        Cursor = Cursor.Default;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void StartNodeDrag(GraphNode node, SKPoint worldPos, float screenX, float screenY)
    {
        SnapshotDeltasForUndo();

        // ── Resolve the move set (UIContract §5 + marquee feature) ──
        // If the grabbed node is part of the current multi-selection, the whole
        // selection moves. If the move-scope is +Related 1D, add direct edge
        // neighbors. A resolved set with more than one node enters the
        // multi-drag path; otherwise the original single-node drag is used.
        var edges = _edges == null
            ? Enumerable.Empty<(string, string)>()
            : _edges
                .Where(ed => ed.FromNode != null && ed.ToNode != null)
                .Select(ed => (ed.FromNode!.Id, ed.ToNode!.Id));
        var moveSet = MoveScopeResolver.ResolveMoveSet(node.Id, _selectedNodeIds, CurrentMoveScope, edges);

        if (moveSet.Count > 1)
        {
            StartMultiDrag(moveSet, worldPos);
            return;
        }

        _draggedNode = node;
        _isDraggingNode = true;
        _isDraggingCluster = false;
        _draggedClusterId = null;
        _dragStartMouseX = worldPos.X;
        _dragStartMouseY = worldPos.Y;
        _dragStartNodeX = node.X;
        _dragStartNodeY = node.Y;
        _draggedNodeIdDuringRender = node.Id;
        // Don't clear edge Points yet — defer until the node actually moves.
        // This prevents a click-select (no movement) from permanently destroying
        // routed edge paths. See _edgePointsClearedForDrag.
        _edgePointsClearedForDrag = false;
        _staticContentDirty = true; // Re-record static content without the dragged node
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// Starts a multi-node drag for the given set of node IDs. Records each
    /// node's start position; the move handler applies the same world delta to
    /// every node. Used by marquee+selection drags and the +Related 1D scope.
    /// </summary>
    private void StartMultiDrag(HashSet<string> nodeIds, SKPoint worldPos)
    {
        SnapshotDeltasForUndo();
        _isDraggingNode = false;
        _isDraggingCluster = false;
        _isMultiDragging = true;
        _draggedNode = null;
        _draggedClusterId = null;
        _dragStartMouseX = worldPos.X;
        _dragStartMouseY = worldPos.Y;
        _multiDragNodeIds = new HashSet<string>(nodeIds);
        _multiDragStartPositions.Clear();
        foreach (var node in _nodes)
        {
            if (_multiDragNodeIds.Contains(node.Id))
                _multiDragStartPositions[node.Id] = new Vector2(node.X, node.Y);
        }
        // Multi-drag disables the single-node partial-redraw optimization (it
        // only tracks one node); a full re-render each frame is correct here.
        _draggedNodeIdDuringRender = null;
        // Don't clear edge Points yet — defer until actual movement.
        _edgePointsClearedForDrag = false;
        _staticContentDirty = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// Begins a marquee (rubber-band) selection at the given world position.
    /// When <paramref name="additive"/> is true (Shift/Ctrl held), the
    /// resulting selection is added to the current one instead of replacing it.
    /// </summary>
    private void StartMarquee(SKPoint worldPos, bool additive)
    {
        _isMarqueeSelecting = true;
        _marqueeStartWorld = worldPos;
        _marqueeCurrentWorld = worldPos;
        _marqueeAdditive = additive;
        Cursor = new Cursor(StandardCursorType.Cross);
    }

    /// <summary>
    /// Completes a marquee selection on pointer release. Computes which nodes
    /// intersect the dragged rectangle and updates the selection set. In Design
    /// Mode the controller's selection is updated; in Analyze Mode the canvas's
    /// own multi-select set is updated and events are raised. A marquee with
    /// no movement (treat-as-click on empty canvas) clears the selection.
    /// </summary>
    private void FinishMarquee()
    {
        float left = Math.Min(_marqueeStartWorld.X, _marqueeCurrentWorld.X);
        float right = Math.Max(_marqueeStartWorld.X, _marqueeCurrentWorld.X);
        float top = Math.Min(_marqueeStartWorld.Y, _marqueeCurrentWorld.Y);
        float bottom = Math.Max(_marqueeStartWorld.Y, _marqueeCurrentWorld.Y);

        // Treat as a click on empty canvas if the drag was negligible — clears selection.
        bool negligibleDrag = Math.Abs(right - left) < 2f && Math.Abs(bottom - top) < 2f;

        if (_designController != null && _designGraph != null)
        {
            // Design Mode: build rectangles and intersect with the marquee rect.
            var rects = _designController.BuildRectangles(_designGraph);
            var hitIds = new List<string>();
            if (!negligibleDrag)
            {
                foreach (var r in rects)
                {
                    // Intersection test (node's rect vs. marquee rect).
                    if (r.X < right && r.X + r.Width > left &&
                        r.Y < bottom && r.Y + r.Height > top)
                    {
                        hitIds.Add(r.ClassId);
                    }
                }
            }
            _designController.SetSelectionFromIds(hitIds, _marqueeAdditive);
        }
        else
        {
            // Analyze Mode: intersect marquee rect with all nodes.
            var hitIds = new List<string>();
            if (!negligibleDrag)
            {
                foreach (var n in _nodes)
                {
                    if (n.X < right && n.X + n.Width > left &&
                        n.Y < bottom && n.Y + n.Height > top)
                    {
                        hitIds.Add(n.Id);
                    }
                }
            }

            var newSet = _marqueeAdditive ? new HashSet<string>(_selectedNodeIds) : new HashSet<string>();
            foreach (var id in hitIds)
            {
                if (newSet.Contains(id)) newSet.Remove(id); // toggle within additive marquee
                else newSet.Add(id);
            }
            _selectedNodeIds = newSet;

            // Update primary selection (first in set) for inspector continuity.
            var firstId = _selectedNodeIds.FirstOrDefault();
            GraphNode? primary = firstId == null ? null : _nodes.FirstOrDefault(n => n.Id == firstId);
            if (primary != _selectedNode)
            {
                _selectedNode = primary;
                SelectionChanged?.Invoke(primary);
            }
            AnalyzeMultiSelectionChanged?.Invoke(_selectedNodeIds.ToList());
        }

        _staticContentDirty = true;
    }

    private void StartClusterDrag(string clusterId, SKPoint worldPos, float screenX, float screenY)
    {
        SnapshotDeltasForUndo();
        _isDraggingNode = false;
        _isDraggingCluster = true;
        _draggedClusterId = clusterId;
        _draggedNode = null;
        _dragStartMouseX = worldPos.X;
        _dragStartMouseY = worldPos.Y;
        _clusterDragStartPositions.Clear();

        var clusterNodeIds = new HashSet<string>();
        foreach (var node in _nodes)
        {
            if (GetNodeClusterId(node) == clusterId)
            {
                _clusterDragStartPositions[node.Id] = new Vector2(node.X, node.Y);
                clusterNodeIds.Add(node.Id);
            }
        }
        // Don't clear edge Points yet — defer until actual movement.
        _edgePointsClearedForDrag = false;
        _staticContentDirty = true;
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    /// <summary>
    /// Clears <see cref="GraphEdge.Points"/> on all edges connected to any of
    /// the given node IDs. This makes the renderer fall back to
    /// <see cref="EdgePortAssigner"/> (dynamic closest-perimeter ports) instead
    /// of using stale routed polylines from the original layout.
    /// </summary>
    private void ClearEdgePointsForNodes(HashSet<string> nodeIds)
    {
        if (_edges == null) return;
        foreach (var edge in _edges)
        {
            if (edge.FromNode == null || edge.ToNode == null) continue;
            if (nodeIds.Contains(edge.FromNode.Id) || nodeIds.Contains(edge.ToNode.Id))
            {
                edge.Points = System.Array.Empty<Vector2>();
            }
        }
    }

    // ── Analyze Mode undo/redo for movement ──

    /// <summary>
    /// Snapshots the current manual-override deltas at the start of a drag so
    /// that, on release, only the changed entries form an undo transaction.
    /// </summary>
    private void SnapshotDeltasForUndo()
    {
        _dragStartDeltaSnapshot = ManualOverrides == null
            ? new Dictionary<string, Vector2>()
            : new Dictionary<string, Vector2>(ManualOverrides.NodePositionDeltas);
    }

    /// <summary>
    /// Builds a transaction from the snapshot taken at drag start vs. the
    /// current deltas, and pushes it onto the undo stack if anything changed.
    /// Called from <see cref="OnPointerReleased"/> after a moved drag.
    /// </summary>
    private void PushAnalyzeMoveTransactionIfChanged()
    {
        if (_dragStartDeltaSnapshot == null) return;
        var before = _dragStartDeltaSnapshot;
        var after = ManualOverrides?.NodePositionDeltas ?? new Dictionary<string, Vector2>();
        var changes = new Dictionary<string, (Vector2 Old, Vector2 New)>();
        foreach (var kvp in before)
        {
            if (!after.TryGetValue(kvp.Key, out var newVal) || !DeltaEquals(newVal, kvp.Value))
                changes[kvp.Key] = (kvp.Value, after.TryGetValue(kvp.Key, out var nv) ? nv : default);
        }
        foreach (var kvp in after)
        {
            if (!before.TryGetValue(kvp.Key, out var oldVal))
                changes[kvp.Key] = (default, kvp.Value);
        }
        _dragStartDeltaSnapshot = null;
        if (changes.Count == 0) return;
        _analyzeUndoStack.Push(new AnalyzeMoveTransaction(changes));
        _analyzeRedoStack.Clear();
    }

    private static bool DeltaEquals(Vector2 a, Vector2 b)
        => Math.Abs(a.X - b.X) < 0.0001f && Math.Abs(a.Y - b.Y) < 0.0001f;

    /// <summary>True if there is an Analyze Mode move that can be undone.</summary>
    public bool CanUndoAnalyze => _analyzeUndoStack.Count > 0;

    /// <summary>True if there is an undone Analyze Mode move that can be redone.</summary>
    public bool CanRedoAnalyze => _analyzeRedoStack.Count > 0;

    /// <summary>
    /// Undoes the most recent Analyze Mode move transaction. Restores each
    /// affected node's override delta and recomputes its canvas position.
    /// Returns false if there is nothing to undo.
    /// </summary>
    public bool UndoAnalyze()
    {
        if (_analyzeUndoStack.Count == 0) return false;
        var txn = _analyzeUndoStack.Pop();
        ApplyTransaction(txn, undo: true);
        _analyzeRedoStack.Push(txn);
        _staticContentDirty = true;
        Invalidate();
        ManualLayoutChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Redoes the most recently undone Analyze Mode move. Returns false if
    /// there is nothing to redo.
    /// </summary>
    public bool RedoAnalyze()
    {
        if (_analyzeRedoStack.Count == 0) return false;
        var txn = _analyzeRedoStack.Pop();
        ApplyTransaction(txn, undo: false);
        _analyzeUndoStack.Push(txn);
        _staticContentDirty = true;
        Invalidate();
        ManualLayoutChanged?.Invoke();
        return true;
    }

    private void ApplyTransaction(AnalyzeMoveTransaction txn, bool undo)
    {
        foreach (var kvp in txn.Deltas)
        {
            var delta = undo ? kvp.Value.Old : kvp.Value.New;
            var node = _nodes.FirstOrDefault(n => n.Id == kvp.Key);
            if (node == null) continue;
            // enginePos is stable between drag-end and undo (no re-layout ran),
            // so: newNodePos = (node.X - currentDelta) + restoredDelta.
            Vector2 currentDelta = ManualOverrides.GetDelta(node.Id);
            Vector2 enginePos = new Vector2(node.X - currentDelta.X, node.Y - currentDelta.Y);
            ManualOverrides.SetDelta(node.Id, delta);
            node.X = enginePos.X + delta.X;
            node.Y = enginePos.Y + delta.Y;
        }
    }

    /// <summary>
    /// Clears the Analyze undo/redo stacks. Called when switching modes or
    /// loading a new graph (the overrides belong to a different layout).
    /// </summary>
    public void ClearAnalyzeUndoHistory()
    {
        _analyzeUndoStack.Clear();
        _analyzeRedoStack.Clear();
        _dragStartDeltaSnapshot = null;
    }

    private string? GetNodeClusterId(GraphNode node)
    {
        return node.Namespace;
    }

    /// <summary>
    /// The engine-computed position before manual overrides were applied.
    /// </summary>
    private Vector2 GetEnginePosition(GraphNode node)
    {
        Vector2 delta = ManualOverrides.GetDelta(node.Id);
        return new Vector2(node.X - delta.X, node.Y - delta.Y);
    }

    private SKPoint ScreenToWorld(float screenX, float screenY)
    {
        return HitTestService.ScreenToWorld(screenX, screenY, _panX, _panY, _zoom);
    }

    private GraphNode? HitTest(SKPoint worldPos)
    {
        return HitTestService.HitTest(worldPos, _nodes);
    }
}

/// <summary>
/// One Analyze Mode move transaction on the undo stack. Records the per-node
/// override-delta change (old → new) for a single completed drag. Undo
/// restores the old deltas; redo re-applies the new ones.
/// </summary>
internal sealed class AnalyzeMoveTransaction
{
    public Dictionary<string, (Vector2 Old, Vector2 New)> Deltas { get; }

    public AnalyzeMoveTransaction(Dictionary<string, (Vector2 Old, Vector2 New)> deltas)
        => Deltas = deltas;
}

public class GraphNode
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string AssetPath { get; set; } = "";
    public string Kind { get; set; } = "Class";
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 120;
    public float Height { get; set; } = 60;
    public List<GraphMember> Members { get; set; } = new();

    /// <summary>
    /// Stereotype labels with their display colors.
    /// Populated by the LayoutEngine from TypeNodeData.Stereotypes + custom rules.
    /// </summary>
    public List<GraphStereotypeBadge> StereotypeBadges { get; set; } = new();
}

public class GraphStereotypeBadge
{
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "#4ECDC4";
}

public class GraphMember
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Kind { get; set; } = "Field";
}

public class GraphEdge
{
    public GraphNode? FromNode { get; set; }
    public GraphNode? ToNode { get; set; }
    public bool IsStrongRelation { get; set; }
    public TypeEdgeKind Kind { get; set; } = TypeEdgeKind.Association;
    public string Label { get; set; } = "";

    /// <summary>
    /// Routed polyline points (start = source perimeter, end = target perimeter).
    /// Empty for Design Mode edges that have no layout-engine routing — the
    /// renderer falls back to computing closest-perimeter points dynamically.
    /// </summary>
    public IReadOnlyList<Vector2> Points { get; set; } = System.Array.Empty<Vector2>();
}
