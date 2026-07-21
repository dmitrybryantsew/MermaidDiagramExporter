using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Gui.Layout;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// One aggregated "highway" between two namespaces: replaces N individual
/// inter-zone edges with a single thick line (research doc §6.4). Direction
/// counts are kept so the renderer can draw arrowheads per flow direction.
/// </summary>
public sealed class HighwayGroup
{
    /// <summary>Namespace pair endpoints, ordinal-sorted (ZoneA &lt; ZoneB).</summary>
    public string ZoneA { get; set; } = "";
    public string ZoneB { get; set; } = "";

    /// <summary>Number of edges flowing ZoneA → ZoneB.</summary>
    public int ForwardCount { get; set; }

    /// <summary>Number of edges flowing ZoneB → ZoneA.</summary>
    public int BackwardCount { get; set; }

    public int TotalCount => ForwardCount + BackwardCount;

    /// <summary>The individual edges inside this highway (kept for expansion).</summary>
    public List<GraphEdge> Edges { get; } = new();
}

/// <summary>
/// Pure grouping + geometry logic for aggregate highway edges. Kept free of
/// SkiaSharp so it is unit-testable; the renderer (<see cref="CanvasRenderer"/>)
/// consumes the groups.
/// </summary>
public static class HighwayGrouper
{
    /// <summary>
    /// Partitions edges into intra-zone (same namespace on both ends, or an
    /// endpoint without a namespace — drawn individually) and inter-zone
    /// (grouped into one <see cref="HighwayGroup"/> per namespace pair).
    /// </summary>
    public static (List<GraphEdge> IntraZone, List<HighwayGroup> Highways) Group(
        IReadOnlyList<GraphEdge> edges)
    {
        var intraZone = new List<GraphEdge>();
        var byPair = new Dictionary<(string, string), HighwayGroup>();

        foreach (var edge in edges)
        {
            string za = edge.FromNode?.Namespace ?? "";
            string zb = edge.ToNode?.Namespace ?? "";
            if (za.Length == 0 || zb.Length == 0 || za == zb)
            {
                intraZone.Add(edge);
                continue;
            }

            bool forward = string.CompareOrdinal(za, zb) < 0;
            var key = forward ? (za, zb) : (zb, za);
            if (!byPair.TryGetValue(key, out var group))
            {
                group = new HighwayGroup { ZoneA = key.Item1, ZoneB = key.Item2 };
                byPair[key] = group;
            }

            if (forward) group.ForwardCount++;
            else group.BackwardCount++;
            group.Edges.Add(edge);
        }

        var highways = byPair.Values
            .OrderBy(g => g.ZoneA, StringComparer.Ordinal)
            .ThenBy(g => g.ZoneB, StringComparer.Ordinal)
            .ToList();
        return (intraZone, highways);
    }

    /// <summary>
    /// True when the edge crosses a namespace boundary (both endpoints have
    /// distinct non-empty namespaces) — the highway-grouping criterion.
    /// </summary>
    public static bool IsInterZone(GraphEdge edge)
    {
        string za = edge.FromNode?.Namespace ?? "";
        string zb = edge.ToNode?.Namespace ?? "";
        return za.Length > 0 && zb.Length > 0 && za != zb;
    }

    /// <summary>
    /// Highway stroke width: grows with the log of the edge count so a 23-edge
    /// highway reads much heavier than a 2-edge one, capped so it stays a line
    /// (not a band). count 1→2px, 2→3.5, 4→5, 8→6.5, 23→~8.8, max 10.
    /// </summary>
    public static float ComputeStrokeWidth(int count)
    {
        return Math.Min(2f + (float)Math.Log(Math.Max(count, 1), 2) * 1.5f, 10f);
    }

    /// <summary>
    /// The point where the segment from <paramref name="rect"/>'s center toward
    /// <paramref name="toward"/> exits the rectangle. Used to anchor highways
    /// at zone borders. Returns the center when <paramref name="toward"/> is
    /// inside the rect (overlapping zones — draw center-to-center).
    /// </summary>
    public static Vector2 BorderPointToward(Rect rect, Vector2 toward)
    {
        var center = rect.center;
        float dx = toward.X - center.X;
        float dy = toward.Y - center.Y;
        if (dx == 0 && dy == 0) return center;

        float halfW = rect.Width * 0.5f;
        float halfH = rect.Height * 0.5f;

        // Parametric clip: smallest positive t at which the ray hits a side.
        float t = float.MaxValue;
        if (dx > 0) t = Math.Min(t, halfW / dx);
        else if (dx < 0) t = Math.Min(t, -halfW / dx);
        if (dy > 0) t = Math.Min(t, halfH / dy);
        else if (dy < 0) t = Math.Min(t, -halfH / dy);

        // t >= 1 means the target point lies inside the rect.
        if (t == float.MaxValue || t >= 1f) return center;
        return new Vector2(center.X + dx * t, center.Y + dy * t);
    }
}
