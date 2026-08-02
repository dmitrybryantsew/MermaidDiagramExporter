using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Assigns edge connection points (ports) on node perimeters.
/// For each edge without routed points, computes the closest perimeter point
/// toward the other node's center, then spreads collinear ports along the
/// same side to avoid overlap when multiple edges share a side.
/// </summary>
public static class EdgePortAssigner
{
    private const float PortSpacing = 10f;

    /// <summary>
    /// Returns a map from edge → (sourcePort, targetPort) for all edges that
    /// have no routed Points. Edges with routed Points are skipped (caller
    /// should use their Points[0]/Points[last] directly).
    /// </summary>
    public static Dictionary<GraphEdge, (Vector2 from, Vector2 to)> AssignPorts(List<GraphEdge> edges)
    {
        var result = new Dictionary<GraphEdge, (Vector2, Vector2)>();
        if (edges == null) return result;

        // Phase 1: compute the "natural" port for every edge that needs one.
        var pending = new List<(GraphEdge edge, Vector2 from, Vector2 to, Side fromSide, Side toSide)>();
        foreach (var edge in edges)
        {
            if (edge.FromNode == null || edge.ToNode == null) continue;
            if (edge.Points.Count >= 2) continue; // routed — skip

            var fromRect = new Rect(edge.FromNode.X, edge.FromNode.Y, edge.FromNode.Width, edge.FromNode.Height);
            var toRect = new Rect(edge.ToNode.X, edge.ToNode.Y, edge.ToNode.Width, edge.ToNode.Height);

            var fromCenter = fromRect.center;
            var toCenter = toRect.center;

            // Source port: exit toward target center
            var from = ClosestPerimeterPoint(fromRect, fromCenter, toCenter, out var fromSide);
            // Target port: entry from source center
            var to = ClosestPerimeterPoint(toRect, toCenter, fromCenter, out var toSide);

            pending.Add((edge, from, to, fromSide, toSide));
        }

        if (pending.Count == 0) return result;

        // Phase 2: spread ports that share the same (node, side).
        SpreadSide(pending, isFrom: true);
        SpreadSide(pending, isFrom: false);

        foreach (var p in pending)
            result[p.edge] = (p.from, p.to);

        return result;
    }

    private enum Side { Left, Right, Top, Bottom }

    /// <summary>
    /// Finds where the ray from <paramref name="origin"/> toward <paramref name="target"/>
    /// exits the rectangle. Returns the perimeter point and which side it hit.
    /// </summary>
    private static Vector2 ClosestPerimeterPoint(Rect rect, Vector2 origin, Vector2 target, out Side side)
    {
        side = Side.Right;
        var dir = target - origin;
        if (dir.sqrMagnitude < 0.0001f)
            return origin;

        float bestT = float.PositiveInfinity;
        var bestPoint = origin;
        byte bestSide = (byte)Side.Right;

        TryHit(rect.xMin, true, rect.yMin, rect.yMax, (byte)Side.Left);
        TryHit(rect.xMax, true, rect.yMin, rect.yMax, (byte)Side.Right);
        TryHit(rect.yMin, false, rect.xMin, rect.xMax, (byte)Side.Bottom);
        TryHit(rect.yMax, false, rect.xMin, rect.xMax, (byte)Side.Top);

        if (bestT < float.PositiveInfinity)
            side = (Side)bestSide;
        return bestT < float.PositiveInfinity ? bestPoint : origin;

        void TryHit(float edgeValue, bool isVertical, float minOther, float maxOther, byte s)
        {
            float primary = isVertical ? dir.x : dir.y;
            if (System.Math.Abs(primary) < 0.0001f) return;
            float t = (edgeValue - (isVertical ? origin.x : origin.y)) / primary;
            if (t <= 0f || t >= bestT) return;
            var point = origin + dir * t;
            float other = isVertical ? point.y : point.x;
            if (other < minOther - 0.001f || other > maxOther + 0.001f) return;
            bestT = t;
            bestPoint = point;
            bestSide = s;
        }
    }

    /// <summary>
    /// Groups ports by (node, side) and redistributes them evenly along that side
    /// when multiple edges share it, preventing visual overlap.
    /// </summary>
    private static void SpreadSide(List<(GraphEdge edge, Vector2 from, Vector2 to, Side fromSide, Side toSide)> pending, bool isFrom)
    {
        var groups = pending
            .GroupBy(p => isFrom
                ? (p.edge.FromNode!.Id, p.fromSide)
                : (p.edge.ToNode!.Id, p.toSide))
            .Where(g => g.Count() > 1);

        foreach (var group in groups)
        {
            var node = isFrom ? group.First().edge.FromNode! : group.First().edge.ToNode!;
            var rect = new Rect(node.X, node.Y, node.Width, node.Height);
            var side = group.Key.Item2;

            // Sort the group along the side's axis by the natural port position
            var ordered = group.OrderBy(p => SideCoordinate(isFrom ? p.from : p.to, side)).ToList();
            int n = ordered.Count;
            if (n == 0) continue;

            float spread = (n - 1) * PortSpacing;
            float start, step;
            float min, max;

            switch (side)
            {
                case Side.Left:
                case Side.Right:
                    min = rect.yMin + 6f;
                    max = rect.yMax - 6f;
                    start = System.Math.Max(min, System.Math.Min(max - spread, (min + max) * 0.5f - spread * 0.5f));
                    step = n > 1 ? System.Math.Min(PortSpacing, (max - min) / (n - 1)) : 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var y = start + i * step;
                        var x = side == Side.Left ? rect.xMin : rect.xMax;
                        var pt = new Vector2(x, y);
                        var item = ordered[i];
                        if (isFrom) item.from = pt; else item.to = pt;
                        var idx = pending.IndexOf(item);
                        if (idx >= 0) pending[idx] = item;
                    }
                    break;

                case Side.Top:
                case Side.Bottom:
                    min = rect.xMin + 6f;
                    max = rect.xMax - 6f;
                    start = System.Math.Max(min, System.Math.Min(max - spread, (min + max) * 0.5f - spread * 0.5f));
                    step = n > 1 ? System.Math.Min(PortSpacing, (max - min) / (n - 1)) : 0f;
                    for (int i = 0; i < n; i++)
                    {
                        var x = start + i * step;
                        var y = side == Side.Top ? rect.yMin : rect.yMax;
                        var pt = new Vector2(x, y);
                        var item = ordered[i];
                        if (isFrom) item.from = pt; else item.to = pt;
                        var idx = pending.IndexOf(item);
                        if (idx >= 0) pending[idx] = item;
                    }
                    break;
            }
        }
    }

    private static float SideCoordinate(Vector2 p, Side side) => side switch
    {
        Side.Left or Side.Right => p.y,
        _ => p.x,
    };
}
