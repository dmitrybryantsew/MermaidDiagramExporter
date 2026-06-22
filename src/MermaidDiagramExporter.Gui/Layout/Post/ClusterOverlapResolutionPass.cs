using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class ClusterOverlapResolutionPass : IPostLayoutPass
{
    public string Name => "Cluster Overlap Resolution";

    public LayoutResult Run(LayoutGraph graph, LayoutResult result, LayoutOptions options)
    {
        if (graph == null || result == null)
            return result ?? new LayoutResult();

        var clone = PostLayoutResultUtility.CloneResult(result);
        var nodeBounds = (Dictionary<string, Rect>)clone.NodeBounds;
        var clusterBounds = (Dictionary<string, Rect>)clone.ClusterBounds;

        bool changed;
        int guard = 0;
        do
        {
            changed = false;
            foreach (var siblingGroup in graph.Clusters.GroupBy(cluster => cluster.ParentClusterId ?? string.Empty))
            {
                var orderedSiblings = siblingGroup
                    .Where(cluster => clusterBounds.ContainsKey(cluster.Id))
                    .OrderBy(cluster => clusterBounds[cluster.Id].yMin)
                    .ThenBy(cluster => clusterBounds[cluster.Id].xMin)
                    .ToList();

                for (int i = 0; i < orderedSiblings.Count; i++)
                {
                    var current = orderedSiblings[i];
                    var currentRect = clusterBounds[current.Id];

                    for (int j = i + 1; j < orderedSiblings.Count; j++)
                    {
                        var next = orderedSiblings[j];
                        var nextRect = clusterBounds[next.Id];

                        if (!HasHorizontalOverlap(currentRect, nextRect))
                            continue;

                        float minimumTop = currentRect.yMax + options.ClusterSpacing;
                        if (nextRect.yMin >= minimumTop - 0.01f)
                            continue;

                        float deltaY = minimumTop - nextRect.yMin;
                        PostLayoutResultUtility.ShiftClusterSubtree(
                            graph,
                            next.Id,
                            new Vector2(0f, deltaY),
                            nodeBounds,
                            clusterBounds);

                        changed = true;
                        nextRect = clusterBounds[next.Id];
                        orderedSiblings = siblingGroup
                            .Where(cluster => clusterBounds.ContainsKey(cluster.Id))
                            .OrderBy(cluster => clusterBounds[cluster.Id].yMin)
                            .ThenBy(cluster => clusterBounds[cluster.Id].xMin)
                            .ToList();
                    }
                }
            }

            guard++;
        }
        while (changed && guard < 12);

        clone.ContentSize = RecalculateContentSize(nodeBounds.Values, clusterBounds.Values, options, clone.ContentSize);
        return clone;
    }

    private static bool HasHorizontalOverlap(Rect a, Rect b)
    {
        return a.xMin < b.xMax - 0.01f && b.xMin < a.xMax - 0.01f;
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
