using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class ClusterBoundsPolishPass : IPostLayoutPass
{
    public string Name => "Cluster Bounds Polish";

    public LayoutResult Run(LayoutGraph graph, LayoutResult result, LayoutOptions options)
    {
        if (graph == null || result == null)
            return result ?? new LayoutResult();

        var clone = PostLayoutResultUtility.CloneResult(result);
        var nodeBounds = (Dictionary<string, Rect>)clone.NodeBounds;
        var clusterBounds = (Dictionary<string, Rect>)clone.ClusterBounds;

        var orderedClusters = graph.Clusters
            .OrderByDescending(cluster => PostLayoutResultUtility.GetDescendantClusterIds(graph, cluster.Id).Count)
            .ToList();

        foreach (var cluster in orderedClusters)
        {
            if (!clusterBounds.TryGetValue(cluster.Id, out var clusterRect))
                continue;

            Rect? contentBounds = null;
            foreach (var nodeId in cluster.NodeIds)
            {
                if (nodeBounds.TryGetValue(nodeId, out var nodeRect))
                {
                    contentBounds = contentBounds.HasValue ? Encapsulate(contentBounds.Value, nodeRect) : nodeRect;
                }
            }

            foreach (var childClusterId in cluster.ChildClusterIds)
            {
                if (clusterBounds.TryGetValue(childClusterId, out var childClusterRect))
                {
                    contentBounds = contentBounds.HasValue ? Encapsulate(contentBounds.Value, childClusterRect) : childClusterRect;
                }
            }

            float minWidthFromTitle = Mathf.Max(options.GroupWidth, (cluster.TitleMetrics?.LabelWidth ?? 0f) + options.ClusterTitleHorizontalPadding);
            if (contentBounds.HasValue)
            {
                var contents = contentBounds.Value;
                float requiredLeft = contents.xMin - options.GroupLeftPadding;
                float requiredTop = clusterRect.yMin;
                float requiredWidth = (contents.xMax - requiredLeft) + options.GroupLeftPadding;
                float requiredHeight = (contents.yMax - requiredTop) + options.GroupBottomPadding;

                clusterRect.x = Mathf.Min(clusterRect.x, requiredLeft);
                clusterRect.width = Mathf.Max(clusterRect.width, requiredWidth, minWidthFromTitle);
                clusterRect.height = Mathf.Max(clusterRect.height, requiredHeight);
            }
            else
            {
                clusterRect.width = Mathf.Max(clusterRect.width, minWidthFromTitle);
            }

            clusterBounds[cluster.Id] = clusterRect;
        }

        clone.ContentSize = RecalculateContentSize(nodeBounds.Values, clusterBounds.Values, options, clone.ContentSize);
        return clone;
    }

    private static Rect Encapsulate(Rect a, Rect b)
    {
        float xMin = Mathf.Min(a.xMin, b.xMin);
        float yMin = Mathf.Min(a.yMin, b.yMin);
        float xMax = Mathf.Max(a.xMax, b.xMax);
        float yMax = Mathf.Max(a.yMax, b.yMax);
        return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
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
