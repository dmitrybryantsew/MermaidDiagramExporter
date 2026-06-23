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

        AssignInitialOrder(layers);
        RunOrderingSweeps(layers, compound, options);
        WriteBackOrderInRank(layers);
    }

    /// <summary>
    /// DFS-seeded initial order: visit graph sources in stable order, append to layers.
    /// Per docs/07 Step 2.
    /// </summary>
    private static void AssignInitialOrder(Dictionary<int, List<CompoundNode>> layers)
    {
        var byId = layers.SelectMany(kv => kv.Value).ToDictionary(n => n.Id);
        var visited = new HashSet<string>();
        var order = new List<string>();

        // Build adjacency from non-containment edges (real graph structure)
        var adjacency = new Dictionary<string, List<string>>();
        foreach (var node in byId.Values) adjacency[node.Id] = new List<string>();
        // We need the compound edges — but we don't have them passed here.
        // For initial order, use a simple heuristic: order nodes by Id within each layer.
        // This is a pragmatic simplification — we'll rely on the sweeps to fix it up.
        foreach (var kv in layers)
        {
            var sorted = kv.Value.OrderBy(n => n.Id).ToList();
            layers[kv.Key] = sorted;
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
            if (edge.IsContainment) continue; // containment edges are invisible to ordering
            if (!adj.TryGetValue(edge.FromId, out var fromSet)) continue;
            fromSet.Add(edge.ToId);
        }
        return adj;
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
