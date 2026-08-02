using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Guarantees sibling namespace clusters never overlap, for 2D scattered
/// layouts (force engine). Unlike the shared ClusterOverlapResolutionPass
/// (which only pushes clusters down — right for row-based layered layouts but
/// aspect-destroying for 2D), this separates each overlapping pair along the
/// axis of least penetration, splitting the translation half/half so the
/// overall center of mass is preserved.
///
/// Processes sibling groups bottom-up (deepest first) and recomputes cluster
/// bounds after each level, so parents tightly re-enclose their separated
/// children — by induction, no two clusters at any level overlap afterwards
/// (parent-child containment is intended, not overlap).
/// </summary>
public static class ForceClusterSeparation
{
    /// <summary>
    /// Mutates nodeBounds (subtree nodes move with their cluster) and
    /// clusterBounds in place until no sibling clusters overlap.
    /// </summary>
    public static void Separate(
        IReadOnlyList<LayoutCluster> clusters,
        Dictionary<string, Rect> nodeBounds,
        Dictionary<string, Rect> clusterBounds,
        LayoutOptions options)
    {
        if (clusters == null || clusters.Count == 0) return;

        var clusterById = clusters.ToDictionary(c => c.Id);
        var depthById = clusters.ToDictionary(c => c.Id, c => Depth(c, clusterById));
        int maxDepth = depthById.Count > 0 ? depthById.Values.Max() : 0;

        for (int depth = maxDepth; depth >= 0; depth--)
        {
            var atDepth = clusters.Where(c => depthById[c.Id] == depth).ToList();
            foreach (var siblingGroup in atDepth.GroupBy(c => c.ParentClusterId ?? string.Empty))
            {
                SeparateSiblingGroup(siblingGroup.Select(c => c.Id), clusters, nodeBounds, clusterBounds, options);
            }

            // Children moved at this level — recompute all cluster bounds so
            // parents (processed next) tightly enclose the separated children.
            if (depth > 0)
            {
                var recomputed = ClusterBoundsComputer.Compute(clusters, nodeBounds, options);
                foreach (var (id, rect) in recomputed)
                    clusterBounds[id] = rect;
            }
        }
    }

    private static void SeparateSiblingGroup(
        IEnumerable<string> siblingIds,
        IReadOnlyList<LayoutCluster> clusters,
        Dictionary<string, Rect> nodeBounds,
        Dictionary<string, Rect> clusterBounds,
        LayoutOptions options)
    {
        float spacing = options.ClusterSpacing;
        var ids = siblingIds.Where(clusterBounds.ContainsKey).ToList();
        if (ids.Count < 2) return;

        // Phase 1 — asymmetric full push along the axis of least penetration:
        // each push fully resolves one pair (the later sibling moves past the
        // earlier), the outer loop resolves overlaps created by earlier
        // pushes. Good aspect behavior, but can oscillate in dense groups.
        for (int guard = 0; guard < 100; guard++)
        {
            var ordered = ids
                .OrderBy(id => clusterBounds[id].yMin)
                .ThenBy(id => clusterBounds[id].xMin)
                .ToList();
            bool changed = false;

            for (int i = 0; i < ordered.Count; i++)
            for (int j = i + 1; j < ordered.Count; j++)
            {
                var a = clusterBounds[ordered[i]];
                var b = clusterBounds[ordered[j]];

                float overlapX = System.Math.Min(a.xMax, b.xMax) - System.Math.Max(a.xMin, b.xMin) + spacing;
                float overlapY = System.Math.Min(a.yMax, b.yMax) - System.Math.Max(a.yMin, b.yMin) + spacing;
                if (overlapX <= 0 || overlapY <= 0) continue;

                if (overlapX < overlapY)
                {
                    float dir = a.X + a.Width * 0.5f <= b.X + b.Width * 0.5f ? 1f : -1f;
                    ShiftSubtree(clusters, ordered[j], new Vector2(overlapX * dir, 0f), nodeBounds, clusterBounds);
                }
                else
                {
                    float dir = a.Y + a.Height * 0.5f <= b.Y + b.Height * 0.5f ? 1f : -1f;
                    ShiftSubtree(clusters, ordered[j], new Vector2(0f, overlapY * dir), nodeBounds, clusterBounds);
                }

                changed = true;
            }

            if (!changed) return;
        }

        // Phase 2 — guaranteed-terminating cleanup for pairs that survived
        // phase 1 oscillation: monotone rightward-only pushes (x-order
        // induction: after sweep k the first k+1 rects are mutually clean,
        // so this converges in at most n sweeps).
        for (int guard = 0; guard < ids.Count + 1; guard++)
        {
            var ordered = ids.OrderBy(id => clusterBounds[id].xMin).ThenBy(id => clusterBounds[id].yMin).ToList();
            bool changed = false;

            for (int i = 0; i < ordered.Count; i++)
            for (int j = i + 1; j < ordered.Count; j++)
            {
                var a = clusterBounds[ordered[i]];
                var b = clusterBounds[ordered[j]];

                float overlapX = System.Math.Min(a.xMax, b.xMax) - System.Math.Max(a.xMin, b.xMin) + spacing;
                float overlapY = System.Math.Min(a.yMax, b.yMax) - System.Math.Max(a.yMin, b.yMin) + spacing;
                if (overlapX <= 0 || overlapY <= 0) continue;

                ShiftSubtree(clusters, ordered[j], new Vector2(overlapX, 0f), nodeBounds, clusterBounds);
                changed = true;
            }

            if (!changed) return;
        }
    }

    /// <summary>
    /// Rigidly translates a cluster subtree: all descendant nodes plus the
    /// cluster rects of the subtree (rects are recomputed per level by the
    /// caller, but shifting them here keeps them consistent between levels).
    /// </summary>
    private static void ShiftSubtree(
        IReadOnlyList<LayoutCluster> clusters,
        string clusterId,
        Vector2 delta,
        Dictionary<string, Rect> nodeBounds,
        Dictionary<string, Rect> clusterBounds)
    {
        var cluster = clusters.FirstOrDefault(c => c.Id == clusterId);
        if (cluster == null) return;

        foreach (var nodeId in cluster.NodeIds)
        {
            if (nodeBounds.TryGetValue(nodeId, out var r))
                nodeBounds[nodeId] = new Rect(r.X + delta.X, r.Y + delta.Y, r.Width, r.Height);
        }

        if (clusterBounds.TryGetValue(clusterId, out var cr))
            clusterBounds[clusterId] = new Rect(cr.X + delta.X, cr.Y + delta.Y, cr.Width, cr.Height);

        foreach (var childId in cluster.ChildClusterIds)
            ShiftSubtree(clusters, childId, delta, nodeBounds, clusterBounds);
    }

    private static int Depth(LayoutCluster cluster, Dictionary<string, LayoutCluster> all)
    {
        int depth = 0;
        var current = cluster;
        for (int guard = 0; guard < 64 && !string.IsNullOrEmpty(current.ParentClusterId); guard++)
        {
            if (!all.TryGetValue(current.ParentClusterId, out var parent)) break;
            depth++;
            current = parent;
        }
        return depth;
    }
}
