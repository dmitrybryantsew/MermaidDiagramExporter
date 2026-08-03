using System;
using System.Collections.Generic;
using System.Linq;
using SkiaSharp;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Settings;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Theming;

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
    public float ViewportWidth { get; init; }
    public float ViewportHeight { get; init; }
    public bool ShowInheritanceEdges { get; init; }
    public bool ShowImplementsEdges { get; init; }
    public bool ShowAssociationEdges { get; init; }
    public GraphNode? SelectedNode { get; init; }
    public GraphNode? HoveredNode { get; init; }
    public HashSet<string>? SelectedDesignNodeIds { get; init; }
    public HashSet<string>? HoveredDesignNodeIds { get; init; }
    /// <summary>
    /// Analyze Mode multi-select set. When non-null and non-empty, the
    /// renderer highlights every node in this set (in addition to the single
    /// <see cref="SelectedNode"/> primary). Null in Design Mode.
    /// </summary>
    public HashSet<string>? SelectedAnalyzeNodeIds { get; init; }
    public string SearchText { get; init; } = string.Empty;
    /// <summary>Set to true to enable Design Mode visual affordances (handles, ports).</summary>
    public bool IsDesignMode { get; init; }
    /// <summary>
    /// Per-kind edge visual style (color + arrowhead). Null = use built-in UML defaults.
    /// </summary>
    public EdgeStyleSettings? EdgeStyles { get; init; }

    /// <summary>
    /// When true (Analyze Mode only), inter-namespace edges collapse into one
    /// thick labeled "highway" per namespace pair. Edges connected to the
    /// selected node are still drawn individually (expand-on-select).
    /// </summary>
    public bool AggregateHighways { get; init; }
}

/// <summary>
/// Owns all SkiaSharp rendering logic previously inline in GraphCanvas.
/// Extracted in Step 17 to separate rendering from input handling and control lifecycle.
/// </summary>
public sealed class CanvasRenderer
{
    // ── Cached SKPaint objects (reused across frames to reduce GC pressure) ──
    // Initialized from RenderPalette.Dark; re-colored on theme change via
    // <see cref="ReloadPaletteColors"/>.
    private static readonly SKPaint NamespaceBgPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint NamespaceBorderPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
    private static readonly SKPaint NamespaceTextPaint = new() { IsAntialias = true, TextSize = 13 };
    private static readonly SKPaint EdgeLabelPaint = new() { IsAntialias = true, TextSize = 9 };
    private static readonly SKPaint NodeFillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint BadgeTextPaint = new() { Color = SKColors.White, IsAntialias = true, TextSize = 9 };
    private static readonly SKPaint StereotypeBadgeTextPaint = new() { Color = SKColors.White, IsAntialias = true, TextSize = 8 };
    private static readonly SKPaint NodeNamePaint = new() { IsAntialias = true, TextSize = 12 };
    private static readonly SKPaint NodeMemberPaint = new() { IsAntialias = true, TextSize = 10 };
    // Mutable paints for state-dependent rendering (reused, properties updated per-frame)
    private static readonly SKPaint EdgeStrokePaint = new() { Style = SKPaintStyle.Stroke, IsAntialias = true, StrokeCap = SKStrokeCap.Round };
    private static readonly SKPaint ArrowheadPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint NodeStrokePaint = new() { Style = SKPaintStyle.Stroke, IsAntialias = true };
    private static readonly SKPaint NodeHeaderPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint BadgeFillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint StereotypeBadgeFillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };

    // Design Mode affordance paints
    private static readonly SKPaint SelectionBorderPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };
    private static readonly SKPaint HoverBorderPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
    private static readonly SKPaint ResizeHandlePaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint PortCircleFillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint PortCircleStrokePaint = new() { Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true };
    private static readonly SKPaint RubberBandPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 2f, IsAntialias = true, PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0) };
    private static readonly SKPaint MarqueeFillPaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint EdgeTargetHighlightPaint = new() { Style = SKPaintStyle.Stroke, StrokeWidth = 3f, IsAntialias = true };

    private const float ResizeHandleSize = 10f;
    private const float PortCircleRadius = 5f;
    private const float PortHitRadius = 6f;
    private static readonly SKColor ColorEdgeInheritance = new(0x50, 0x90, 0xD0);
    private static readonly SKColor ColorEdgeImplements = new(0x40, 0xB0, 0x70);
    private static readonly SKColor ColorEdgeAssociation = new(0x60, 0x60, 0x60);
    private static readonly SKColor ColorEdgeHighway = new(0x8A, 0x92, 0xA8);
    private static readonly SKPaint HighwayBadgePaint = new() { Style = SKPaintStyle.Fill, IsAntialias = true };
    private static readonly SKPaint HighwayLabelPaint = new() { Color = SKColors.White, IsAntialias = true, TextSize = 10 };
    private static SKColor ColorNodeStroke => RenderPalette.Current.NodeStroke;
    private static SKColor ColorNodeStrokeSelected => RenderPalette.Current.Selection;
    private static SKColor ColorNodeStrokeHover => RenderPalette.Current.Hover;
    private static SKColor ColorNodeStrokeSearchMatch => RenderPalette.Current.SearchMatch;
    private static readonly SKColor ColorBadgeInterface = new(0x40, 0x80, 0xC0);
    private static readonly SKColor ColorBadgeEnum = new(0xC0, 0x80, 0x30);
    private static readonly SKColor ColorBadgeStruct = new(0x80, 0x50, 0xC0);
    private static readonly SKColor ColorBadgeStatic = new(0xC0, 0x50, 0x50);
    private static readonly SKColor ColorBadgeAbstract = new(0x30, 0x80, 0x80);

    static CanvasRenderer()
    {
        // Initialize paint colors from the active palette on first use.
        // Subsequent theme changes call ReloadPaletteColors() to refresh.
        ReloadPaletteColors();
    }

    /// <summary>
    /// Re-applies <see cref="RenderPalette.Current"/> colors to the cached
    /// SKPaint objects. Called from <see cref="ThemeService.ThemeChanged"/>
    /// so the next rendered frame uses the new palette.
    /// </summary>
    public static void ReloadPaletteColors()
    {
        var p = RenderPalette.Current;
        NamespaceBgPaint.Color = p.ClusterFill;
        NamespaceBorderPaint.Color = p.ClusterStroke;
        NamespaceTextPaint.Color = p.ClusterLabel;
        EdgeLabelPaint.Color = p.EdgeLabel;
        NodeFillPaint.Color = p.NodeFill;
        NodeNamePaint.Color = p.NodeTitleText;
        NodeMemberPaint.Color = p.NodeMemberText;

        SelectionBorderPaint.Color = p.Selection;
        HoverBorderPaint.Color = p.Hover;
        ResizeHandlePaint.Color = p.Selection;
        PortCircleFillPaint.Color = p.Selection;
        RubberBandPaint.Color = p.RubberBand;
        MarqueeFillPaint.Color = p.Marquee;
        EdgeTargetHighlightPaint.Color = p.DropTarget;
    }

    private const float NodePaddingX = 12;
    private const float NodeHeaderHeight = 28;
    private const float NodeMemberHeight = 16;
    private const float NamespacePadding = 24;
    private const float NamespaceTitleHeight = 24;
    private const int MaxMembersShownPerNode = 6;
    private const float ArrowheadLength = 10f;
    private const float ArrowheadHalfWidth = 5f;

    /// <summary>
    /// Computes the background rectangle per namespace (bbox of member nodes +
    /// namespace padding + title strip). Shared by <see cref="DrawNamespaceGroups"/>
    /// (backgrounds) and the highway edge drawing (anchor points), so highway
    /// endpoints land exactly on the drawn zone borders.
    /// </summary>
    private static Dictionary<string, Rect> ComputeNamespaceRects(List<GraphNode> nodes)
    {
        var groups = new Dictionary<string, List<GraphNode>>();
        foreach (var node in nodes)
        {
            if (!groups.ContainsKey(node.Namespace))
                groups[node.Namespace] = new();
            groups[node.Namespace].Add(node);
        }

        var rects = new Dictionary<string, Rect>();
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

            rects[ns] = new Rect(
                minX - NamespacePadding,
                minY - NamespacePadding - NamespaceTitleHeight,
                (maxX - minX) + NamespacePadding * 2,
                (maxY - minY) + NamespacePadding * 2 + NamespaceTitleHeight);
        }

        return rects;
    }

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

        var rects = ComputeNamespaceRects(nodes);

        foreach (var (ns, nsNodes) in groups)
        {
            if (string.IsNullOrEmpty(ns) || nsNodes.Count == 0) continue;

            var r = rects[ns];
            canvas.DrawRoundRect(r.X, r.Y, r.Width, r.Height, 8, 8, NamespaceBgPaint);
            canvas.DrawRoundRect(r.X, r.Y, r.Width, r.Height, 8, 8, NamespaceBorderPaint);
            canvas.DrawText(ns, r.X + 12, r.Y + NamespaceTitleHeight - 6, NamespaceTextPaint);
        }
    }

    /// <summary>
    /// Draws all edges, optionally excluding edges connected to a specific node (for drag optimization).
    /// Uses routed Points when available (from the layout engine); otherwise computes
    /// closest-perimeter ports with overlap spreading via <see cref="EdgePortAssigner"/>.
    /// When <see cref="ViewportState.AggregateHighways"/> is on (Analyze Mode),
    /// inter-namespace edges collapse into one thick labeled highway per
    /// namespace pair; edges of the selected node stay expanded.
    /// </summary>
    public void DrawEdges(SKCanvas canvas, List<GraphNode> nodes, List<GraphEdge> edges, ViewportState vp, string? excludeNodeId = null)
    {
        // Pre-compute ports for edges without routed points (spreads overlaps).
        var portMap = EdgePortAssigner.AssignPorts(edges);

        var visibleEdges = new List<GraphEdge>(edges.Count);
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
            if (visible) visibleEdges.Add(edge);
        }

        if (!vp.AggregateHighways || vp.IsDesignMode)
        {
            foreach (var edge in visibleEdges)
            {
                var style = ResolveEdgeStyle(edge.Kind, vp);
                DrawSingleEdgePath(canvas, edge, portMap, style, isSelected: IsEdgeHighlighted(edge, vp));
            }
            return;
        }

        // ── Highway mode: intra-zone edges individually, inter-zone edges as
        // one thick labeled highway per namespace pair ──
        var (intraZone, highways) = HighwayGrouper.Group(visibleEdges);
        foreach (var edge in intraZone)
        {
            var style = ResolveEdgeStyle(edge.Kind, vp);
            DrawSingleEdgePath(canvas, edge, portMap, style, isSelected: IsEdgeHighlighted(edge, vp));
        }

        var zoneRects = ComputeNamespaceRects(nodes);
        foreach (var highway in highways)
        {
            if (!zoneRects.TryGetValue(highway.ZoneA, out var rectA)) continue;
            if (!zoneRects.TryGetValue(highway.ZoneB, out var rectB)) continue;
            DrawHighway(canvas, highway, rectA, rectB);
        }

        // Expand-on-select: the selected node's inter-zone edges are hidden
        // inside highways — draw them individually (highlighted) on top.
        if (vp.SelectedNode != null)
        {
            foreach (var edge in visibleEdges)
            {
                if (!HighwayGrouper.IsInterZone(edge)) continue;
                if (edge.FromNode!.Id != vp.SelectedNode.Id && edge.ToNode!.Id != vp.SelectedNode.Id) continue;
                var style = ResolveEdgeStyle(edge.Kind, vp);
                DrawSingleEdgePath(canvas, edge, portMap, style, isSelected: true);
            }
        }
    }

    /// <summary>
    /// Draws one aggregated highway: a thick line between the two zone border
    /// points, arrowheads for each direction that has flow, and a count badge
    /// at the midpoint.
    /// </summary>
    private static void DrawHighway(SKCanvas canvas, HighwayGroup highway, Rect rectA, Rect rectB)
    {
        var from = HighwayGrouper.BorderPointToward(rectA, rectB.center);
        var to = HighwayGrouper.BorderPointToward(rectB, rectA.center);
        var dir = to - from;
        if (dir.sqrMagnitude < 0.001f) return;

        EdgeStrokePaint.PathEffect = null;
        EdgeStrokePaint.Color = ColorEdgeHighway;
        EdgeStrokePaint.StrokeWidth = HighwayGrouper.ComputeStrokeWidth(highway.TotalCount);

        using var path = new SKPath();
        path.MoveTo(from.X, from.Y);
        path.LineTo(to.X, to.Y);
        canvas.DrawPath(path, EdgeStrokePaint);

        // Arrowheads per flow direction (ZoneA→ZoneB and/or ZoneB→ZoneA).
        if (highway.ForwardCount > 0)
            DrawArrowhead(canvas, to.X, to.Y, dir, ColorEdgeHighway, EdgeArrowheadStyle.SolidArrow);
        if (highway.BackwardCount > 0)
            DrawArrowhead(canvas, from.X, from.Y, from - to, ColorEdgeHighway, EdgeArrowheadStyle.SolidArrow);

        // Count badge at the midpoint.
        float midX = (from.X + to.X) * 0.5f;
        float midY = (from.Y + to.Y) * 0.5f;
        string label = highway.TotalCount.ToString();
        float badgeW = HighwayLabelPaint.MeasureText(label) + 12;
        float badgeH = 16;
        HighwayBadgePaint.Color = ColorEdgeHighway;
        canvas.DrawRoundRect(midX - badgeW * 0.5f, midY - badgeH * 0.5f, badgeW, badgeH, 4, 4, HighwayBadgePaint);
        canvas.DrawText(label, midX - badgeW * 0.5f + 6, midY + badgeH * 0.5f - 4, HighwayLabelPaint);
    }

    private static bool IsEdgeHighlighted(GraphEdge edge, ViewportState vp)
    {
        if (vp.IsDesignMode)
        {
            var sel = vp.SelectedDesignNodeIds;
            if (sel != null && (sel.Contains(edge.FromNode!.Id) || sel.Contains(edge.ToNode!.Id)))
                return true;
            return false;
        }
        return vp.SelectedNode != null
            && (edge.FromNode!.Id == vp.SelectedNode.Id || edge.ToNode!.Id == vp.SelectedNode.Id);
    }

    private static (SKColor color, float strokeWidth, EdgeArrowheadStyle arrow) ResolveEdgeStyle(TypeEdgeKind kind, ViewportState vp)
    {
        var s = vp.EdgeStyles?.ForKind(kind);
        var color = TryParseColor(s?.ColorHex) ?? DefaultColorForKind(kind);
        var arrow = s?.Arrowhead ?? DefaultArrowForKind(kind);
        float strokeWidth = kind == TypeEdgeKind.Association ? 1.2f : 2.0f;
        return (color, strokeWidth, arrow);
    }

    private static SKColor DefaultColorForKind(TypeEdgeKind kind) => kind switch
    {
        TypeEdgeKind.Inheritance => ColorEdgeInheritance,
        TypeEdgeKind.Implements => ColorEdgeImplements,
        _ => ColorEdgeAssociation,
    };

    private static EdgeArrowheadStyle DefaultArrowForKind(TypeEdgeKind kind) => kind switch
    {
        TypeEdgeKind.Inheritance => EdgeArrowheadStyle.HollowTriangle,
        TypeEdgeKind.Implements => EdgeArrowheadStyle.HollowTriangle,
        _ => EdgeArrowheadStyle.SolidArrow,
    };

    private static SKColor? TryParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        if (hex.StartsWith("#") && hex.Length == 7
            && SKColor.TryParse(hex, out var c))
            return c;
        return null;
    }

    /// <summary>
    /// Draws a single edge using routed Points (polyline) when available, or
    /// assigned port points (smooth bezier) otherwise. Renders the appropriate
    /// arrowhead shape oriented along the final segment.
    /// </summary>
    private void DrawSingleEdgePath(
        SKCanvas canvas,
        GraphEdge edge,
        Dictionary<GraphEdge, (Vector2 from, Vector2 to)> portMap,
        (SKColor color, float strokeWidth, EdgeArrowheadStyle arrow) style,
        bool isSelected)
    {
        float strokeWidth = style.strokeWidth + (isSelected ? 0.5f : 0f);
        EdgeStrokePaint.Color = style.color;
        EdgeStrokePaint.StrokeWidth = strokeWidth;
        // Dashed for Implements (UML realization convention)
        if (edge.Kind == TypeEdgeKind.Implements)
            EdgeStrokePaint.PathEffect = SKPathEffect.CreateDash(new[] { 6f, 4f }, 0);
        else
            EdgeStrokePaint.PathEffect = null;

        Vector2 from, to, arrowDir;

        if (edge.Points.Count >= 2)
        {
            // Routed polyline — draw straight segments through all points.
            var pts = edge.Points;
            from = pts[0];
            to = pts[pts.Count - 1];
            arrowDir = pts.Count >= 2 ? (to - pts[pts.Count - 2]) : (to - from);

            using var path = new SKPath();
            path.MoveTo(from.X, from.Y);
            for (int i = 1; i < pts.Count; i++)
                path.LineTo(pts[i].X, pts[i].Y);
            canvas.DrawPath(path, EdgeStrokePaint);

            if (!string.IsNullOrEmpty(edge.Label) && pts.Count >= 2)
            {
                int mid = pts.Count / 2;
                var a = pts[mid - 1];
                var b = pts[mid];
                float mx = (a.X + b.X) * 0.5f;
                float my = (a.Y + b.Y) * 0.5f;
                canvas.DrawText(edge.Label, mx, my - 4, EdgeLabelPaint);
            }
        }
        else
        {
            // No routed points — use assigned ports, draw a smooth bezier.
            if (!portMap.TryGetValue(edge, out var ports))
                return;
            from = ports.from;
            to = ports.to;
            arrowDir = to - from;

            float dx = to.X - from.X;
            float dy = to.Y - from.Y;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy);
            if (dist < 0.001f) return;
            float controlOffset = Math.Max(20, Math.Min(80, dist * 0.35f));
            var nx = dx / dist;
            var ny = dy / dist;

            using var path = new SKPath();
            path.MoveTo(from.X, from.Y);
            path.CubicTo(
                from.X + nx * controlOffset, from.Y + ny * controlOffset,
                to.X - nx * controlOffset, to.Y - ny * controlOffset,
                to.X, to.Y);
            canvas.DrawPath(path, EdgeStrokePaint);

            if (!string.IsNullOrEmpty(edge.Label))
            {
                float midX = (from.X + to.X) * 0.5f;
                float midY = (from.Y + to.Y) * 0.5f;
                canvas.DrawText(edge.Label, midX, midY - 4, EdgeLabelPaint);
            }
        }

        // Reset path effect so it doesn't leak into the arrowhead fill
        EdgeStrokePaint.PathEffect = null;
        DrawArrowhead(canvas, to.X, to.Y, arrowDir, style.color, style.arrow);
    }

    /// <summary>
    /// Draws all nodes with search highlighting, selection, and hover states.
    /// Supports both Analyze Mode (single SelectedNode) and Design Mode
    /// (SelectedDesignNodeIds set for multi-select).
    /// </summary>
    public void DrawNodes(SKCanvas canvas, List<GraphNode> nodes, ViewportState vp, string? excludeNodeId = null)
    {
        bool searchActive = !string.IsNullOrWhiteSpace(vp.SearchText);
        bool designMode = vp.IsDesignMode;
        var designSelectedIds = vp.SelectedDesignNodeIds;
        bool hasDesignSelection = designSelectedIds != null && designSelectedIds.Count > 0;
        var analyzeSelectedIds = vp.SelectedAnalyzeNodeIds;
        bool hasAnalyzeSelection = analyzeSelectedIds != null && analyzeSelectedIds.Count > 0;

        // View frustum culling: calculate the visible rectangle in world coordinates
        float visibleWorldX = -vp.PanX / vp.Zoom;
        float visibleWorldY = -vp.PanY / vp.Zoom;
        float visibleWorldW = vp.ViewportWidth / vp.Zoom;
        float visibleWorldH = vp.ViewportHeight / vp.Zoom;

        // Add some padding to ensure nodes slightly off-screen but casting shadows or overlapping are drawn
        float cullPad = 100f;
        var visibleWorldRect = SKRect.Create(
            visibleWorldX - cullPad,
            visibleWorldY - cullPad,
            visibleWorldW + (cullPad * 2),
            visibleWorldH + (cullPad * 2)
        );

        var nodesToRender = new List<GraphNode>();

        // Pass 1: Culling and filtering
        foreach (var node in nodes)
        {
            if (excludeNodeId != null && node.Id == excludeNodeId)
                continue;

            var nodeRect = SKRect.Create(node.X, node.Y, node.Width, node.Height);
            if (visibleWorldRect.IntersectsWith(nodeRect))
            {
                nodesToRender.Add(node);
            }
        }

        // Pass 2: Draw all backgrounds
        foreach (var node in nodesToRender)
        {
            canvas.DrawRoundRect(node.X, node.Y, node.Width, node.Height, 6, 6, NodeFillPaint);
        }

        // Pass 3: Draw all borders and headers
        foreach (var node in nodesToRender)
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

            bool isSelected = designMode && hasDesignSelection
                ? designSelectedIds!.Contains(node.Id)
                : hasAnalyzeSelection
                    ? analyzeSelectedIds!.Contains(node.Id)
                    : node == vp.SelectedNode;

            bool isHovered = designMode && vp.HoveredDesignNodeIds != null
                ? vp.HoveredDesignNodeIds.Contains(node.Id)
                : node == vp.HoveredNode;

            var strokeColor = searchActive && searchMatch ? ColorNodeStrokeSearchMatch
                           : isSelected ? ColorNodeStrokeSelected
                           : isHovered ? ColorNodeStrokeHover
                           : ColorNodeStroke;

            float strokeWidth = (isSelected || (searchActive && searchMatch)) ? 3 : 1.5f;

            // Draw border
            NodeStrokePaint.Color = strokeColor;
            NodeStrokePaint.StrokeWidth = strokeWidth;
            canvas.DrawRoundRect(x, y, w, h, 6, 6, NodeStrokePaint);

            // Draw header
            NodeHeaderPaint.Color = strokeColor.WithAlpha(40);
            canvas.DrawRoundRect(x, y, w, NodeHeaderHeight, 6, 6, NodeHeaderPaint);
            canvas.DrawRect(x, y + NodeHeaderHeight - 4, w, 4, NodeHeaderPaint);
        }

        // Pass 4: Draw all badges
        foreach (var node in nodesToRender)
        {
            float x = node.X, y = node.Y, w = node.Width, h = node.Height;
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

            if (vp.Zoom >= 0.2f && node.StereotypeBadges.Count > 0)
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
        }

        // Pass 5: Draw text and members
        if (vp.Zoom >= 0.2f)
        {
            foreach (var node in nodesToRender)
            {
                float x = node.X, y = node.Y;
                canvas.DrawText(node.DisplayName, x + NodePaddingX, y + NodeHeaderHeight - 8, NodeNamePaint);

                if (vp.Zoom >= 0.4f)
                {
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
        }

        // Pass 6: Draw design mode affordances
        if (designMode)
        {
            foreach (var node in nodesToRender)
            {
                bool isSelected = hasDesignSelection ? designSelectedIds!.Contains(node.Id) : node == vp.SelectedNode;
                bool isHovered = vp.HoveredDesignNodeIds != null ? vp.HoveredDesignNodeIds.Contains(node.Id) : node == vp.HoveredNode;

                if (isSelected)
                {
                    DrawResizeHandle(canvas, node.X, node.Y, node.Width, node.Height);
                    DrawConnectionPorts(canvas, node.X, node.Y, node.Width, node.Height);
                }
                else if (isHovered)
                {
                    DrawConnectionPorts(canvas, node.X, node.Y, node.Width, node.Height);
                }
            }
        }
    }

    /// <summary>
    /// Draws a single node (and its connected edges) during drag operations.
    /// Reuses <see cref="EdgePortAssigner"/> so drag-time edges also use
    /// closest-perimeter ports with overlap spreading.
    /// </summary>
    public void DrawSingleNode(SKCanvas canvas, List<GraphEdge> edges, GraphNode node, ViewportState vp)
    {
        float x = node.X, y = node.Y, w = node.Width, h = node.Height;

        // Draw connected edges first — only those touching this node.
        var connectedEdges = edges
            .Where(e => e.FromNode != null && e.ToNode != null
                && (e.FromNode.Id == node.Id || e.ToNode.Id == node.Id))
            .ToList();
        var portMap = EdgePortAssigner.AssignPorts(connectedEdges);

        foreach (var edge in connectedEdges)
        {
            bool visible = edge.Kind switch
            {
                TypeEdgeKind.Inheritance => vp.ShowInheritanceEdges,
                TypeEdgeKind.Implements => vp.ShowImplementsEdges,
                TypeEdgeKind.Association => vp.ShowAssociationEdges,
                _ => true
            };
            if (!visible) continue;

            var style = ResolveEdgeStyle(edge.Kind, vp);
            DrawSingleEdgePath(canvas, edge, portMap, style, isSelected: IsEdgeHighlighted(edge, vp));
        }

        // Draw the node itself (with Design Mode selection support)
        canvas.DrawRoundRect(x, y, w, h, 6, 6, NodeFillPaint);

        bool designMode = vp.IsDesignMode;
        var designSelectedIds = vp.SelectedDesignNodeIds;
        var analyzeSelectedIds = vp.SelectedAnalyzeNodeIds;
        bool isSelected = designMode && designSelectedIds != null
            ? designSelectedIds.Contains(node.Id)
            : analyzeSelectedIds != null && analyzeSelectedIds.Count > 0
                ? analyzeSelectedIds.Contains(node.Id)
                : node == vp.SelectedNode;
        bool isHovered = designMode && vp.HoveredDesignNodeIds != null
            ? vp.HoveredDesignNodeIds.Contains(node.Id)
            : node == vp.HoveredNode;

        var strokeColor = isSelected ? ColorNodeStrokeSelected
                       : isHovered ? ColorNodeStrokeHover
                       : ColorNodeStroke;
        float strokeW = isSelected ? 3f : 1.5f;
        NodeStrokePaint.Color = strokeColor;
        NodeStrokePaint.StrokeWidth = strokeW;
        canvas.DrawRoundRect(x, y, w, h, 6, 6, NodeStrokePaint);

        NodeHeaderPaint.Color = strokeColor.WithAlpha(40);
        canvas.DrawRoundRect(x, y, w, NodeHeaderHeight, 6, 6, NodeHeaderPaint);
        canvas.DrawRect(x, y + NodeHeaderHeight - 4, w, 4, NodeHeaderPaint);

        canvas.DrawText(node.DisplayName, x + NodePaddingX, y + NodeHeaderHeight - 8, NodeNamePaint);

        // Design Mode affordances for the dragged/selected node
        if (designMode && isSelected)
        {
            DrawResizeHandle(canvas, x, y, w, h);
            DrawConnectionPorts(canvas, x, y, w, h);
        }
        else if (designMode && isHovered)
        {
            DrawConnectionPorts(canvas, x, y, w, h);
        }
    }

    /// <summary>
    /// Draws an arrowhead of the given style at <paramref name="x"/>,<paramref name="y"/>,
    /// oriented along <paramref name="direction"/> (the incoming edge tangent).
    /// </summary>
    private static void DrawArrowhead(SKCanvas canvas, float x, float y, Vector2 direction, SKColor color, EdgeArrowheadStyle style)
    {
        if (style == EdgeArrowheadStyle.None) return;

        float len = direction.sqrMagnitude;
        if (len < 0.0001f) return;
        len = (float)Math.Sqrt(len);
        float nx = direction.X / len; // unit direction (pointing INTO the target)
        float ny = direction.Y / len;

        // Perpendicular (for width)
        float px = -ny;
        float py = nx;

        float aLen = ArrowheadLength;
        float aHalf = ArrowheadHalfWidth;

        // Tip is at (x, y). Base is aLen back along the incoming direction.
        float baseX = x - nx * aLen;
        float baseY = y - ny * aLen;
        // Width corners: base ± perpendicular * aHalf
        float w1X = baseX + px * aHalf;
        float w1Y = baseY + py * aHalf;
        float w2X = baseX - px * aHalf;
        float w2Y = baseY - py * aHalf;

        var path = new SKPath();
        path.MoveTo(x, y);
        path.LineTo(w1X, w1Y);
        path.LineTo(w2X, w2Y);
        path.Close();

        switch (style)
        {
            case EdgeArrowheadStyle.HollowTriangle:
                ArrowheadPaint.Color = color;
                ArrowheadPaint.Style = SKPaintStyle.Stroke;
                ArrowheadPaint.StrokeWidth = 1.5f;
                canvas.DrawPath(path, ArrowheadPaint);
                // Reset for next use
                ArrowheadPaint.Style = SKPaintStyle.Fill;
                break;
            case EdgeArrowheadStyle.HollowDiamond:
                // Diamond: tip + base + two side points twice as wide
                float dHalf = aHalf * 1.6f;
                float midX = baseX + nx * aLen * 0.5f;
                float midY = baseY + ny * aLen * 0.5f;
                float s1X = midX + px * dHalf;
                float s1Y = midY + py * dHalf;
                float s2X = midX - px * dHalf;
                float s2Y = midY - py * dHalf;
                float tailX = baseX - nx * aLen * 0.4f;
                float tailY = baseY - ny * aLen * 0.4f;
                var diamond = new SKPath();
                diamond.MoveTo(x, y);
                diamond.LineTo(s1X, s1Y);
                diamond.LineTo(tailX, tailY);
                diamond.LineTo(s2X, s2Y);
                diamond.Close();
                ArrowheadPaint.Color = color;
                ArrowheadPaint.Style = SKPaintStyle.Stroke;
                ArrowheadPaint.StrokeWidth = 1.5f;
                canvas.DrawPath(diamond, ArrowheadPaint);
                ArrowheadPaint.Style = SKPaintStyle.Fill;
                break;
            case EdgeArrowheadStyle.FilledDiamond:
                dHalf = aHalf * 1.6f;
                midX = baseX + nx * aLen * 0.5f;
                midY = baseY + ny * aLen * 0.5f;
                s1X = midX + px * dHalf;
                s1Y = midY + py * dHalf;
                s2X = midX - px * dHalf;
                s2Y = midY - py * dHalf;
                tailX = baseX - nx * aLen * 0.4f;
                tailY = baseY - ny * aLen * 0.4f;
                var filledDiamond = new SKPath();
                filledDiamond.MoveTo(x, y);
                filledDiamond.LineTo(s1X, s1Y);
                filledDiamond.LineTo(tailX, tailY);
                filledDiamond.LineTo(s2X, s2Y);
                filledDiamond.Close();
                ArrowheadPaint.Color = color;
                ArrowheadPaint.Style = SKPaintStyle.Fill;
                canvas.DrawPath(filledDiamond, ArrowheadPaint);
                break;
            default: // SolidArrow
                ArrowheadPaint.Color = color;
                ArrowheadPaint.Style = SKPaintStyle.Fill;
                canvas.DrawPath(path, ArrowheadPaint);
                break;
        }
    }

    /// <summary>
    /// Draws a resize handle (small square) at the bottom-right corner of a node.
    /// Used in Design Mode to show that the node can be resized.
    /// </summary>
    private static void DrawResizeHandle(SKCanvas canvas, float nodeX, float nodeY, float nodeW, float nodeH)
    {
        float hx = nodeX + nodeW - ResizeHandleSize;
        float hy = nodeY + nodeH - ResizeHandleSize;
        canvas.DrawRect(hx, hy, ResizeHandleSize, ResizeHandleSize, ResizeHandlePaint);
        // White border for visibility
        canvas.DrawRect(hx, hy, ResizeHandleSize, ResizeHandleSize, new SKPaint
        {
            Color = SKColors.White, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true
        });
    }

    /// <summary>
    /// Draws connection ports (circles) on the left and right edges of a node.
    /// Used in Design Mode when a node is selected or hovered.
    /// </summary>
    private static void DrawConnectionPorts(SKCanvas canvas, float nodeX, float nodeY, float nodeW, float nodeH)
    {
        float midY = nodeY + nodeH / 2f;
        // Left port
        canvas.DrawCircle(nodeX, midY, PortCircleRadius, PortCircleFillPaint);
        canvas.DrawCircle(nodeX, midY, PortCircleRadius, PortCircleStrokePaint);
        // Right port
        canvas.DrawCircle(nodeX + nodeW, midY, PortCircleRadius, PortCircleFillPaint);
        canvas.DrawCircle(nodeX + nodeW, midY, PortCircleRadius, PortCircleStrokePaint);
    }

    /// <summary>
    /// Draws a rubber-band line from a source node's port to the current cursor
    /// position during edge creation in Design Mode.
    /// </summary>
    public static void DrawEdgeCreationPreview(SKCanvas canvas, float fromX, float fromY, SKPoint toWorld, bool fromRightPort)
    {
        float srcX = fromRightPort ? fromX : fromX;
        float srcY = fromY;
        float dstX = toWorld.X;
        float dstY = toWorld.Y;
        canvas.DrawLine(srcX, srcY, dstX, dstY, RubberBandPaint);
    }

    /// <summary>
    /// Draws a green border around a node to highlight it as a valid edge
    /// creation target during drag-from-port.
    /// </summary>
    public static void DrawEdgeTargetHighlight(SKCanvas canvas, float nodeX, float nodeY, float nodeW, float nodeH)
    {
        canvas.DrawRoundRect(nodeX, nodeY, nodeW, nodeH, 6, 6, EdgeTargetHighlightPaint);
    }

    /// <summary>
    /// Draws the rubber-band marquee selection rectangle (world-space) plus a
    /// translucent fill, so the user can see which classes will be selected on
    /// release. Used by both Analyze and Design Mode marquee selection.
    /// </summary>
    public static void DrawMarquee(SKCanvas canvas, float x0, float y0, float x1, float y1)
    {
        float left = Math.Min(x0, x1);
        float top = Math.Min(y0, y1);
        float right = Math.Max(x0, x1);
        float bottom = Math.Max(y0, y1);
        var rect = new SKRect(left, top, right, bottom);
        canvas.DrawRect(rect, MarqueeFillPaint);
        canvas.DrawRect(rect, RubberBandPaint);
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
