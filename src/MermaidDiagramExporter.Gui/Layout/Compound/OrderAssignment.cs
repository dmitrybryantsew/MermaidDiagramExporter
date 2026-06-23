using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Phase 2 of the compound layout engine: assigns OrderInRank to every CompoundNode
/// with cluster-contiguity constraints. Per docs/07.
/// </summary>
public static class OrderAssignment
{
    public static void Run(CompoundGraph compound, LayoutOptions options)
    {
        var layers = compound.Nodes
            .GroupBy(n => n.Rank)
            .OrderBy(g => g.Key)
            .ToDictionary(g => g.Key, g => g.ToList());

        AssignInitialOrder(layers, compound);
        RunOrderingSweeps(layers, compound, options);
        ApplyBorderFixup(layers);
        WriteBackOrderInRank(layers);
    }

    /// <summary>
    /// DFS-seeded initial order: visit graph sources (nodes with no incoming non-containment
    /// edges) in stable order, append to layers. Per docs/07 Step 2.
    /// </summary>
    private static void AssignInitialOrder(
        Dictionary<int, List<CompoundNode>> layers,
        CompoundGraph compound)
    {
        // Build adjacency from non-containment edges (real graph structure)
        var outgoing = new Dictionary<string, List<string>>();
        var incomingCount = new Dictionary<string, int>();
        foreach (var node in compound.Nodes)
        {
            outgoing[node.Id] = new List<string>();
            incomingCount[node.Id] = 0;
        }
        foreach (var edge in compound.Edges)
        {
            if (edge.IsContainment) continue;
            if (!outgoing.ContainsKey(edge.FromId)) continue;
            outgoing[edge.FromId].Add(edge.ToId);
            incomingCount[edge.ToId] = incomingCount.GetValueOrDefault(edge.ToId) + 1;
        }

        // DFS from sources (rank-0 nodes with no incoming edges), stable order by Id
        var visited = new HashSet<string>();
        var order = new List<string>();

        // Collect all sources across all layers, sorted by rank then id
        var sources = compound.Nodes
            .Where(n => incomingCount.GetValueOrDefault(n.Id, 0) == 0)
            .OrderBy(n => n.Rank)
            .ThenBy(n => n.Id)
            .Select(n => n.Id)
            .ToList();

        // If no sources found (cycle), start from rank-0 nodes
        if (sources.Count == 0)
        {
            sources = compound.Nodes
                .Where(n => n.Rank == 0)
                .OrderBy(n => n.Id)
                .Select(n => n.Id)
                .ToList();
        }

        foreach (var sourceId in sources)
        {
            if (!visited.Contains(sourceId))
                DfsVisit(sourceId, outgoing, visited, order);
        }

        // Visit any remaining unvisited nodes (shouldn't happen, but safety)
        foreach (var node in compound.Nodes.OrderBy(n => n.Rank).ThenBy(n => n.Id))
        {
            if (!visited.Contains(node.Id))
                DfsVisit(node.Id, outgoing, visited, order);
        }

        // Now assign initial order per layer: for each layer, the DFS order
        // determines the initial position. Nodes not yet in the DFS order get
        // appended at the end (stable).
        var positionInDfs = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++) positionInDfs[order[i]] = i;

        foreach (var kv in layers)
        {
            var sorted = kv.Value
                .OrderBy(n => positionInDfs.TryGetValue(n.Id, out var p) ? p : int.MaxValue)
                .ThenBy(n => n.Id)
                .ToList();
            layers[kv.Key] = sorted;
        }
    }

    private static void DfsVisit(
        string nodeId,
        Dictionary<string, List<string>> outgoing,
        HashSet<string> visited,
        List<string> order)
    {
        if (!visited.Add(nodeId)) return;
        order.Add(nodeId);
        if (outgoing.TryGetValue(nodeId, out var neighbors))
        {
            // Stable order: sort by id
            var sorted = neighbors.OrderBy(n => n).ToList();
            foreach (var neighbor in sorted)
            {
                if (!visited.Contains(neighbor))
                    DfsVisit(neighbor, outgoing, visited, order);
            }
        }
    }

    /// <summary>
    /// 4-sweep, alternating-direction, cluster-contiguity-aware ordering.
    /// Per docs/07 Step 3-4.
    /// </summary>
    private static void RunOrderingSweeps(
        Dictionary<int, List<CompoundNode>> layers,
        CompoundGraph compound,
        LayoutOptions options)
    {
        var adjacency = BuildAdjacency(compound);
        const int sweepCount = 4;

        for (int sweep = 0; sweep < sweepCount; sweep++)
        {
            bool downward = sweep % 2 == 0;
            var ranksInOrder = downward
                ? layers.Keys.OrderBy(r => r).ToList()
                : layers.Keys.OrderByDescending(r => r).ToList();

            for (int i = 0; i < ranksInOrder.Count - 1; i++)
            {
                int fixedRank = ranksInOrder[i];
                int movableRank = ranksInOrder[i + 1];
                var fixedLayer = layers[fixedRank];
                var movableLayer = layers[movableRank];

                CompoundLayerOrdering.ReorderLayerWithClusterContiguity(
                    movableLayer, fixedLayer, adjacency, compound);
            }
        }
    }

    private static Dictionary<string, HashSet<string>> BuildAdjacency(CompoundGraph compound)
    {
        var adj = new Dictionary<string, HashSet<string>>();
        foreach (var node in compound.Nodes)
            adj[node.Id] = new HashSet<string>();

        foreach (var edge in compound.Edges)
        {
            if (edge.IsContainment) continue;
            if (adj.TryGetValue(edge.FromId, out var set)) set.Add(edge.ToId);
        }
        return adj;
    }

    /// <summary>
    /// After ordering, move each cluster's border nodes to the exact ends of their
    /// contiguous block. Per docs/07 Step 5.
    /// </summary>
    private static void ApplyBorderFixup(Dictionary<int, List<CompoundNode>> layers)
    {
        foreach (var kv in layers)
        {
            var layer = kv.Value;

            // For each cluster, find its contiguous block
            var clusterPositions = new Dictionary<string, (int Min, int Max)>();
            for (int i = 0; i < layer.Count; i++)
            {
                var node = layer[i];
                if (node.OwningClusterId == null) continue;
                if (node.Kind == CompoundNodeKind.ClusterBorderTop ||
                    node.Kind == CompoundNodeKind.ClusterBorderBottom)
                    continue;

                if (clusterPositions.TryGetValue(node.OwningClusterId, out var range))
                {
                    if (i < range.Min) range = (i, range.Max);
                    if (i > range.Max) range = (range.Min, i);
                    clusterPositions[node.OwningClusterId] = range;
                }
                else
                {
                    clusterPositions[node.OwningClusterId] = (i, i);
                }
            }

            // For each cluster with a block, find its border nodes and move them to ends
            foreach (var (clusterId, (minIdx, maxIdx)) in clusterPositions)
            {
                if (minIdx == maxIdx) continue; // single-node cluster, no room for borders

                var topBorder = layer.FirstOrDefault(n =>
                    n.Kind == CompoundNodeKind.ClusterBorderTop && n.OwningClusterId == clusterId);
                var bottomBorder = layer.FirstOrDefault(n =>
                    n.Kind == CompoundNodeKind.ClusterBorderBottom && n.OwningClusterId == clusterId);

                if (topBorder != null)
                {
                    int currentIdx = layer.IndexOf(topBorder);
                    if (currentIdx != minIdx && currentIdx > minIdx && currentIdx <= maxIdx)
                    {
                        layer.RemoveAt(currentIdx);
                        layer.Insert(minIdx, topBorder);
                    }
                }
                if (bottomBorder != null)
                {
                    int currentIdx = layer.IndexOf(bottomBorder);
                    if (currentIdx != maxIdx && currentIdx >= minIdx && currentIdx < maxIdx)
                    {
                        layer.RemoveAt(currentIdx);
                        layer.Insert(maxIdx, bottomBorder);
                    }
                }
            }
        }
    }

    private static void WriteBackOrderInRank(Dictionary<int, List<CompoundNode>> layers)
    {
        foreach (var kv in layers)
        {
            for (int i = 0; i < kv.Value.Count; i++)
                kv.Value[i].OrderInRank = i;
        }
    }
}
