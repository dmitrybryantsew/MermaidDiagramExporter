using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Brute-force crossing counter for testing purposes (O(E²) — fine for small fixtures).
/// Per docs/09 §1.3 validation checklist.
/// </summary>
internal static class CrossingCounter
{
    /// <summary>
    /// Counts the total number of edge crossings in the final per-layer orders.
    /// Two edges cross if their endpoints are in the same rank and their order
    /// indices are interleaved.
    /// </summary>
    public static int CountCrossings(
        Dictionary<int, List<CompoundNode>> layers,
        CompoundGraph compound)
    {
        // Build adjacency for quick neighbor lookup
        var adjacency = new Dictionary<string, HashSet<string>>();
        foreach (var node in compound.Nodes)
            adjacency[node.Id] = new HashSet<string>();
        foreach (var edge in compound.Edges)
        {
            if (edge.IsContainment) continue;
            if (adjacency.TryGetValue(edge.FromId, out var set)) set.Add(edge.ToId);
        }

        // Build position lookup per rank
        var positionByRank = new Dictionary<int, Dictionary<string, int>>();
        foreach (var kv in layers)
        {
            var pos = new Dictionary<string, int>();
            for (int i = 0; i < kv.Value.Count; i++)
                pos[kv.Value[i].Id] = i;
            positionByRank[kv.Key] = pos;
        }

        int crossings = 0;
        var ranks = layers.Keys.OrderBy(r => r).ToList();

        // For each pair of adjacent ranks, check all edge pairs that connect them
        for (int r = 0; r < ranks.Count - 1; r++)
        {
            int rankA = ranks[r];
            int rankB = ranks[r + 1];
            if (!positionByRank.TryGetValue(rankA, out var posA)) continue;
            if (!positionByRank.TryGetValue(rankB, out var posB)) continue;

            // Collect all edges from rankA to rankB
            var edgesAB = new List<(int FromPos, int ToPos)>();
            foreach (var nodeA in layers[rankA])
            {
                if (!adjacency.TryGetValue(nodeA.Id, out var neighbors)) continue;
                foreach (var neighborId in neighbors)
                {
                    if (posB.TryGetValue(neighborId, out var toPos))
                        edgesAB.Add((posA[nodeA.Id], toPos));
                }
            }

            // Count crossings between edge pairs (O(E²))
            for (int i = 0; i < edgesAB.Count; i++)
            {
                for (int j = i + 1; j < edgesAB.Count; j++)
                {
                    var e1 = edgesAB[i];
                    var e2 = edgesAB[j];
                    // Two edges cross if their endpoints are interleaved
                    if ((e1.FromPos < e2.FromPos && e1.ToPos > e2.ToPos) ||
                        (e1.FromPos > e2.FromPos && e1.ToPos < e2.ToPos))
                    {
                        crossings++;
                    }
                }
            }
        }

        return crossings;
    }
}
