using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class ClusterTitleMarginPass : IPostLayoutPass
{
    /// <summary>Convergence threshold for float comparisons in layout passes.</summary>
    private const float LayoutEpsilon = 0.01f;

    public string Name => "Cluster Title Margin";

    public LayoutResult Run(LayoutGraph graph, LayoutResult result, LayoutOptions options)
    {
        if (graph == null || result == null)
            return result ?? new LayoutResult();

        var clone = PostLayoutResultUtility.CloneResult(result);
        var nodeBounds = (Dictionary<string, Rect>)clone.NodeBounds;
        var clusterBounds = (Dictionary<string, Rect>)clone.ClusterBounds;

        foreach (var cluster in graph.Clusters)
        {
            if (!clusterBounds.TryGetValue(cluster.Id, out var clusterRect))
                continue;

            float desiredInset = Mathf.Max(options.GroupTopPadding, (cluster.TitleMetrics?.TotalMargin ?? 0f) + 12f);
            float currentInset = CalculateCurrentTopInset(graph, cluster, clusterRect, nodeBounds, clusterBounds);
            float delta = desiredInset - currentInset;
            if (delta <= LayoutEpsilon)
                continue;

            ShiftClusterContents(graph, cluster, delta, nodeBounds, clusterBounds);
            clusterRect.height += delta;
            clusterBounds[cluster.Id] = clusterRect;
        }

        clone.ContentSize = RecalculateContentSize(nodeBounds.Values, clusterBounds.Values, options, clone.ContentSize);
        return clone;
    }

    private static float CalculateCurrentTopInset(
        LayoutGraph graph,
        LayoutCluster cluster,
        Rect clusterRect,
        IReadOnlyDictionary<string, Rect> nodeBounds,
        IReadOnlyDictionary<string, Rect> clusterBounds)
    {
        float minY = float.PositiveInfinity;

        foreach (var nodeId in cluster.NodeIds)
        {
            if (nodeBounds.TryGetValue(nodeId, out var nodeRect))
                minY = Mathf.Min(minY, nodeRect.yMin);
        }

        foreach (var childClusterId in cluster.ChildClusterIds)
        {
            if (clusterBounds.TryGetValue(childClusterId, out var childClusterRect))
                minY = Mathf.Min(minY, childClusterRect.yMin);
        }

        if (float.IsPositiveInfinity(minY))
            return clusterRect.height;

        return minY - clusterRect.yMin;
    }

    private static void ShiftClusterContents(
        LayoutGraph graph,
        LayoutCluster cluster,
        float deltaY,
        IDictionary<string, Rect> nodeBounds,
        IDictionary<string, Rect> clusterBounds)
    {
        var descendantClusterIds = PostLayoutResultUtility.GetDescendantClusterIds(graph, cluster.Id);

        foreach (var nodeId in cluster.NodeIds)
        {
            if (nodeBounds.TryGetValue(nodeId, out var nodeRect))
            {
                nodeRect.y += deltaY;
                nodeBounds[nodeId] = nodeRect;
            }
        }

        foreach (var descendantClusterId in descendantClusterIds)
        {
            var descendantCluster = graph.Clusters.FirstOrDefault(candidate => candidate.Id == descendantClusterId);
            if (descendantCluster == null)
                continue;

            if (clusterBounds.TryGetValue(descendantClusterId, out var descendantClusterRect))
            {
                descendantClusterRect.y += deltaY;
                clusterBounds[descendantClusterId] = descendantClusterRect;
            }

            foreach (var nodeId in descendantCluster.NodeIds)
            {
                if (nodeBounds.TryGetValue(nodeId, out var nodeRect))
                {
                    nodeRect.y += deltaY;
                    nodeBounds[nodeId] = nodeRect;
                }
            }
        }
    }

    private static Vector2 RecalculateContentSize(
        IEnumerable<Rect> nodeRects,
        IEnumerable<Rect> clusterRects,
        LayoutOptions options,
        Vector2 current)
    {
        float maxX = 0f;
        float maxY = 0f;

        foreach (var rect in nodeRects.Concat(clusterRects))
        {
            maxX = Mathf.Max(maxX, rect.xMax);
            maxY = Mathf.Max(maxY, rect.yMax);
        }

        return new Vector2(
            Mathf.Max(current.x, maxX + options.OuterMarginX),
            Mathf.Max(current.y, maxY + options.OuterMarginY));
    }
}
