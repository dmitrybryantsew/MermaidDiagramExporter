using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// High-performance SkiaSharp graph canvas with zoom, pan, and hit testing.
/// </summary>
public class GraphCanvas : Control
{
    private List<GraphNode> _nodes = new();
    private List<GraphEdge> _edges = new();

    private float _zoom = 1.0f;
    private float _panX = 0f;
    private float _panY = 0f;

    private bool _isPanning;
    private float _lastMouseX;
    private float _lastMouseY;
    private GraphNode? _hoveredNode;
    private GraphNode? _selectedNode;

    // Styling
    private static readonly SKPaint NodeFill = new() { Color = SKColors.White, Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint NodeStroke = new() { Color = new SKColor(0x40, 0x80, 0xC0), Style = SKPaintStyle.Stroke, StrokeWidth = 2, IsAntialias = true };
    private static readonly SKPaint NodeStrokeSelected = new() { Color = new SKColor(0xFF, 0x8C, 0x00), Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
    private static readonly SKPaint NodeStrokeHover = new() { Color = new SKColor(0x60, 0xA0, 0xE0), Style = SKPaintStyle.Stroke, StrokeWidth = 3, IsAntialias = true };
    private static readonly SKPaint EdgePaint = new() { Color = new SKColor(0x60, 0x60, 0x60), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
    private static readonly SKPaint EdgePaintStrong = new() { Color = new SKColor(0x30, 0x50, 0x80), Style = SKPaintStyle.Stroke, StrokeWidth = 2.5f, IsAntialias = true };
    private static readonly SKPaint TextPaint = new() { Color = SKColors.Black, IsAntialias = true, TextSize = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
    private static readonly SKPaint TextPaintBold = new() { Color = SKColors.Black, IsAntialias = true, TextSize = 12, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) };
    private static readonly SKPaint TextPaintSmall = new() { Color = new SKColor(0x60, 0x60, 0x60), IsAntialias = true, TextSize = 10, Typeface = SKTypeface.FromFamilyName("Segoe UI") };
    private static readonly SKPaint ArrowPaint = new() { Color = new SKColor(0x60, 0x60, 0x60), Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint NamespaceBgPaint = new() { Color = new SKColor(0xF0, 0xF5, 0xFA), Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint NamespaceBorderPaint = new() { Color = new SKColor(0xD0, 0xDD, 0xEA), Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
    private static readonly SKPaint NamespaceTextPaint = new() { Color = new SKColor(0x80, 0x90, 0xA0), IsAntialias = true, TextSize = 11, Typeface = SKTypeface.FromFamilyName("Segoe UI", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright) };

    private const float NodePaddingX = 12;
    private const float NodeHeaderHeight = 24;
    private const float NodeMemberHeight = 16;
    private const float NamespacePadding = 16;
    private const float NamespaceTitleHeight = 20;

    public event Action<GraphNode?>? SelectionChanged;

    public GraphNode? SelectedNode => _selectedNode;

    public void SetGraph(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _edges = edges;
        _selectedNode = null;
        _hoveredNode = null;
        FitToScreen();
        InvalidateVisual();
    }

    public void FitToScreen()
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

        float graphW = maxX - minX + 40;
        float graphH = maxY - minY + 40;
        float viewW = (float)Bounds.Width;
        float viewH = (float)Bounds.Height;

        if (viewW <= 0 || viewH <= 0 || graphW <= 0 || graphH <= 0) return;

        float zoomX = viewW / graphW;
        float zoomY = viewH / graphH;
        _zoom = Math.Min(zoomX, zoomY) * 0.9f;
        _panX = (viewW - graphW * _zoom) / 2 - minX * _zoom + 20 * _zoom;
        _panY = (viewH - graphH * _zoom) / 2 - minY * _zoom + 20 * _zoom;
        InvalidateVisual();
    }

    public void ZoomBy(float factor)
    {
        float newZoom = Math.Clamp(_zoom * factor, 0.1f, 5.0f);
        float cx = (float)Bounds.Width / 2;
        float cy = (float)Bounds.Height / 2;
        float worldX = (cx - _panX) / _zoom;
        float worldY = (cy - _panY) / _zoom;
        _panX = cx - worldX * newZoom;
        _panY = cy - worldY * newZoom;
        _zoom = newZoom;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        context.Custom(new GraphDrawOperation(_nodes, _edges, _zoom, _panX, _panY, _selectedNode, _hoveredNode));
    }

    private class GraphDrawOperation : ICustomDrawOperation
    {
        private readonly List<GraphNode> _nodes;
        private readonly List<GraphEdge> _edges;
        private readonly float _zoom;
        private readonly float _panX;
        private readonly float _panY;
        private readonly GraphNode? _selected;
        private readonly GraphNode? _hovered;

        public Rect Bounds { get; set; }

        public GraphDrawOperation(List<GraphNode> nodes, List<GraphEdge> edges, float zoom, float panX, float panY, GraphNode? selected, GraphNode? hovered)
        {
            _nodes = nodes;
            _edges = edges;
            _zoom = zoom;
            _panX = panX;
            _panY = panY;
            _selected = selected;
            _hovered = hovered;
        }

        public bool Equals(ICustomDrawOperation? other) => false;
        public bool HitTest(Point p) => true;
        public void Dispose() { }

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null) return;
            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            canvas.Clear(SKColors.White);

            canvas.Save();
            var matrix = SKMatrix.CreateScaleTranslation(_zoom, _zoom, _panX, _panY);
            canvas.SetMatrix(matrix);

            DrawNamespaceGroups(canvas);
            DrawEdges(canvas);
            DrawNodes(canvas);

            canvas.Restore();
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
                float w = maxX - minX + NamespacePadding * 2;
                float h = maxY - minY + NamespacePadding * 2 + NamespaceTitleHeight;

                var rect = new SKRect(x, y, x + w, y + h);
                canvas.DrawRoundRect(rect, 8, 8, NamespaceBgPaint);
                canvas.DrawRoundRect(rect, 8, 8, NamespaceBorderPaint);
                canvas.DrawText(ns, x + 8, y + NamespaceTitleHeight - 4, NamespaceTextPaint);
            }
        }

        private void DrawEdges(SKCanvas canvas)
        {
            foreach (var edge in _edges)
            {
                if (edge.FromNode == null || edge.ToNode == null) continue;
                var from = new SKPoint(edge.FromNode.X + edge.FromNode.Width / 2, edge.FromNode.Y + edge.FromNode.Height / 2);
                var to = new SKPoint(edge.ToNode.X + edge.ToNode.Width / 2, edge.ToNode.Y + edge.ToNode.Height / 2);
                var paint = edge.IsStrongRelation ? EdgePaintStrong : EdgePaint;
                canvas.DrawLine(from, to, paint);
                DrawArrowhead(canvas, from, to, paint.Color);
            }
        }

        private void DrawArrowhead(SKCanvas canvas, SKPoint from, SKPoint to, SKColor color)
        {
            float arrowSize = 8;
            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1) return;
            dx /= len;
            dy /= len;

            float px = to.X - dx * 12;
            float py = to.Y - dy * 12;

            ArrowPaint.Color = color;
            var path = new SKPath();
            path.MoveTo(px, py);
            path.LineTo(px - dx * arrowSize + dy * arrowSize * 0.4f, py - dy * arrowSize - dx * arrowSize * 0.4f);
            path.LineTo(px - dx * arrowSize - dy * arrowSize * 0.4f, py - dy * arrowSize + dx * arrowSize * 0.4f);
            path.Close();
            canvas.DrawPath(path, ArrowPaint);
        }

        private void DrawNodes(SKCanvas canvas)
        {
            foreach (var node in _nodes)
            {
                var rect = new SKRect(node.X, node.Y, node.X + node.Width, node.Y + node.Height);
                canvas.DrawRoundRect(rect, 4, 4, NodeFill);

                var stroke = node == _selected ? NodeStrokeSelected
                           : node == _hovered ? NodeStrokeHover
                           : NodeStroke;
                canvas.DrawRoundRect(rect, 4, 4, stroke);

                var headerRect = new SKRect(node.X, node.Y, node.X + node.Width, node.Y + NodeHeaderHeight);
                using var headerPath = new SKPath();
                headerPath.AddRoundRect(new SKRect(rect.Left, rect.Top, rect.Right, rect.Top + NodeHeaderHeight), 4, 4);
                canvas.DrawPath(headerPath, stroke);

                float textX = node.X + NodePaddingX;
                float textY = node.Y + NodeHeaderHeight - 6;
                canvas.DrawText(node.DisplayName, textX, textY, TextPaintBold);

                float memberY = node.Y + NodeHeaderHeight + 4;
                foreach (var member in node.Members)
                {
                    string prefix = member.Kind == "Method" ? "  " : "+ ";
                    string text = $"{prefix}{member.TypeName} {member.Name}";
                    if (member.Kind == "Method") text += "()";
                    canvas.DrawText(text, textX, memberY + 12, TextPaintSmall);
                    memberY += NodeMemberHeight;
                }
            }
        }
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);
        float zoomDelta = (float)(e.Delta.Y * 0.1f);
        float newZoom = Math.Clamp(_zoom * (1 + zoomDelta), 0.1f, 5.0f);

        var cursorPos = e.GetPosition(this);
        float worldX = (float)(cursorPos.X - _panX) / _zoom;
        float worldY = (float)(cursorPos.Y - _panY) / _zoom;
        _panX = (float)cursorPos.X - worldX * newZoom;
        _panY = (float)cursorPos.Y - worldY * newZoom;
        _zoom = newZoom;
        InvalidateVisual();
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
            InvalidateVisual();
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
            InvalidateVisual();
            return;
        }

        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
        var hovered = HitTest(worldPos);
        if (hovered != _hoveredNode)
        {
            _hoveredNode = hovered;
            InvalidateVisual();
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
        foreach (var node in _nodes)
        {
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
