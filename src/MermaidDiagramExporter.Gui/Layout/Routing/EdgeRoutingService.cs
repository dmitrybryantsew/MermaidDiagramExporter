using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class EdgeRoutingService
{
    private readonly ClusterBoundaryClipper _clipper = new();

    public IReadOnlyList<LayoutEdgePath> BuildPaths(TypeGraph graph, LayoutResult result, LayoutOptions options)
    {
        if (graph == null || result == null || result.NodeBounds.Count == 0)
            return Array.Empty<LayoutEdgePath>();

        Dictionary<string, string> clusterIdByNodeId = result.NodeClusterIds != null
            ? new Dictionary<string, string>(result.NodeClusterIds)
            : new Dictionary<string, string>();
        Dictionary<string, CrossClusterLane> crossClusterLanes = BuildCrossClusterLanes(graph, result, clusterIdByNodeId);
        List<LayoutEdgePath> paths = new();

        foreach (TypeEdgeData edge in graph.Edges)
        {
            if (!result.NodeBounds.TryGetValue(edge.FromNodeId, out Rect fromRect)
                || !result.NodeBounds.TryGetValue(edge.ToNodeId, out Rect toRect))
            {
                continue;
            }

            string fromClusterId = clusterIdByNodeId.TryGetValue(edge.FromNodeId, out string fromCluster) ? fromCluster : "";
            string toClusterId = clusterIdByNodeId.TryGetValue(edge.ToNodeId, out string toCluster) ? toCluster : "";

            LayoutEdgePath path = BuildPath(
                edge,
                fromRect,
                toRect,
                fromClusterId,
                toClusterId,
                result.ClusterBounds,
                crossClusterLanes);

            if (path.Points.Count >= 2)
                paths.Add(path);
        }

        return paths;
    }

    private LayoutEdgePath BuildPath(
        TypeEdgeData edge,
        Rect fromRect,
        Rect toRect,
        string fromClusterId,
        string toClusterId,
        IReadOnlyDictionary<string, Rect> clusterBounds,
        IReadOnlyDictionary<string, CrossClusterLane> crossClusterLanes)
    {
        bool isCrossCluster = !string.IsNullOrEmpty(fromClusterId)
            && !string.IsNullOrEmpty(toClusterId)
            && fromClusterId != toClusterId
            && clusterBounds.ContainsKey(fromClusterId)
            && clusterBounds.ContainsKey(toClusterId);

        IReadOnlyList<Vector2> points;

        if (isCrossCluster)
        {
            CrossClusterLane lane = crossClusterLanes.TryGetValue(BuildEdgeId(edge), out CrossClusterLane? value)
                ? value
                : CrossClusterLane.BuildFallback(clusterBounds[fromClusterId], clusterBounds[toClusterId]);
            points = BuildCrossClusterPoints(
                fromRect,
                toRect,
                clusterBounds[fromClusterId],
                clusterBounds[toClusterId],
                lane);
        }
        else
        {
            points = BuildSameClusterPoints(fromRect, toRect);
        }

        return new LayoutEdgePath
        {
            EdgeId = BuildEdgeId(edge),
            FromNodeId = edge.FromNodeId,
            ToNodeId = edge.ToNodeId,
            Kind = edge.Kind,
            IsClippedToClusters = isCrossCluster,
            Points = CleanupPath(points)
        };
    }

    private IReadOnlyList<Vector2> BuildSameClusterPoints(Rect fromRect, Rect toRect)
    {
        Vector2 fromCenter = fromRect.center;
        Vector2 toCenter = toRect.center;
        float dx = toCenter.x - fromCenter.x;
        float dy = toCenter.y - fromCenter.y;
        List<Vector2> points = new();

        if (Mathf.Abs(dx) >= Mathf.Abs(dy))
        {
            float midX = fromCenter.x + (dx * 0.5f);
            Vector2 firstCorner = new(midX, fromCenter.y);
            Vector2 secondCorner = new(midX, toCenter.y);
            Vector2 start = _clipper.FindIntersection(fromRect, fromCenter, firstCorner);
            Vector2 end = _clipper.FindIntersection(toRect, toCenter, secondCorner);
            points.Add(start);
            points.Add(firstCorner);
            points.Add(secondCorner);
            points.Add(end);
            return CleanupPath(points);
        }

        {
            float midY = fromCenter.y + (dy * 0.5f);
            Vector2 topCorner = new(fromCenter.x, midY);
            Vector2 bottomCorner = new(toCenter.x, midY);
            Vector2 verticalStart = _clipper.FindIntersection(fromRect, fromCenter, topCorner);
            Vector2 verticalEnd = _clipper.FindIntersection(toRect, toCenter, bottomCorner);
            points.Add(verticalStart);
            points.Add(topCorner);
            points.Add(bottomCorner);
            points.Add(verticalEnd);
            return CleanupPath(points);
        }
    }

    private IReadOnlyList<Vector2> BuildCrossClusterPoints(
        Rect fromRect,
        Rect toRect,
        Rect fromClusterBounds,
        Rect toClusterBounds,
        CrossClusterLane lane)
    {
        Vector2 fromCenter = fromRect.center;
        Vector2 toCenter = toRect.center;

        if (TryBuildAlignedDirectClusterRoute(
                fromRect,
                toRect,
                fromClusterBounds,
                toClusterBounds,
                lane,
                out List<Vector2>? directPoints))
        {
            return CleanupPath(directPoints);
        }

        List<Vector2> points = new();
        Vector2 exit;
        Vector2 entry;

        if (lane.Orientation == CrossClusterLaneOrientation.HorizontalTrunk)
        {
            float exitX = lane.SourceBranchCoordinate >= fromClusterBounds.center.x
                ? fromClusterBounds.xMax
                : fromClusterBounds.xMin;
            float entryX = lane.TargetBranchCoordinate >= toClusterBounds.center.x
                ? toClusterBounds.xMax
                : toClusterBounds.xMin;
            float exitY = Mathf.Clamp(fromCenter.y, fromClusterBounds.yMin + 6f, fromClusterBounds.yMax - 6f);
            float entryY = Mathf.Clamp(toCenter.y, toClusterBounds.yMin + 6f, toClusterBounds.yMax - 6f);

            exit = new Vector2(exitX, exitY);
            entry = new Vector2(entryX, entryY);

            Vector2 start = _clipper.FindIntersection(fromRect, fromCenter, exit);
            Vector2 end = _clipper.FindIntersection(toRect, toCenter, entry);
            points.Add(start);
            points.Add(exit);
            points.Add(new Vector2(lane.SourceBranchCoordinate, exit.y));
            points.Add(new Vector2(lane.SourceBranchCoordinate, lane.SharedCoordinate));
            points.Add(new Vector2(lane.TargetBranchCoordinate, lane.SharedCoordinate));
            points.Add(new Vector2(lane.TargetBranchCoordinate, entry.y));
            points.Add(entry);
            points.Add(end);
        }
        else
        {
            float exitY = lane.SourceBranchCoordinate >= fromClusterBounds.center.y
                ? fromClusterBounds.yMax
                : fromClusterBounds.yMin;
            float entryY = lane.TargetBranchCoordinate >= toClusterBounds.center.y
                ? toClusterBounds.yMax
                : toClusterBounds.yMin;
            float exitX = Mathf.Clamp(fromCenter.x, fromClusterBounds.xMin + 6f, fromClusterBounds.xMax - 6f);
            float entryX = Mathf.Clamp(toCenter.x, toClusterBounds.xMin + 6f, toClusterBounds.xMax - 6f);

            exit = new Vector2(exitX, exitY);
            entry = new Vector2(entryX, entryY);

            Vector2 start = _clipper.FindIntersection(fromRect, fromCenter, exit);
            Vector2 end = _clipper.FindIntersection(toRect, toCenter, entry);
            points.Add(start);
            points.Add(exit);
            points.Add(new Vector2(exit.x, lane.SourceBranchCoordinate));
            points.Add(new Vector2(lane.SharedCoordinate, lane.SourceBranchCoordinate));
            points.Add(new Vector2(lane.SharedCoordinate, lane.TargetBranchCoordinate));
            points.Add(new Vector2(entry.x, lane.TargetBranchCoordinate));
            points.Add(entry);
            points.Add(end);
        }

        return CleanupPath(points);
    }

    private bool TryBuildAlignedDirectClusterRoute(
        Rect fromRect,
        Rect toRect,
        Rect fromClusterBounds,
        Rect toClusterBounds,
        CrossClusterLane lane,
        out List<Vector2>? points)
    {
        Vector2 fromCenter = fromRect.center;
        Vector2 toCenter = toRect.center;
        points = null;

        const float directAlignmentThreshold = 18f;

        if (lane.Orientation == CrossClusterLaneOrientation.VerticalTrunk
            && Mathf.Abs(fromCenter.x - toCenter.x) <= directAlignmentThreshold)
        {
            float trunkX = (fromCenter.x + toCenter.x) * 0.5f;
            float sourceEdgeY = fromClusterBounds.center.y <= toClusterBounds.center.y
                ? fromClusterBounds.yMax
                : fromClusterBounds.yMin;
            float targetEdgeY = fromClusterBounds.center.y <= toClusterBounds.center.y
                ? toClusterBounds.yMin
                : toClusterBounds.yMax;

            Vector2 sourceExit = new(trunkX, sourceEdgeY);
            Vector2 targetEntry = new(trunkX, targetEdgeY);
            Vector2 start = _clipper.FindIntersection(fromRect, fromCenter, sourceExit);
            Vector2 end = _clipper.FindIntersection(toRect, toCenter, targetEntry);

            points = new List<Vector2>
            {
                start,
                sourceExit,
                targetEntry,
                end
            };
            return true;
        }

        if (lane.Orientation == CrossClusterLaneOrientation.HorizontalTrunk
            && Mathf.Abs(fromCenter.y - toCenter.y) <= directAlignmentThreshold)
        {
            float trunkY = (fromCenter.y + toCenter.y) * 0.5f;
            float sourceEdgeX = fromClusterBounds.center.x <= toClusterBounds.center.x
                ? fromClusterBounds.xMax
                : fromClusterBounds.xMin;
            float targetEdgeX = fromClusterBounds.center.x <= toClusterBounds.center.x
                ? toClusterBounds.xMin
                : toClusterBounds.xMax;

            Vector2 sourceExit = new(sourceEdgeX, trunkY);
            Vector2 targetEntry = new(targetEdgeX, trunkY);
            Vector2 start = _clipper.FindIntersection(fromRect, fromCenter, sourceExit);
            Vector2 end = _clipper.FindIntersection(toRect, toCenter, targetEntry);

            points = new List<Vector2>
            {
                start,
                sourceExit,
                targetEntry,
                end
            };
            return true;
        }

        return false;
    }

    private static IReadOnlyList<Vector2> CleanupPath(IEnumerable<Vector2> points)
    {
        return SimplifyOrthogonalPoints(
            RemoveShortDoglegs(
                DeduplicateSequentialPoints(points)));
    }

    private static IReadOnlyList<Vector2> DeduplicateSequentialPoints(IEnumerable<Vector2> points)
    {
        List<Vector2> deduplicated = new();
        foreach (Vector2 point in points)
        {
            if (deduplicated.Count > 0 && Vector2.Distance(deduplicated[deduplicated.Count - 1], point) < 0.5f)
                continue;
            deduplicated.Add(point);
        }
        return deduplicated;
    }

    private static IReadOnlyList<Vector2> RemoveShortDoglegs(IReadOnlyList<Vector2> points)
    {
        if (points.Count <= 3)
            return points;

        const float doglegThreshold = 22f;
        List<Vector2> reduced = new(points);
        bool changed;
        int safetyCounter = 0;
        int maxIterations = Mathf.Max(8, reduced.Count * 2);
        do
        {
            changed = false;
            for (int index = 1; index < reduced.Count - 2; index++)
            {
                Vector2 a = reduced[index - 1];
                Vector2 b = reduced[index];
                Vector2 c = reduced[index + 1];
                Vector2 d = reduced[index + 2];

                bool horizontalVerticalHorizontal =
                    Mathf.Abs(a.y - b.y) < 0.01f &&
                    Mathf.Abs(b.x - c.x) < 0.01f &&
                    Mathf.Abs(c.y - d.y) < 0.01f;
                bool verticalHorizontalVertical =
                    Mathf.Abs(a.x - b.x) < 0.01f &&
                    Mathf.Abs(b.y - c.y) < 0.01f &&
                    Mathf.Abs(c.x - d.x) < 0.01f;

                if (!horizontalVerticalHorizontal && !verticalHorizontalVertical)
                    continue;

                float jogLength = Vector2.Distance(b, c);
                if (jogLength > doglegThreshold)
                    continue;

                if (horizontalVerticalHorizontal)
                {
                    bool sameHorizontalSide = (b.x - a.x) * (d.x - c.x) >= 0f;
                    if (!sameHorizontalSide)
                        continue;
                }
                else
                {
                    bool sameVerticalSide = (b.y - a.y) * (d.y - c.y) >= 0f;
                    if (!sameVerticalSide)
                        continue;
                }

                reduced.RemoveAt(index + 1);
                reduced.RemoveAt(index);
                changed = true;
                break;
            }
            safetyCounter++;
        }
        while (changed && safetyCounter < maxIterations);

        return reduced;
    }

    private static IReadOnlyList<Vector2> SimplifyOrthogonalPoints(IReadOnlyList<Vector2> points)
    {
        if (points.Count <= 2)
            return points;

        List<Vector2> simplified = new() { points[0] };
        for (int index = 1; index < points.Count - 1; index++)
        {
            Vector2 previous = simplified[simplified.Count - 1];
            Vector2 current = points[index];
            Vector2 next = points[index + 1];

            bool sameX = Mathf.Abs(previous.x - current.x) < 0.01f && Mathf.Abs(current.x - next.x) < 0.01f;
            bool sameY = Mathf.Abs(previous.y - current.y) < 0.01f && Mathf.Abs(current.y - next.y) < 0.01f;
            if (sameX || sameY)
                continue;

            simplified.Add(current);
        }

        simplified.Add(points[points.Count - 1]);
        return simplified;
    }

    private static string BuildEdgeId(TypeEdgeData edge)
    {
        return edge.FromNodeId + "->" + edge.ToNodeId + ":" + edge.Kind;
    }

    private static Dictionary<string, CrossClusterLane> BuildCrossClusterLanes(
        TypeGraph graph,
        LayoutResult result,
        IReadOnlyDictionary<string, string> clusterIdByNodeId)
    {
        Dictionary<string, CrossClusterLane> lanesByEdgeId = new();
        Dictionary<string, List<TypeEdgeData>> edgesByClusterPair = new();

        foreach (TypeEdgeData edge in graph.Edges)
        {
            if (!clusterIdByNodeId.TryGetValue(edge.FromNodeId, out string? fromClusterId)
                || !clusterIdByNodeId.TryGetValue(edge.ToNodeId, out string? toClusterId)
                || string.IsNullOrEmpty(fromClusterId)
                || string.IsNullOrEmpty(toClusterId)
                || fromClusterId == toClusterId
                || !result.ClusterBounds.ContainsKey(fromClusterId)
                || !result.ClusterBounds.ContainsKey(toClusterId))
            {
                continue;
            }

            string key = fromClusterId + "->" + toClusterId;
            if (!edgesByClusterPair.TryGetValue(key, out List<TypeEdgeData>? pairEdges))
            {
                pairEdges = new List<TypeEdgeData>();
                edgesByClusterPair[key] = pairEdges;
            }

            pairEdges.Add(edge);
        }

        foreach (KeyValuePair<string, List<TypeEdgeData>> pair in edgesByClusterPair)
        {
            TypeEdgeData firstEdge = pair.Value[0];
            string fromClusterId = clusterIdByNodeId[firstEdge.FromNodeId];
            string toClusterId = clusterIdByNodeId[firstEdge.ToNodeId];
            Rect fromClusterBounds = result.ClusterBounds[fromClusterId];
            Rect toClusterBounds = result.ClusterBounds[toClusterId];

            CrossClusterLaneOrientation orientation = Mathf.Abs(toClusterBounds.center.x - fromClusterBounds.center.x)
                >= Mathf.Abs(toClusterBounds.center.y - fromClusterBounds.center.y)
                ? CrossClusterLaneOrientation.HorizontalTrunk
                : CrossClusterLaneOrientation.VerticalTrunk;

            float baseCoordinate = ComputeBundleCoordinate(orientation, pair.Value, result.NodeBounds, fromClusterBounds, toClusterBounds);
            List<TypeEdgeData> orderedEdges = OrderPairEdges(pair.Value, result.NodeBounds, orientation);
            float centerIndex = (orderedEdges.Count - 1) * 0.5f;
            float spacing = orderedEdges.Count > 2 ? 12f : 8f;
            float sourceBranchCoordinate = ComputeSourceBranchCoordinate(orientation, fromClusterBounds, toClusterBounds);
            float targetBranchCoordinate = ComputeTargetBranchCoordinate(orientation, fromClusterBounds, toClusterBounds);

            for (int index = 0; index < orderedEdges.Count; index++)
            {
                float coordinate = baseCoordinate + ((index - centerIndex) * spacing);
                lanesByEdgeId[BuildEdgeId(orderedEdges[index])] = new CrossClusterLane
                {
                    Orientation = orientation,
                    SharedCoordinate = coordinate,
                    SourceBranchCoordinate = sourceBranchCoordinate,
                    TargetBranchCoordinate = targetBranchCoordinate
                };
            }
        }

        return lanesByEdgeId;
    }

    private static float ComputeBundleCoordinate(
        CrossClusterLaneOrientation orientation,
        IEnumerable<TypeEdgeData> edges,
        IReadOnlyDictionary<string, Rect> nodeBounds,
        Rect fromBounds,
        Rect toBounds)
    {
        float averageEdgeCoordinate = edges
            .Select(edge => GetEdgeOrderValue(edge, nodeBounds, orientation))
            .DefaultIfEmpty(orientation == CrossClusterLaneOrientation.HorizontalTrunk
                ? (fromBounds.center.y + toBounds.center.y) * 0.5f
                : (fromBounds.center.x + toBounds.center.x) * 0.5f)
            .Average();

        if (orientation == CrossClusterLaneOrientation.HorizontalTrunk)
        {
            return Mathf.Clamp(
                averageEdgeCoordinate,
                Mathf.Min(fromBounds.yMin, toBounds.yMin) - 48f,
                Mathf.Max(fromBounds.yMax, toBounds.yMax) + 48f);
        }

        return Mathf.Clamp(
            averageEdgeCoordinate,
            Mathf.Min(fromBounds.xMin, toBounds.xMin) - 48f,
            Mathf.Max(fromBounds.xMax, toBounds.xMax) + 48f);
    }

    private static List<TypeEdgeData> OrderPairEdges(
        IEnumerable<TypeEdgeData> edges,
        IReadOnlyDictionary<string, Rect> nodeBounds,
        CrossClusterLaneOrientation orientation)
    {
        return edges
            .OrderBy(edge => GetEdgeOrderValue(edge, nodeBounds, orientation))
            .ThenBy(edge => GetEdgeSecondaryOrderValue(edge, nodeBounds, orientation))
            .ThenBy(edge => BuildEdgeId(edge))
            .ToList();
    }

    private static float GetEdgeOrderValue(
        TypeEdgeData edge,
        IReadOnlyDictionary<string, Rect> nodeBounds,
        CrossClusterLaneOrientation orientation)
    {
        if (!nodeBounds.TryGetValue(edge.FromNodeId, out Rect fromRect)
            || !nodeBounds.TryGetValue(edge.ToNodeId, out Rect toRect))
        {
            return 0f;
        }

        return orientation == CrossClusterLaneOrientation.HorizontalTrunk
            ? (fromRect.center.y + toRect.center.y) * 0.5f
            : (fromRect.center.x + toRect.center.x) * 0.5f;
    }

    private static float GetEdgeSecondaryOrderValue(
        TypeEdgeData edge,
        IReadOnlyDictionary<string, Rect> nodeBounds,
        CrossClusterLaneOrientation orientation)
    {
        if (!nodeBounds.TryGetValue(edge.FromNodeId, out Rect fromRect)
            || !nodeBounds.TryGetValue(edge.ToNodeId, out Rect toRect))
        {
            return 0f;
        }

        return orientation == CrossClusterLaneOrientation.HorizontalTrunk
            ? fromRect.center.y
            : fromRect.center.x;
    }

    private static float ComputeSourceBranchCoordinate(
        CrossClusterLaneOrientation orientation,
        Rect fromBounds,
        Rect toBounds)
    {
        const float fanOutDistance = 18f;
        if (orientation == CrossClusterLaneOrientation.HorizontalTrunk)
        {
            return fromBounds.center.x <= toBounds.center.x
                ? fromBounds.xMax + fanOutDistance
                : fromBounds.xMin - fanOutDistance;
        }

        return fromBounds.center.y <= toBounds.center.y
            ? fromBounds.yMax + fanOutDistance
            : fromBounds.yMin - fanOutDistance;
    }

    private static float ComputeTargetBranchCoordinate(
        CrossClusterLaneOrientation orientation,
        Rect fromBounds,
        Rect toBounds)
    {
        const float fanOutDistance = 18f;
        if (orientation == CrossClusterLaneOrientation.HorizontalTrunk)
        {
            return fromBounds.center.x <= toBounds.center.x
                ? toBounds.xMin - fanOutDistance
                : toBounds.xMax + fanOutDistance;
        }

        return fromBounds.center.y <= toBounds.center.y
            ? toBounds.yMin - fanOutDistance
            : toBounds.yMax + fanOutDistance;
    }
}

internal enum CrossClusterLaneOrientation
{
    HorizontalTrunk,
    VerticalTrunk
}

internal sealed class CrossClusterLane
{
    public CrossClusterLaneOrientation Orientation { get; set; }

    public float SharedCoordinate { get; set; }

    public float SourceBranchCoordinate { get; set; }

    public float TargetBranchCoordinate { get; set; }

    public static CrossClusterLane BuildFallback(Rect fromBounds, Rect toBounds)
    {
        CrossClusterLaneOrientation orientation = Mathf.Abs(toBounds.center.x - fromBounds.center.x)
            >= Mathf.Abs(toBounds.center.y - fromBounds.center.y)
            ? CrossClusterLaneOrientation.HorizontalTrunk
            : CrossClusterLaneOrientation.VerticalTrunk;

        const float fanOutDistance = 18f;

        float sourceBranch, targetBranch;
        if (orientation == CrossClusterLaneOrientation.HorizontalTrunk)
        {
            sourceBranch = fromBounds.center.x <= toBounds.center.x
                ? fromBounds.xMax + fanOutDistance
                : fromBounds.xMin - fanOutDistance;
            targetBranch = fromBounds.center.x <= toBounds.center.x
                ? toBounds.xMin - fanOutDistance
                : toBounds.xMax + fanOutDistance;
        }
        else
        {
            sourceBranch = fromBounds.center.y <= toBounds.center.y
                ? fromBounds.yMax + fanOutDistance
                : fromBounds.yMin - fanOutDistance;
            targetBranch = fromBounds.center.y <= toBounds.center.y
                ? toBounds.yMin - fanOutDistance
                : toBounds.yMax + fanOutDistance;
        }

        return new CrossClusterLane
        {
            Orientation = orientation,
            SharedCoordinate = orientation == CrossClusterLaneOrientation.HorizontalTrunk
                ? (fromBounds.center.y + toBounds.center.y) * 0.5f
                : (fromBounds.center.x + toBounds.center.x) * 0.5f,
            SourceBranchCoordinate = sourceBranch,
            TargetBranchCoordinate = targetBranch
        };
    }
}
