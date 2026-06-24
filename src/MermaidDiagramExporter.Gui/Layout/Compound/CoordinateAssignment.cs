using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Phase 3 of the compound layout engine: assigns X/Y coordinates to every
/// CompoundNode. Simplified priority/median single-pass approach (not full BK).
/// Per docs/08 Part A.
/// </summary>
public static class CoordinateAssignment
{
    public static void Run(CompoundGraph compound, LayoutOptions options)
    {
        AssignRankAxisPosition(compound, options);
        AssignOrderAxisPosition(compound, options);
    }

    /// <summary>
    /// Rank axis (perpendicular to reading direction): cumulative position
    /// based on max node size at each rank + spacing.
    /// </summary>
    private static void AssignRankAxisPosition(CompoundGraph compound, LayoutOptions options)
    {
        var maxSizeByRank = compound.Nodes
            .GroupBy(n => n.Rank)
            .ToDictionary(
                g => g.Key,
                g => g.Max(n => options.Direction == LayoutDirection.LeftToRight ? n.Width : n.Height));

        float cursor = options.Direction == LayoutDirection.LeftToRight
            ? options.OuterMarginY
            : options.OuterMarginX;

        foreach (var rank in maxSizeByRank.Keys.OrderBy(r => r))
        {
            foreach (var node in compound.Nodes.Where(n => n.Rank == rank))
            {
                if (options.Direction == LayoutDirection.LeftToRight) node.Y = cursor;
                else node.X = cursor;
            }
            cursor += maxSizeByRank[rank] + options.RankSpacing;
        }
    }

    /// <summary>
    /// Order axis: pack each rank with spacing, then iteratively pull toward
    /// neighbor median with priority-based conflict resolution. Border nodes
    /// are NOT pulled — they're repositioned after each pass to sit at the
    /// exact ends of their cluster's contiguous member block (same approach
    /// OrderAssignment.ApplyBorderFixup uses for ordering).
    /// </summary>
    private static void AssignOrderAxisPosition(CompoundGraph compound, LayoutOptions options)
    {
        // Build full position lookup from all compound nodes
        var nodeById = compound.Nodes.ToDictionary(n => n.Id);

        var byRank = compound.Nodes
            .GroupBy(n => n.Rank)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(n => n.OrderInRank).ToList());

        // Pass 1: pack with spacing (no overlaps)
        foreach (var rank in byRank.Keys)
            PackRankWithoutOverlap(byRank[rank], options);

        // Pass 2: iterative median-pull using UNDIRECTED neighbor positions
        // (a node pulls toward both its predecessors and its successors — this
        // fixes the "sink-like nodes never move" bug where nodes with only
        // incoming edges got desired.Count == 0)
        var adjacency = BuildUndirectedAdjacency(compound);
        int passes = options.CoordinateAssignmentPasses;
        for (int pass = 0; pass < passes; pass++)
        {
            bool downward = pass % 2 == 0;
            var ranksInPassOrder = downward
                ? byRank.Keys.OrderBy(r => r).ToList()
                : byRank.Keys.OrderByDescending(r => r).ToList();

            foreach (var rank in ranksInPassOrder)
                PullTowardNeighborMedianAndResolveOverlaps(byRank[rank], adjacency, nodeById, options);

            // After each pass settles, reposition border nodes to the exact ends
            // of their cluster's contiguous member block at this rank. This keeps
            // the border geometrically meaningful (it tracks the members) without
            // participating in the pull itself.
            RepositionBorderNodes(byRank, nodeById, options);
        }
    }

    private static void PackRankWithoutOverlap(List<CompoundNode> rank, LayoutOptions options)
    {
        float cursor = options.Direction == LayoutDirection.LeftToRight
            ? options.OuterMarginX
            : options.OuterMarginY;
        foreach (var node in rank)
        {
            // Border and EdgeSegment nodes have zero size — don't advance cursor for them
            float size = options.Direction == LayoutDirection.LeftToRight ? node.Width : node.Height;
            if (options.Direction == LayoutDirection.LeftToRight) node.X = cursor;
            else node.Y = cursor;
            if (size > 0) cursor += size + options.NodeSpacing;
        }
    }

    /// <summary>
    /// Builds UNDIRECTED adjacency: for every non-containment edge, both endpoints
    /// see each other as neighbors. This ensures nodes with only incoming edges
    /// (e.g. base classes with many inbound inheritance edges) still participate
    /// in the median-pull phase.
    /// </summary>
    private static Dictionary<string, HashSet<string>> BuildUndirectedAdjacency(CompoundGraph compound)
    {
        var adj = new Dictionary<string, HashSet<string>>();
        foreach (var node in compound.Nodes) adj[node.Id] = new HashSet<string>();
        foreach (var edge in compound.Edges)
        {
            if (edge.IsContainment) continue;
            if (adj.TryGetValue(edge.FromId, out var fromSet)) fromSet.Add(edge.ToId);
            if (adj.TryGetValue(edge.ToId, out var toSet)) toSet.Add(edge.FromId);
        }
        return adj;
    }

    /// <summary>
    /// Pulls every Real node in the rank toward the median position of its
    /// (undirected) neighbors, then resolves overlaps in priority order
    /// (highest neighbor-count first). Border and edge-segment nodes are
    /// excluded from the pull — they're repositioned separately.
    /// </summary>
    private static void PullTowardNeighborMedianAndResolveOverlaps(
        List<CompoundNode> rank,
        Dictionary<string, HashSet<string>> adjacency,
        Dictionary<string, CompoundNode> nodeById,
        LayoutOptions options)
    {
        // Compute desired position for each REAL node = median of neighbor positions
        var desired = new Dictionary<string, float>();
        foreach (var node in rank)
        {
            if (node.Kind != CompoundNodeKind.Real) continue;
            if (!adjacency.TryGetValue(node.Id, out var neighbors)) continue;
            var positions = new List<float>();
            foreach (var neighborId in neighbors)
            {
                if (!nodeById.TryGetValue(neighborId, out var neighbor)) continue;
                float pos = options.Direction == LayoutDirection.LeftToRight ? neighbor.X : neighbor.Y;
                positions.Add(pos);
            }
            if (positions.Count == 0) continue;
            positions.Sort();
            int mid = positions.Count / 2;
            desired[node.Id] = positions.Count % 2 == 0
                ? (positions[mid - 1] + positions[mid]) / 2f
                : positions[mid];
        }

        // Sort by priority (neighbor count descending) for conflict resolution
        var sorted = rank
            .Where(n => desired.ContainsKey(n.Id))
            .OrderByDescending(n => adjacency[n.Id].Count)
            .ToList();

        // Apply desired positions, resolving overlaps
        float minGap = options.NodeSpacing;
        float lastEnd = float.NegativeInfinity;
        foreach (var node in sorted)
        {
            float desiredPos = desired[node.Id];
            float size = options.Direction == LayoutDirection.LeftToRight ? node.Width : node.Height;
            float start = System.Math.Max(desiredPos, lastEnd + minGap);
            if (options.Direction == LayoutDirection.LeftToRight) node.X = start;
            else node.Y = start;
            lastEnd = start + size;
        }
    }

    /// <summary>
    /// After each pull pass settles, reposition border nodes to the exact ends of
    /// their cluster's contiguous member block at each rank. This keeps the border
    /// geometrically meaningful (tracks the members) without participating in the
    /// pull itself — same approach OrderAssignment.ApplyBorderFixup uses for ordering.
    /// </summary>
    private static void RepositionBorderNodes(
        Dictionary<int, List<CompoundNode>> byRank,
        Dictionary<string, CompoundNode> nodeById,
        LayoutOptions options)
    {
        foreach (var kv in byRank)
        {
            var layer = kv.Value;

            // For each cluster, find its contiguous block of REAL member nodes
            // (excluding border nodes themselves — they're what we're placing)
            var clusterBlocks = new Dictionary<string, (int MinIdx, int MaxIdx)>();
            for (int i = 0; i < layer.Count; i++)
            {
                var node = layer[i];
                if (node.OwningClusterId == null) continue;
                if (node.Kind != CompoundNodeKind.Real) continue;

                if (clusterBlocks.TryGetValue(node.OwningClusterId, out var range))
                {
                    if (i < range.MinIdx) range = (i, range.MaxIdx);
                    if (i > range.MaxIdx) range = (range.MinIdx, i);
                    clusterBlocks[node.OwningClusterId] = range;
                }
                else
                {
                    clusterBlocks[node.OwningClusterId] = (i, i);
                }
            }

            // For each cluster with a block, position its border nodes at the
            // exact ends of that block (or as close as possible — if the block
            // is empty at this rank, leave the border at its current position).
            foreach (var (clusterId, (minIdx, maxIdx)) in clusterBlocks)
            {
                if (minIdx == maxIdx) continue; // single-node block, no room for borders

                // Find the leftmost and rightmost member's order-axis position
                var minNode = layer[minIdx];
                var maxNode = layer[maxIdx];
                float leftPos = options.Direction == LayoutDirection.LeftToRight
                    ? minNode.X : minNode.Y;
                float rightPos = options.Direction == LayoutDirection.LeftToRight
                    ? maxNode.X + maxNode.Width : maxNode.Y + maxNode.Height;

                // Find and reposition border nodes for this cluster at this rank
                var topBorder = layer.FirstOrDefault(n =>
                    n.Kind == CompoundNodeKind.ClusterBorderTop && n.OwningClusterId == clusterId);
                var bottomBorder = layer.FirstOrDefault(n =>
                    n.Kind == CompoundNodeKind.ClusterBorderBottom && n.OwningClusterId == clusterId);

                if (topBorder != null)
                {
                    // Place top border just to the left of the leftmost member
                    if (options.Direction == LayoutDirection.LeftToRight)
                        topBorder.X = leftPos - options.NodeSpacing;
                    else
                        topBorder.Y = leftPos - options.NodeSpacing;
                }
                if (bottomBorder != null)
                {
                    // Place bottom border just to the right of the rightmost member
                    if (options.Direction == LayoutDirection.LeftToRight)
                        bottomBorder.X = rightPos + options.NodeSpacing;
                    else
                        bottomBorder.Y = rightPos + options.NodeSpacing;
                }
            }
        }
    }
}
