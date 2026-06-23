using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Cluster ancestry queries for the compound layout engine.
/// Pure utility with no dependency on ordering having happened yet.
/// </summary>
internal static class ClusterAncestry
{
    /// <summary>
    /// Returns the chain of cluster ids from `clusterId` up to (but not including) null,
    /// e.g. [Inner, Middle, Outer] for a 3-level nesting.
    /// </summary>
    public static List<string?> GetAncestorChainInclusive(string? clusterId, CompoundGraph compound)
    {
        var chain = new List<string?>();
        var current = clusterId;
        while (current != null)
        {
            chain.Add(current);
            compound.ClusterParent.TryGetValue(current, out var parent);
            current = parent;
        }
        return chain;
    }

    /// <summary>
    /// Returns the lowest common ancestor of two clusters (or null if they share
    /// no common ancestor, i.e. one is top-level and the other is in an unrelated tree).
    /// If both ids are the same (including both null), returns that id.
    /// </summary>
    public static string? FindLowestCommonAncestor(
        string? clusterIdA, string? clusterIdB, CompoundGraph compound)
    {
        if (clusterIdA == clusterIdB) return clusterIdA;

        var ancestorsOfA = GetAncestorChainInclusive(clusterIdA, compound);
        var ancestorsOfB = GetAncestorChainInclusive(clusterIdB, compound);
        var setOfB = new HashSet<string?>(ancestorsOfB);

        // Walk from most-specific (self) up; first match is the LCA
        foreach (var ancestor in ancestorsOfA)
        {
            if (setOfB.Contains(ancestor))
                return ancestor;
        }
        return null;
    }

    /// <summary>
    /// Returns the set of cluster ids that are `clusterId` or any descendant of it
    /// (transitively), including `clusterId` itself.
    /// </summary>
    public static HashSet<string> GetClusterAndDescendants(string clusterId, CompoundGraph compound)
    {
        var result = new HashSet<string> { clusterId };
        var queue = new Queue<string>();
        queue.Enqueue(clusterId);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (compound.ClusterChildren.TryGetValue(current, out var children))
            {
                foreach (var child in children)
                {
                    if (result.Add(child))
                        queue.Enqueue(child);
                }
            }
        }
        return result;
    }
}
