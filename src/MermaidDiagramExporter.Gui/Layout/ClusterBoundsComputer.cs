using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Computes tight cluster rectangles around already-placed member nodes.
/// Used by engines that don't get usable cluster bounds from their backend:
/// the MDS MSAGL variant (MSAGL's MDS ignores clusters entirely) and the
/// own force-directed engine (no MSAGL involvement).
///
/// A cluster rect = bounding box of its member node rects ∪ child cluster
/// rects, padded: title area on top (measured <see cref="ClusterTitleMetrics"/>
/// + group top padding), group paddings on the other three sides. Deepest
/// clusters are computed first so parents can enclose children.
/// </summary>
public static class ClusterBoundsComputer
{
    public static Dictionary<string, Rect> Compute(
        IReadOnlyList<LayoutCluster> clusters,
        IReadOnlyDictionary<string, Rect> nodeBounds,
        LayoutOptions options)
    {
        var result = new Dictionary<string, Rect>();
        if (clusters == null || clusters.Count == 0) return result;

        var clusterById = clusters.ToDictionary(c => c.Id);

        // Deepest-first order: a parent is processed after all its children.
        foreach (var cluster in clusters.OrderByDescending(c => Depth(c, clusterById)))
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            bool any = false;

            foreach (var nodeId in cluster.NodeIds)
            {
                if (!nodeBounds.TryGetValue(nodeId, out var r)) continue;
                any = true;
                if (r.xMin < minX) minX = r.xMin;
                if (r.yMin < minY) minY = r.yMin;
                if (r.xMax > maxX) maxX = r.xMax;
                if (r.yMax > maxY) maxY = r.yMax;
            }

            foreach (var childId in cluster.ChildClusterIds)
            {
                if (!result.TryGetValue(childId, out var r)) continue;
                any = true;
                if (r.xMin < minX) minX = r.xMin;
                if (r.yMin < minY) minY = r.yMin;
                if (r.xMax > maxX) maxX = r.xMax;
                if (r.yMax > maxY) maxY = r.yMax;
            }

            if (!any) continue;

            float padL = options.GroupLeftPadding;
            float padR = options.GroupLeftPadding;
            float padT = options.GroupTopPadding + cluster.TitleMetrics.TotalMargin;
            float padB = options.GroupBottomPadding;

            result[cluster.Id] = Rect.MinMaxRect(
                minX - padL, minY - padT,
                maxX + padR, maxY + padB);
        }

        return result;
    }

    private static int Depth(LayoutCluster cluster, IReadOnlyDictionary<string, LayoutCluster> all)
    {
        int depth = 0;
        var current = cluster;
        // Guard against parent cycles with a hard step cap.
        for (int guard = 0; guard < 64 && !string.IsNullOrEmpty(current.ParentClusterId); guard++)
        {
            if (!all.TryGetValue(current.ParentClusterId, out var parent)) break;
            depth++;
            current = parent;
        }
        return depth;
    }
}
