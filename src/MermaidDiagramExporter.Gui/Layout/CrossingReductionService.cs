using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — reduces edge crossings via median/barycenter heuristic
/// with transpose pass. Iterates sweeps up to 4 times.
/// </summary>
internal sealed class CrossingReductionService
{
    private const int DefaultSweepCount = 4;

    public void RefineRows(IList<List<LayoutNode>> rows, LayoutGraph graph, int sweepCount = DefaultSweepCount)
    {
        if (rows == null || graph == null || rows.Count <= 1) return;

        var includedIds = new HashSet<string>(rows.SelectMany(r => r.Select(n => n.Id)));
        var adjacency = BuildAdjacency(graph, includedIds);
        if (adjacency.Values.All(n => n.Count == 0)) return;

        for (int sweep = 0; sweep < sweepCount; sweep++)
        {
            for (int i = 1; i < rows.Count; i++)
                rows[i] = ReorderRow(rows[i], rows[i - 1], adjacency);

            for (int i = rows.Count - 2; i >= 0; i--)
                rows[i] = ReorderRow(rows[i], rows[i + 1], adjacency);

            ApplyTransposePass(rows, adjacency);
        }
    }

    private static Dictionary<string, HashSet<string>> BuildAdjacency(LayoutGraph graph, IReadOnlyCollection<string> ids)
    {
        var adj = ids.ToDictionary(id => id, _ => new HashSet<string>());
        foreach (var edge in graph.Edges)
        {
            if (!IsOrderingEdge(edge) || edge.FromNodeId == edge.ToNodeId) continue;
            if (!adj.ContainsKey(edge.FromNodeId) || !adj.ContainsKey(edge.ToNodeId)) continue;
            adj[edge.FromNodeId].Add(edge.ToNodeId);
            adj[edge.ToNodeId].Add(edge.FromNodeId);
        }
        return adj;
    }

    private static bool IsOrderingEdge(LayoutEdge edge) => edge.Role switch
    {
        LayoutEdgeRole.Direct or LayoutEdgeRole.SelfLoopSourceLink or LayoutEdgeRole.SelfLoopBridge or LayoutEdgeRole.SelfLoopTargetLink => true,
        _ => false
    };

    private static List<LayoutNode> ReorderRow(IReadOnlyList<LayoutNode> row, IReadOnlyList<LayoutNode> adjacent, IReadOnlyDictionary<string, HashSet<string>> adj)
    {
        if (row.Count <= 1 || adjacent.Count == 0) return row.ToList();

        var order = BuildOrderIndex(adjacent);
        return row
            .Select((node, i) => BuildMetric(node, i, order, adj))
            .OrderBy(m => m.NeighborCount == 0 ? 1 : 0)
            .ThenBy(m => m.Median)
            .ThenBy(m => m.Barycenter)
            .ThenByDescending(m => m.NeighborCount)
            .ThenBy(m => m.ExistingIndex)
            .ThenBy(m => m.Node.Label)
            .ThenBy(m => m.Node.Id)
            .Select(m => m.Node)
            .ToList();
    }

    private static NodeOrderMetric BuildMetric(LayoutNode node, int idx, Dictionary<string, int> order, IReadOnlyDictionary<string, HashSet<string>> adj)
    {
        var orders = adj.TryGetValue(node.Id, out var neighbors)
            ? neighbors.Where(order.ContainsKey).Select(id => order[id]).OrderBy(v => v).ToList()
            : new List<int>();

        return new NodeOrderMetric
        {
            Node = node,
            ExistingIndex = idx,
            NeighborCount = orders.Count,
            Median = orders.Count > 0 ? ComputeMedian(orders) : idx,
            Barycenter = orders.Count > 0 ? orders.Average() : idx
        };
    }

    private static double ComputeMedian(IReadOnlyList<int> sorted)
    {
        if (sorted.Count == 0) return 0;
        int mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) * 0.5;
    }

    private static Dictionary<string, int> BuildOrderIndex(IReadOnlyList<LayoutNode> row)
    {
        var order = new Dictionary<string, int>();
        for (int i = 0; i < row.Count; i++) order[row[i].Id] = i;
        return order;
    }

    private static void ApplyTransposePass(IList<List<LayoutNode>> rows, IReadOnlyDictionary<string, HashSet<string>> adj)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Count <= 1) continue;

            var prev = i > 0 ? (IReadOnlyList<LayoutNode>)rows[i - 1] : new List<LayoutNode>();
            var next = i < rows.Count - 1 ? (IReadOnlyList<LayoutNode>)rows[i + 1] : new List<LayoutNode>();

            bool changed;
            do
            {
                changed = false;
                for (int j = 0; j < row.Count - 1; j++)
                {
                    var a = row[j];
                    var b = row[j + 1];
                    int current = CountPairCrossings(a.Id, b.Id, prev, adj) + CountPairCrossings(a.Id, b.Id, next, adj);
                    int swapped = CountPairCrossings(b.Id, a.Id, prev, adj) + CountPairCrossings(b.Id, a.Id, next, adj);
                    if (swapped >= current) continue;
                    row[j] = b;
                    row[j + 1] = a;
                    changed = true;
                }
            } while (changed);
        }
    }

    private static int CountPairCrossings(string first, string second, IReadOnlyList<LayoutNode> adjacent, IReadOnlyDictionary<string, HashSet<string>> adj)
    {
        if (adjacent.Count == 0) return 0;
        var order = BuildOrderIndex(adjacent);
        var firstOrders = GetAdjacentOrders(first, order, adj);
        var secondOrders = GetAdjacentOrders(second, order, adj);
        if (firstOrders.Count == 0 || secondOrders.Count == 0) return 0;

        int crossings = 0;
        foreach (var f in firstOrders)
            foreach (var s in secondOrders)
                if (f > s) crossings++;
        return crossings;
    }

    private static List<int> GetAdjacentOrders(string nodeId, Dictionary<string, int> order, IReadOnlyDictionary<string, HashSet<string>> adj)
    {
        if (!adj.TryGetValue(nodeId, out var neighbors)) return new List<int>();
        return neighbors.Where(order.ContainsKey).Select(id => order[id]).OrderBy(v => v).ToList();
    }

    private sealed class NodeOrderMetric
    {
        public LayoutNode Node = null!;
        public int ExistingIndex;
        public int NeighborCount;
        public double Median;
        public double Barycenter;
    }
}
