using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;
using MermaidDiagramExporter.Gui.Layout;
using LayoutRect = MermaidDiagramExporter.Gui.Layout.Rect;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// A minimap showing the full graph with a viewport rectangle overlay.
/// Clicking on the minimap pans the main canvas to that location.
/// </summary>
public partial class MinimapControl : UserControl
{
    private List<GraphNode> _nodes = new();
    private List<GraphEdge> _edges = new();
    private LayoutRect _graphBounds;
    private float _mainCanvasZoom = 1f;
    private float _mainCanvasPanX = 0f;
    private float _mainCanvasPanY = 0f;
    private float _mainCanvasViewportW = 800f;
    private float _mainCanvasViewportH = 600f;

    private static readonly SKColor MinimapBg = new(0x15, 0x19, 0x1E);
    private static readonly SKColor MinimapNodeFill = new(0x2D, 0x33, 0x3F);
    private static readonly SKColor MinimapNodeStroke = new(0x4A, 0x6A, 0x8A);
    private static readonly SKColor MinimapEdgeColor = new(0x3A, 0x42, 0x50);
    private static readonly SKColor ViewportBorderColor = new(0xFF, 0xE0, 0x40);

    /// <summary>
    /// Raised when the user clicks on the minimap. Arguments are the requested pan offset.
    /// </summary>
    public event Action<float, float>? ViewportJumpRequested;

    public MinimapControl()
    {
        InitializeComponent();
        PointerPressed += OnMinimapPointerPressed;
    }

    /// <summary>
    /// Updates the minimap content. Call whenever the graph changes.
    /// </summary>
    public void SetGraph(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _edges = edges;
        RecalculateGraphBounds();
        RenderMinimap();
        UpdateViewportRect();
    }

    /// <summary>
    /// Updates the viewport rectangle position to match the main canvas view state.
    /// Call on every pan/zoom of the main canvas.
    /// </summary>
    public void UpdateViewport(float zoom, float panX, float panY, float viewW, float viewH)
    {
        _mainCanvasZoom = zoom;
        _mainCanvasPanX = panX;
        _mainCanvasPanY = panY;
        _mainCanvasViewportW = viewW;
        _mainCanvasViewportH = viewH;
        UpdateViewportRect();
    }

    private void RecalculateGraphBounds()
    {
        if (_nodes.Count == 0)
        {
            _graphBounds = new LayoutRect(0, 0, 200, 150);
            return;
        }

        float minX = _nodes.Min(n => n.X);
        float minY = _nodes.Min(n => n.Y);
        float maxX = _nodes.Max(n => n.X + n.Width);
        float maxY = _nodes.Max(n => n.Y + n.Height);

        float padding = 50;
        _graphBounds = new LayoutRect(minX - padding, minY - padding,
            maxX - minX + padding * 2, maxY - minY + padding * 2);
    }

    private void RenderMinimap()
    {
        if (_nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            MinimapImage.Source = null;
            return;
        }

        int w = (int)Bounds.Width;
        int h = (int)Bounds.Height;
        if (w <= 0 || h <= 0) return;

        float scaleX = w / _graphBounds.Width;
        float scaleY = h / _graphBounds.Height;
        float minimapScale = Math.Min(scaleX, scaleY);

        using var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(MinimapBg);

        float offsetX = (w - _graphBounds.Width * minimapScale) / 2 - _graphBounds.X * minimapScale;
        float offsetY = (h - _graphBounds.Height * minimapScale) / 2 - _graphBounds.Y * minimapScale;

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(minimapScale);

        // Draw edges (simplified)
        using var edgePaint = new SKPaint
        {
            Color = MinimapEdgeColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f / minimapScale,
            IsAntialias = true
        };

        foreach (var edge in _edges)
        {
            if (edge.FromNode == null || edge.ToNode == null) continue;
            float x1 = edge.FromNode.X + edge.FromNode.Width / 2;
            float y1 = edge.FromNode.Y + edge.FromNode.Height / 2;
            float x2 = edge.ToNode.X + edge.ToNode.Width / 2;
            float y2 = edge.ToNode.Y + edge.ToNode.Height / 2;
            canvas.DrawLine(x1, y1, x2, y2, edgePaint);
        }

        // Draw nodes (simplified as rectangles)
        using var nodeFillPaint = new SKPaint { Color = MinimapNodeFill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var nodeStrokePaint = new SKPaint { Color = MinimapNodeStroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1f / minimapScale, IsAntialias = true };

        foreach (var node in _nodes)
        {
            canvas.DrawRect(node.X, node.Y, node.Width, node.Height, nodeFillPaint);
            canvas.DrawRect(node.X, node.Y, node.Width, node.Height, nodeStrokePaint);
        }

        // Convert to Avalonia bitmap
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var ms = new System.IO.MemoryStream(data.ToArray());
        var avaloniaBitmap = new Bitmap(ms);
        MinimapImage.Source = avaloniaBitmap;
    }

    private void UpdateViewportRect()
    {
        if (_nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            ViewportRect.IsVisible = false;
            return;
        }

        ViewportRect.IsVisible = true;

        int w = (int)Bounds.Width;
        int h = (int)Bounds.Height;
        float scaleX = w / _graphBounds.Width;
        float scaleY = h / _graphBounds.Height;
        float minimapScale = Math.Min(scaleX, scaleY);

        float offsetX = (w - _graphBounds.Width * minimapScale) / 2 - _graphBounds.X * minimapScale;
        float offsetY = (h - _graphBounds.Height * minimapScale) / 2 - _graphBounds.Y * minimapScale;

        float vpX = (-_mainCanvasPanX / _mainCanvasZoom) * minimapScale + offsetX;
        float vpY = (-_mainCanvasPanY / _mainCanvasZoom) * minimapScale + offsetY;
        float vpW = (_mainCanvasViewportW / _mainCanvasZoom) * minimapScale;
        float vpH = (_mainCanvasViewportH / _mainCanvasZoom) * minimapScale;

        vpX = Math.Max(0, Math.Min(w, vpX));
        vpY = Math.Max(0, Math.Min(h, vpY));
        vpW = Math.Max(10, Math.Min(w, vpW));
        vpH = Math.Max(10, Math.Min(h, vpH));

        Canvas.SetLeft(ViewportRect, vpX);
        Canvas.SetTop(ViewportRect, vpY);
        ViewportRect.Width = vpW;
        ViewportRect.Height = vpH;
    }

    private void OnMinimapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        float clickX = (float)pos.X;
        float clickY = (float)pos.Y;

        int w = (int)Bounds.Width;
        int h = (int)Bounds.Height;
        if (w <= 0 || h <= 0) return;

        float scaleX = w / _graphBounds.Width;
        float scaleY = h / _graphBounds.Height;
        float minimapScale = Math.Min(scaleX, scaleY);
        float offsetX = (w - _graphBounds.Width * minimapScale) / 2 - _graphBounds.X * minimapScale;
        float offsetY = (h - _graphBounds.Height * minimapScale) / 2 - _graphBounds.Y * minimapScale;

        float worldX = (clickX - offsetX) / minimapScale;
        float worldY = (clickY - offsetY) / minimapScale;

        float newPanX = _mainCanvasViewportW / 2 - worldX * _mainCanvasZoom;
        float newPanY = _mainCanvasViewportH / 2 - worldY * _mainCanvasZoom;

        ViewportJumpRequested?.Invoke(newPanX, newPanY);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_nodes.Count > 0)
            RenderMinimap();
    }
}
