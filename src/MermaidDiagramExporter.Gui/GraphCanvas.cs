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

    // ── Extracted rendering and hit-test services (Step 17) ──
    private readonly CanvasRenderer _renderer = new();

    // Edge type visibility
    private bool _showInheritanceEdges = true;
    private bool _showImplementsEdges = true;
    private bool _showAssociationEdges = true;

    // Search highlight color (bright yellow)
    private static readonly SKColor ColorNodeStrokeSearchMatch = new(0xFF, 0xE0, 0x40);

    // Colors (dark theme matching Unity)
    private static readonly SKColor ColorBg = new(0x1A, 0x1E, 0x24);
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

    public GraphCanvas()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public GraphNode? SelectedNode => _selectedNode;

    /// <summary>
    /// Sets the pan position directly (used by minimap).
    /// </summary>
    public void SetPan(float panX, float panY)
    {
        _panX = panX;
        _panY = panY;
        Invalidate();
    }

    public void SetGraph(List<GraphNode> nodes, List<GraphEdge> edges, bool preserveViewport = false)
    {
        _nodes = nodes;
        _edges = edges;
        _selectedNode = null;
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
        SelectedNode = _selectedNode,
        HoveredNode = _hoveredNode,
        SearchText = _searchText,
        SelectedDesignNodeIds = _designGraph != null ? _designSelectedNodeIds : null,
        HoveredDesignNodeIds = _designGraph != null ? _designHoveredNodeIds : null,
        IsDesignMode = _designGraph != null,
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
        _renderer.DrawEdges(canvas, _edges, GetViewportState(), excludeNodeId);
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

        // ── Design Mode routing (M2) ──
        // Guard: require both _designController AND _designGraph.
        // Without _designGraph, we fall through to Analyze Mode behavior.
        if (_designController != null && _designGraph != null && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
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

            // Normal node drag
            if (hit != null)
            {
                StartNodeDrag(hit, worldPos, (float)pos.X, (float)pos.Y);
                if (hit != _selectedNode)
                {
                    _selectedNode = hit;
                    SelectionChanged?.Invoke(hit);
                }
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }

            // Click on empty space (start pan)
            _isPanning = true;
            _lastMouseX = (float)pos.X;
            _lastMouseY = (float)pos.Y;
            Cursor = new Cursor(StandardCursorType.SizeAll);
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

        // Design Mode drag/resize routing (M2)
        // Guard: require both _designController AND _designGraph
        if (_designController != null && _designGraph != null && (_designController.IsDragging || _designController.IsResizing))
        {
            _designController.HandlePointerMoved(designWorldPos);
            // Sync GraphNode positions from DesignClass during drag for smooth live-redraw rendering.
            // The DesignCanvasController updates DesignClass.X/Y in HandlePointerMoved,
            // but the canvas renders from _nodes (GraphNode list). We must copy the
            // updated position to the matching GraphNode so the bitmap re-render shows
            // the class at its new position. This mirrors Analyze Mode's direct node.X/Y update.
            var draggedRect = _designController.GetDraggedOrResizingClassId();
            if (draggedRect != null)
            {
                var cls = _designGraph.Classes.FirstOrDefault(c => c.Id == draggedRect);
                var node = _nodes.FirstOrDefault(n => n.Id == draggedRect);
                if (cls != null && node != null)
                {
                    node.X = cls.X;
                    node.Y = cls.Y;
                    node.Width = cls.Width;
                    node.Height = cls.Height;
                }

                // Set up partial-redraw optimization if not already active
                if (_draggedNodeIdDuringRender == null)
                {
                    _draggedNodeIdDuringRender = draggedRect;
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

        // Hover detection (existing behavior, but skip during drag)
        if (!_isDraggingNode && !_isDraggingCluster)
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

        if (_isDraggingNode || _isDraggingCluster)
        {
            _isDraggingNode = false;
            _isDraggingCluster = false;

            // Only raise ManualLayoutChanged if the node actually moved
            bool moved = false;
            if (_draggedNode != null)
            {
                const float MovedEpsilon = 0.5f;
                moved = Math.Abs(_draggedNode.X - _dragStartNodeX) > MovedEpsilon
                     || Math.Abs(_draggedNode.Y - _dragStartNodeY) > MovedEpsilon;
            }
            else if (_draggedClusterId != null)
            {
                // Cluster drag: check if any node moved
                const float MovedEpsilon = 0.5f;
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

            _draggedNode = null;
            _draggedClusterId = null;
            _draggedNodeIdDuringRender = null;
            _staticContentDirty = true; // Re-record static content with the node at its new position
            Cursor = Cursor.Default;
            e.Pointer.Capture(null);

            if (moved)
            {
                _staticContentDirty = true;
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
        _draggedNode = node;
        _isDraggingNode = true;
        _isDraggingCluster = false;
        _draggedClusterId = null;
        _dragStartMouseX = worldPos.X;
        _dragStartMouseY = worldPos.Y;
        _dragStartNodeX = node.X;
        _dragStartNodeY = node.Y;
        _draggedNodeIdDuringRender = node.Id;
        _staticContentDirty = true; // Re-record static content without the dragged node
        Cursor = new Cursor(StandardCursorType.Hand);
    }

    private void StartClusterDrag(string clusterId, SKPoint worldPos, float screenX, float screenY)
    {
        _isDraggingNode = false;
        _isDraggingCluster = true;
        _draggedClusterId = clusterId;
        _draggedNode = null;
        _dragStartMouseX = worldPos.X;
        _dragStartMouseY = worldPos.Y;
        _clusterDragStartPositions.Clear();

        // Record start positions of all nodes in this cluster
        foreach (var node in _nodes)
        {
            if (GetNodeClusterId(node) == clusterId)
            {
                _clusterDragStartPositions[node.Id] = new Vector2(node.X, node.Y);
            }
        }
        Cursor = new Cursor(StandardCursorType.Hand);
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
}
