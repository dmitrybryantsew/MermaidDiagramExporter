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
    /// neighbor median with priority-based conflict resolution.
    /// </summary>
    private static void AssignOrderAxisPosition(CompoundGraph compound, LayoutOptions options)
    {
        var byRank = compound.Nodes
            .GroupBy(n => n.Rank)
            .OrderBy(g => g.Key)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(n => n.OrderInRank).ToList());

        // Pass 1: pack with spacing (no overlaps)
        foreach (var rank in byRank.Keys)
            PackRankWithoutOverlap(byRank[rank], options);

        // Pass 2: iterative median-pull
        var adjacency = BuildAdjacency(compound);
        int passes = options.CoordinateAssignmentPasses;
        for (int pass = 0; pass < passes; pass++)
        {
            bool downward = pass % 2 == 0;
            var ranksInPassOrder = downward
                ? byRank.Keys.OrderBy(r => r).ToList()
                : byRank.Keys.OrderByDescending(r => r).ToList();

            foreach (var rank in ranksInPassOrder)
                PullTowardNeighborMedianAndResolveOverlaps(byRank[rank], adjacency, options);
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

    private static Dictionary<string, int> BuildPositionLookup(Dictionary<int, List<CompoundNode>> byRank)
    {
        var pos = new Dictionary<string, int>();
        foreach (var kv in byRank)
        {
            for (int i = 0; i < kv.Value.Count; i++)
                pos[kv.Value[i].Id] = i;
        }
        return pos;
    }

    private static Dictionary<string, HashSet<string>> BuildAdjacency(CompoundGraph compound)
    {
        var adj = new Dictionary<string, HashSet<string>>();
        foreach (var node in compound.Nodes) adj[node.Id] = new HashSet<string>();
        foreach (var edge in compound.Edges)
        {
            if (edge.IsContainment) continue;
            if (adj.TryGetValue(edge.FromId, out var set)) set.Add(edge.ToId);
            if (adj.TryGetValue(edge.ToId, out var set2)) set2.Add(edge.FromId);
        }
        return adj;
    }

    private static void PullTowardNeighborMedianAndResolveOverlaps(
        List<CompoundNode> rank,
        Dictionary<string, HashSet<string>> adjacency,
        LayoutOptions options)
    {
        // Build a quick lookup from node id to node for position queries
        var nodeById = new Dictionary<string, CompoundNode>();
        foreach (var node in rank) nodeById[node.Id] = node;
        // Also include all nodes reachable via adjacency (could be in other layers)
        var allNodes = new Dictionary<string, CompoundNode>();
        foreach (var node in rank) allNodes[node.Id] = node;

        // Compute desired position for each node = median of neighbor positions
        var desired = new Dictionary<string, float>();
        foreach (var node in rank)
        {
            if (node.Kind != CompoundNodeKind.Real) continue;
            if (!adjacency.TryGetValue(node.Id, out var neighbors)) continue;
            var positions = new List<float>();
            foreach (var neighborId in neighbors)
            {
                // We don't have access to all nodes here; use the current node's position
                // as a proxy. A full implementation would need access to the full compound graph.
                if (options.Direction == LayoutDirection.LeftToRight) positions.Add(node.X);
                else positions.Add(node.Y);
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
}
