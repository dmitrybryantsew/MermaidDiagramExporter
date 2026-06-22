using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Persistence;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Applies manual position overrides to a LayoutResult.
/// Called after the layout engine computes positions, before the canvas renders.
/// </summary>
public static class ManualLayoutApplier
{
    /// <summary>
    /// Adjusts node bounds in the LayoutResult by applying stored manual deltas.
    /// Returns a NEW LayoutResult (immutable transform).
    /// </summary>
    public static LayoutResult ApplyOverrides(LayoutResult result, ManualLayoutOverrides overrides)
    {
        if (overrides == null || !overrides.HasOverrides)
            return result;

        var clone = PostLayoutResultUtility.CloneResult(result);
        var nodeBounds = (Dictionary<string, Rect>)clone.NodeBounds;

        foreach (var kvp in overrides.NodePositionDeltas)
        {
            string nodeId = kvp.Key;
            Vector2 delta = kvp.Value;
            if (nodeBounds.TryGetValue(nodeId, out var rect))
            {
                nodeBounds[nodeId] = new Rect(rect.X + delta.X, rect.Y + delta.Y, rect.Width, rect.Height);
            }
        }

        // Recalculate cluster bounds to encompass moved nodes
        RecalculateClusterBounds(clone, nodeBounds);

        return clone;
    }

    /// <summary>
    /// After moving nodes, recalculate cluster bounds to ensure they still contain their nodes.
    /// </summary>
    private static void RecalculateClusterBounds(LayoutResult result, Dictionary<string, Rect> nodeBounds)
    {
        var clusterBounds = (Dictionary<string, Rect>)result.ClusterBounds;
        foreach (var clusterId in clusterBounds.Keys.ToList())
        {
            // Find all nodes belonging to this cluster
            var nodeIdsInCluster = result.NodeClusterIds
                .Where(kvp => kvp.Value == clusterId)
                .Select(kvp => kvp.Key)
                .ToList();

            if (nodeIdsInCluster.Count == 0) continue;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var nodeId in nodeIdsInCluster)
            {
                if (nodeBounds.TryGetValue(nodeId, out var r))
                {
                    minX = Mathf.Min(minX, r.xMin);
                    minY = Mathf.Min(minY, r.yMin);
                    maxX = Mathf.Max(maxX, r.xMax);
                    maxY = Mathf.Max(maxY, r.yMax);
                }
            }

            if (minX < float.MaxValue)
            {
                // Add padding around the cluster
                float padding = 24;
                float titleHeight = 24;
                clusterBounds[clusterId] = new Rect(
                    minX - padding,
                    minY - padding - titleHeight,
                    maxX - minX + padding * 2,
                    maxY - minY + padding * 2 + titleHeight);
            }
        }

        // Recalculate content size
        float contentMaxX = nodeBounds.Values.Concat(clusterBounds.Values).Max(r => r.xMax);
        float contentMaxY = nodeBounds.Values.Concat(clusterBounds.Values).Max(r => r.yMax);
        result.ContentSize = new Vector2(contentMaxX + 40, contentMaxY + 52);
    }
}
