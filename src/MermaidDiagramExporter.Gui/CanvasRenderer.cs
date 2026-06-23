using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Immutable snapshot of viewport state needed for rendering.
/// Passed to CanvasRenderer methods so they don't depend on GraphCanvas's mutable fields.
/// </summary>
public sealed class ViewportState
{
    public float Zoom { get; init; }
    public float PanX { get; init; }
    public float PanY { get; init; }
    public bool ShowInheritanceEdges { get; init; }
    public bool ShowImplementsEdges { get; init; }
    public bool ShowAssociationEdges { get; init; }
    public GraphNode? SelectedNode { get; init; }
    public GraphNode? HoveredNode { get; init; }
    public string SearchText { get; init; } = string.Empty;
}

/// <summary>
/// Owns all SkiaSharp rendering logic previously inline in GraphCanvas.
/// Extracted in Step 17 to separate rendering from input handling and control lifecycle.
/// </summary>
public sealed class CanvasRenderer
{
    // ── Cached SKPaint objects (reused across frames to reduce GC pressure) ──
    private static readonly SKPaint NamespaceBgPaint = new()
    {
        Color = new SKColor(0x25, 0x2A, 0x32), Style = SKPaintStyle.Fill, IsAntialias = true
    };
    private static readonly SKPaint NamespaceBorderPaint = new()
    {
        Color = new SKColor(0x3A, 0x42, 0x50), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true
    };
    private static readonly SKPaint NamespaceTextPaint = new()
    {
        Color = new SKColor(0x70, 0x80, 0x90), IsAntialias = true, TextSize = 13
    };
    private static readonly SKPaint EdgeLabelPaint = new()
    {
        Color = new SKColor(0x88, 0x90, 0x98), IsAntialias = true, TextSize = 9
    };
    private static readonly SKPaint NodeFillPaint = new()
    {
        Color = new SKColor(0x2D, 0x33, 0x3F), Style = SKPaintStyle.Fill, IsAntialias = true
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
        Color = new SKColor(0xE0, 0xE6, 0xEC), IsAntialias = true, TextSize = 12
    };
    private static readonly SKPaint NodeMemberPaint = new()
    {
        Color = new SKColor(0x88, 0x90, 0x98), IsAntialias = true, TextSize = 10
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

    // Colors
    private static readonly SKColor ColorEdgeInheritance = new(0x50, 0x90, 0xD0);
    private static readonly SKColor ColorEdgeImplements = new(0x40, 0xB0, 0x70);
    private static readonly SKColor ColorEdgeAssociation = new(0x60, 0x60, 0x60);
    private static readonly SKColor ColorNodeStroke = new(0x4A, 0x6A, 0x8A);
    private static readonly SKColor ColorNodeStrokeSelected = new(0xFF, 0x8C, 0x00);
    private static readonly SKColor ColorNodeStrokeHover = new(0x60, 0xA0, 0xE0);
    private static readonly SKColor ColorNodeStrokeSearchMatch = new(0xFF, 0xE0, 0x40);
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
    private const int MaxMembersShownPerNode = 6;
    private const float ArrowheadLength = 10f;
    private const float ArrowheadHalfWidth = 5f;

    /// <summary>
    /// Draws namespace group backgrounds and labels.
    /// </summary>
    public void DrawNamespaceGroups(SKCanvas canvas, List<GraphNode> nodes)
    {
        var groups = new Dictionary<string, List<GraphNode>>();
        foreach (var node in nodes)
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

            canvas.DrawRoundRect(x, y, ww, hh, 8, 8, NamespaceBgPaint);
            canvas.DrawRoundRect(x, y, ww, hh, 8, 8, NamespaceBorderPaint);
            canvas.DrawText(ns, x + 12, y + NamespaceTitleHeight - 6, NamespaceTextPaint);
        }
    }

    /// <summary>
    /// Draws all edges, optionally excluding edges connected to a specific node (for drag optimization).
    /// </summary>
    public void DrawEdges(SKCanvas canvas, List<GraphEdge> edges, ViewportState vp, string? excludeNodeId = null)
    {
        foreach (var edge in edges)
        {
            if (edge.FromNode == null || edge.ToNode == null) continue;
            if (excludeNodeId != null && (edge.FromNode.Id == excludeNodeId || edge.ToNode.Id == excludeNodeId))
                continue;

            bool visible = edge.Kind switch
            {
                TypeEdgeKind.Inheritance => vp.ShowInheritanceEdges,
                TypeEdgeKind.Implements => vp.ShowImplementsEdges,
                TypeEdgeKind.Association => vp.ShowAssociationEdges,
                _ => true
            };
            if (!visible) continue;

            float fromX = edge.FromNode.X + edge.FromNode.Width;
            float fromY = edge.FromNode.Y + edge.FromNode.Height / 2;
            float toX = edge.ToNode.X;
            float toY = edge.ToNode.Y + edge.ToNode.Height / 2;

            SKColor edgeColor = edge.Kind switch
            {
                TypeEdgeKind.Inheritance => ColorEdgeInheritance,
                TypeEdgeKind.Implements => ColorEdgeImplements,
                _ => ColorEdgeAssociation
            };
            float strokeWidth = edge.Kind == TypeEdgeKind.Association ? 1.2f : 2.0f;

            if (vp.SelectedNode != null && (edge.FromNode.Id == vp.SelectedNode.Id || edge.ToNode.Id == vp.SelectedNode.Id))
            {
                strokeWidth += 0.5f;
            }

            EdgeStrokePaint.Color = edgeColor;
            EdgeStrokePaint.StrokeWidth = strokeWidth;

            float dx = toX - fromX;
            float controlOffset = Math.Max(40, Math.Min(150, Math.Abs(dx) * 0.4f));

            using var path = new SKPath();
            path.MoveTo(fromX, fromY);
            path.CubicTo(fromX + controlOffset, fromY, toX - controlOffset, toY, toX, toY);
            canvas.DrawPath(path, EdgeStrokePaint);

            if (!string.IsNullOrEmpty(edge.Label))
            {
                float midX = (fromX + toX) / 2;
                float midY = (fromY + toY) / 2;
                canvas.DrawText(edge.Label, midX, midY - 4, EdgeLabelPaint);
            }

            DrawArrowhead(canvas, toX, toY, edgeColor);
        }
    }

    /// <summary>
    /// Draws all nodes with search highlighting, selection, and hover states.
    /// </summary>
    public void DrawNodes(SKCanvas canvas, List<GraphNode> nodes, ViewportState vp)
    {
        bool searchActive = !string.IsNullOrWhiteSpace(vp.SearchText);

        foreach (var node in nodes)
        {
            float x = node.X, y = node.Y, w = node.Width, h = node.Height;

            bool searchMatch = false;
            if (searchActive)
            {
                searchMatch =
                    node.DisplayName.Contains(vp.SearchText, StringComparison.OrdinalIgnoreCase)
                    || node.Namespace.Contains(vp.SearchText, StringComparison.OrdinalIgnoreCase)
                    || node.Id.Contains(vp.SearchText, StringComparison.OrdinalIgnoreCase);
            }

            canvas.DrawRoundRect(x, y, w, h, 6, 6, NodeFillPaint);

            var strokeColor = searchActive && searchMatch ? ColorNodeStrokeSearchMatch
                           : node == vp.SelectedNode ? ColorNodeStrokeSelected
                           : node == vp.HoveredNode ? ColorNodeStrokeHover
                           : ColorNodeStroke;
            float strokeWidth = (node == vp.SelectedNode || (searchActive && searchMatch)) ? 3 : 1.5f;
            NodeStrokePaint.Color = strokeColor;
            NodeStrokePaint.StrokeWidth = strokeWidth;
            canvas.DrawRoundRect(x, y, w, h, 6, 6, NodeStrokePaint);

            NodeHeaderPaint.Color = strokeColor.WithAlpha(40);
            canvas.DrawRoundRect(x, y, w, NodeHeaderHeight, 6, 6, NodeHeaderPaint);
            canvas.DrawRect(x, y + NodeHeaderHeight - 4, w, 4, NodeHeaderPaint);

            string? badge = GetBadgeText(node);
            if (badge != null)
            {
                BadgeFillPaint.Color = GetBadgeColor(node);
                float badgeW = BadgeTextPaint.MeasureText(badge) + 10;
                float badgeH = 16;
                float badgeX = x + w - badgeW - 6;
                float badgeY = y + 6;
                canvas.DrawRoundRect(badgeX, badgeY, badgeW, badgeH, 3, 3, BadgeFillPaint);
                canvas.DrawText(badge, badgeX + 5, badgeY + 12, BadgeTextPaint);
            }

            if (node.StereotypeBadges.Count > 0)
            {
                float badgeSpacing = 4;
                float badgeH = 14;
                float currentBadgeY = y + 6;
                float currentBadgeX = x + w - 6;
                if (badge != null)
                {
                    float existingBadgeW = BadgeTextPaint.MeasureText(badge) + 10;
                    currentBadgeX -= existingBadgeW + badgeSpacing;
                }

                foreach (var stBadge in node.StereotypeBadges)
                {
                    StereotypeBadgeFillPaint.Color = SKColor.TryParse(stBadge.ColorHex, out var c) ? c : SKColor.Parse("#9E9E9E");
                    float stBadgeW = StereotypeBadgeTextPaint.MeasureText(stBadge.Label) + 10;
                    currentBadgeX -= stBadgeW;
                    canvas.DrawRoundRect(currentBadgeX, currentBadgeY, stBadgeW, badgeH, 3, 3, StereotypeBadgeFillPaint);
                    canvas.DrawText(stBadge.Label, currentBadgeX + 5, currentBadgeY + 10, StereotypeBadgeTextPaint);
                    currentBadgeX -= badgeSpacing;
                }
            }

            canvas.DrawText(node.DisplayName, x + NodePaddingX, y + NodeHeaderHeight - 8, NodeNamePaint);

            float memberY = y + NodeHeaderHeight + 14;
            int count = 0;
            foreach (var member in node.Members)
            {
                if (count >= MaxMembersShownPerNode) break;
                string prefix = member.Kind == "Method" ? "  " : "+ ";
                string text = prefix + member.TypeName + " " + member.Name;
                if (member.Kind == "Method") text += "()";
                canvas.DrawText(text, x + NodePaddingX, memberY, NodeMemberPaint);
                memberY += NodeMemberHeight;
                count++;
            }
        }
    }

    /// <summary>
    /// Draws a single node (and its connected edges) during drag operations.
    /// </summary>
    public void DrawSingleNode(SKCanvas canvas, List<GraphEdge> edges, GraphNode node, ViewportState vp)
    {
        float x = node.X, y = node.Y, w = node.Width, h = node.Height;

        // Draw connected edges first
        foreach (var edge in edges)
        {
            if (edge.FromNode == null || edge.ToNode == null) continue;
            bool connected = edge.FromNode.Id == node.Id || edge.ToNode.Id == node.Id;
            if (!connected) continue;

            bool visible = edge.Kind switch
            {
                TypeEdgeKind.Inheritance => vp.ShowInheritanceEdges,
                TypeEdgeKind.Implements => vp.ShowImplementsEdges,
                TypeEdgeKind.Association => vp.ShowAssociationEdges,
                _ => true
            };
            if (!visible) continue;

            SKColor edgeColor = edge.Kind switch
            {
                TypeEdgeKind.Inheritance => ColorEdgeInheritance,
                TypeEdgeKind.Implements => ColorEdgeImplements,
                _ => ColorEdgeAssociation
            };
            float strokeWidth = edge.Kind == TypeEdgeKind.Association ? 1.2f : 2.0f;

            float fromX = edge.FromNode.X + edge.FromNode.Width;
            float fromY = edge.FromNode.Y + edge.FromNode.Height / 2;
            float toX = edge.ToNode.X;
            float toY = edge.ToNode.Y + edge.ToNode.Height / 2;

            EdgeStrokePaint.Color = edgeColor;
            EdgeStrokePaint.StrokeWidth = strokeWidth;

            float dx = toX - fromX;
            float controlOffset = Math.Max(40, Math.Min(150, Math.Abs(dx) * 0.4f));

            using var path = new SKPath();
            path.MoveTo(fromX, fromY);
            path.CubicTo(fromX + controlOffset, fromY, toX - controlOffset, toY, toX, toY);
            canvas.DrawPath(path, EdgeStrokePaint);

            DrawArrowhead(canvas, toX, toY, edgeColor);
        }

        // Draw the node itself
        canvas.DrawRoundRect(x, y, w, h, 6, 6, NodeFillPaint);

        var strokeColor = node == vp.SelectedNode ? ColorNodeStrokeSelected
                       : node == vp.HoveredNode ? ColorNodeStrokeHover
                       : ColorNodeStroke;
        float strokeW = node == vp.SelectedNode ? 3f : 1.5f;
        NodeStrokePaint.Color = strokeColor;
        NodeStrokePaint.StrokeWidth = strokeW;
        canvas.DrawRoundRect(x, y, w, h, 6, 6, NodeStrokePaint);

        NodeHeaderPaint.Color = strokeColor.WithAlpha(40);
        canvas.DrawRoundRect(x, y, w, NodeHeaderHeight, 6, 6, NodeHeaderPaint);
        canvas.DrawRect(x, y + NodeHeaderHeight - 4, w, 4, NodeHeaderPaint);

        canvas.DrawText(node.DisplayName, x + NodePaddingX, y + NodeHeaderHeight - 8, NodeNamePaint);
    }

    private static void DrawArrowhead(SKCanvas canvas, float x, float y, SKColor color)
    {
        float arrowLen = ArrowheadLength;
        float arrowWidth = ArrowheadHalfWidth;

        ArrowheadPaint.Color = color;
        var path = new SKPath();
        path.MoveTo(x, y);
        path.LineTo(x - arrowLen, y - arrowWidth);
        path.LineTo(x - arrowLen, y + arrowWidth);
        path.Close();
        canvas.DrawPath(path, ArrowheadPaint);
    }

    internal static string? GetBadgeText(GraphNode node)
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

    internal static SKColor GetBadgeColor(GraphNode node)
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
}
