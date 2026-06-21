using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using Avalonia.Threading;
using SkiaSharp;

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
    private WriteableBitmap? _writeableBitmap;

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

    public event Action<GraphNode?>? SelectionChanged;

    public GraphCanvas()
    {
        // Don't set Background here - Control doesn't have it in all versions
    }

    public GraphNode? SelectedNode => _selectedNode;

    public void SetGraph(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _edges = edges;
        _selectedNode = null;
        _hoveredNode = null;
        FitToScreen();
        Invalidate();
    }

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
        RecalculateLayout(w, h);

        using var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColor.Parse("#1A1E24"));
        canvas.Save();
        canvas.Translate(_panX, _panY);
        canvas.Scale(_zoom);
        DrawNamespaceGroups(canvas);
        DrawEdges(canvas);
        DrawNodes(canvas);
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
        RecalculateLayout((int)Math.Max(1, Bounds.Width), (int)Math.Max(1, Bounds.Height));
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
        Invalidate();
    }

    private void Invalidate()
    {
        _needsRender = true;
        InvalidateVisual();
    }

    private void RenderNow()
    {
        int w = (int)Math.Max(1, Bounds.Width);
        int h = (int)Math.Max(1, Bounds.Height);
        if (w <= 1 || h <= 1) return;

        _needsRender = false;

        // Recalculate layout for current size
        RecalculateLayout(w, h);

        _writeableBitmap?.Dispose();
        _writeableBitmap = new WriteableBitmap(
            new PixelSize(w, h), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Premul);

        using var framebuffer = _writeableBitmap.Lock();
        var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(info, framebuffer.Address, framebuffer.RowBytes);
        var canvas = surface.Canvas;

        canvas.Clear(SKColor.Parse("#1A1E24"));
        canvas.Save();
        canvas.Translate(_panX, _panY);
        canvas.Scale(_zoom);
        DrawNamespaceGroups(canvas);
        DrawEdges(canvas);
        DrawNodes(canvas);
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

        // Render to WriteableBitmap and present it
        RenderNow();

        if (_writeableBitmap != null)
        {
            var srcRect = new Rect(0, 0, _writeableBitmap.PixelSize.Width, _writeableBitmap.PixelSize.Height);
            var destRect = new Rect(0, 0, w, h);
            context.DrawImage(_writeableBitmap, srcRect, destRect);
        }
    }

    private void DrawNamespaceGroups(SKCanvas canvas)
    {
        var groups = new Dictionary<string, List<GraphNode>>();
        foreach (var node in _nodes)
        {
            if (!groups.ContainsKey(node.Namespace))
                groups[node.Namespace] = new();
            groups[node.Namespace].Add(node);
        }

        foreach (var (ns, nsNodes) in groups)
        {
            if (string.IsNullOrEmpty(ns) || nsNodes.Count == 0) continue;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in nsNodes)
            {
                minX = Math.Min(minX, n.X);
                minY = Math.Min(minY, n.Y);
                maxX = Math.Max(maxX, n.X + n.Width);
                maxY = Math.Max(maxY, n.Y + n.Height);
            }

            float x = minX - NamespacePadding;
            float y = minY - NamespacePadding - NamespaceTitleHeight;
            float ww = maxX - minX + NamespacePadding * 2;
            float hh = maxY - minY + NamespacePadding * 2 + NamespaceTitleHeight;

            using var bgPaint = new SKPaint { Color = ColorNamespaceBg, Style = SKPaintStyle.Fill, IsAntialias = true };
            using var borderPaint = new SKPaint { Color = ColorNamespaceBorder, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            using var textPaint = new SKPaint { Color = ColorNamespaceText, IsAntialias = true, TextSize = 13 };

            canvas.DrawRoundRect(x, y, ww, hh, 8, 8, bgPaint);
            canvas.DrawRoundRect(x, y, ww, hh, 8, 8, borderPaint);
            canvas.DrawText(ns, x + 12, y + NamespaceTitleHeight - 6, textPaint);
        }
    }

    private void DrawEdges(SKCanvas canvas)
    {
        foreach (var edge in _edges)
        {
            if (edge.FromNode == null || edge.ToNode == null) continue;

            float fromX = edge.FromNode.X + edge.FromNode.Width;
            float fromY = edge.FromNode.Y + edge.FromNode.Height / 2;
            float toX = edge.ToNode.X;
            float toY = edge.ToNode.Y + edge.ToNode.Height / 2;

            SKColor edgeColor = edge.IsStrongRelation ? ColorEdgeInheritance : ColorEdgeAssociation;
            float strokeWidth = edge.IsStrongRelation ? 2.0f : 1.2f;

            if (_selectedNode != null && (edge.FromNode.Id == _selectedNode.Id || edge.ToNode.Id == _selectedNode.Id))
            {
                edgeColor = edge.IsStrongRelation ? ColorEdgeInheritance : ColorEdgeImplements;
                strokeWidth += 0.5f;
            }

            using var paint = new SKPaint
            {
                Color = edgeColor,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = strokeWidth,
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round
            };

            float dx = toX - fromX;
            float controlOffset = Math.Max(40, Math.Min(150, Math.Abs(dx) * 0.4f));

            using var path = new SKPath();
            path.MoveTo(fromX, fromY);
            path.CubicTo(
                fromX + controlOffset, fromY,
                toX - controlOffset, toY,
                toX, toY);
            canvas.DrawPath(path, paint);

            DrawArrowhead(canvas, toX, toY, edgeColor);
        }
    }

    private void DrawArrowhead(SKCanvas canvas, float x, float y, SKColor color)
    {
        float arrowLen = 10;
        float arrowWidth = 5;

        using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill, IsAntialias = true };
        var path = new SKPath();
        path.MoveTo(x, y);
        path.LineTo(x - arrowLen, y - arrowWidth);
        path.LineTo(x - arrowLen, y + arrowWidth);
        path.Close();
        canvas.DrawPath(path, paint);
    }

    private void DrawNodes(SKCanvas canvas)
    {
        foreach (var node in _nodes)
        {
            float x = node.X, y = node.Y, w = node.Width, h = node.Height;

            using var fillPaint = new SKPaint { Color = ColorNodeFill, Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(x, y, w, h, 6, 6, fillPaint);

            var strokeColor = node == _selectedNode ? ColorNodeStrokeSelected
                           : node == _hoveredNode ? ColorNodeStrokeHover
                           : ColorNodeStroke;
            using var strokePaint = new SKPaint { Color = strokeColor, Style = SKPaintStyle.Stroke, StrokeWidth = node == _selectedNode ? 3 : 1.5f, IsAntialias = true };
            canvas.DrawRoundRect(x, y, w, h, 6, 6, strokePaint);

            using var headerPaint = new SKPaint { Color = strokeColor.WithAlpha(40), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRoundRect(x, y, w, NodeHeaderHeight, 6, 6, headerPaint);
            canvas.DrawRect(x, y + NodeHeaderHeight - 4, w, 4, headerPaint);

            string? badge = GetBadgeText(node);
            if (badge != null)
            {
                using var badgePaint = new SKPaint { Color = GetBadgeColor(node), Style = SKPaintStyle.Fill, IsAntialias = true };
                using var badgeTextPaint = new SKPaint { Color = SKColors.White, IsAntialias = true, TextSize = 9 };
                float badgeW = badgeTextPaint.MeasureText(badge) + 10;
                float badgeH = 16;
                float badgeX = x + w - badgeW - 6;
                float badgeY = y + 6;
                canvas.DrawRoundRect(badgeX, badgeY, badgeW, badgeH, 3, 3, badgePaint);
                canvas.DrawText(badge, badgeX + 5, badgeY + 12, badgeTextPaint);
            }

            using var namePaint = new SKPaint { Color = ColorText, IsAntialias = true, TextSize = 12 };
            canvas.DrawText(node.DisplayName, x + NodePaddingX, y + NodeHeaderHeight - 8, namePaint);

            using var memberPaint = new SKPaint { Color = ColorTextMuted, IsAntialias = true, TextSize = 10 };
            float memberY = y + NodeHeaderHeight + 14;
            int count = 0;
            foreach (var member in node.Members)
            {
                if (count >= 6) break;
                string prefix = member.Kind == "Method" ? "  " : "+ ";
                string text = prefix + member.TypeName + " " + member.Name;
                if (member.Kind == "Method") text += "()";
                canvas.DrawText(text, x + NodePaddingX, memberY, memberPaint);
                memberY += NodeMemberHeight;
                count++;
            }
        }
    }

    private static string? GetBadgeText(GraphNode node)
    {
        return node.Kind switch
        {
            "Interface" => "IF",
            "Enum" => "EN",
            "Struct" => "ST",
            "StaticClass" => "SC",
            "AbstractClass" => "AB",
            _ => null
        };
    }

    private static SKColor GetBadgeColor(GraphNode node)
    {
        return node.Kind switch
        {
            "Interface" => ColorBadgeInterface,
            "Enum" => ColorBadgeEnum,
            "Struct" => ColorBadgeStruct,
            "StaticClass" => ColorBadgeStatic,
            "AbstractClass" => ColorBadgeAbstract,
            _ => ColorNodeStroke
        };
    }

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
        Invalidate();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pos = e.GetPosition(this);

        if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
            (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
             (e.KeyModifiers & KeyModifiers.Control) != 0))
        {
            _isPanning = true;
            _lastMouseX = (float)pos.X;
            _lastMouseY = (float)pos.Y;
            Cursor = new Cursor(StandardCursorType.SizeAll);
            return;
        }

        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
        var hit = HitTest(worldPos);
        if (hit != _selectedNode)
        {
            _selectedNode = hit;
            SelectionChanged?.Invoke(hit);
            Invalidate();
        }
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);

        if (_isPanning)
        {
            float dx = (float)(pos.X - _lastMouseX);
            float dy = (float)(pos.Y - _lastMouseY);
            _panX += dx;
            _panY += dy;
            _lastMouseX = (float)pos.X;
            _lastMouseY = (float)pos.Y;
            Invalidate();
            return;
        }

        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
        var hovered = HitTest(worldPos);
        if (hovered != _hoveredNode)
        {
            _hoveredNode = hovered;
            Invalidate();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isPanning = false;
        Cursor = Cursor.Default;
    }

    private SKPoint ScreenToWorld(float screenX, float screenY)
    {
        return new SKPoint((screenX - _panX) / _zoom, (screenY - _panY) / _zoom);
    }

    private GraphNode? HitTest(SKPoint worldPos)
    {
        for (int i = _nodes.Count - 1; i >= 0; i--)
        {
            var node = _nodes[i];
            if (worldPos.X >= node.X && worldPos.X <= node.X + node.Width &&
                worldPos.Y >= node.Y && worldPos.Y <= node.Y + node.Height)
            {
                return node;
            }
        }
        return null;
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
}
