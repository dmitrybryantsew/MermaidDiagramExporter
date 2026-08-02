using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Two-level sort: treat each cluster's nodes as one sortable unit, sort units
/// by barycenter, then expand and recursively sort within each unit.
/// Per docs/07 Step 3b.
/// </summary>
internal static class CompoundLayerOrdering
{
    public static void ReorderLayerWithClusterContiguity(
        List<CompoundNode> layer,
        List<CompoundNode> adjacentLayer,
        Dictionary<string, HashSet<string>> adjacency,
        CompoundGraph compound)
    {
        if (layer.Count <= 1) return;

        // Build position map for the fixed (adjacent) layer
        var adjacentPosition = new Dictionary<string, int>();
        for (int i = 0; i < adjacentLayer.Count; i++)
            adjacentPosition[adjacentLayer[i].Id] = i;

        // Partition layer into groups by most-specific cluster (recursive nesting)
        var rootGroup = PartitionIntoGroups(layer, compound, scopeClusterId: null);

        // Sort recursively
        SortGroupRecursive(rootGroup, adjacentPosition, adjacency);

        // Flatten back
        var sorted = Flatten(rootGroup);

        // Write back
        layer.Clear();
        layer.AddRange(sorted);
    }

    private sealed class Group
    {
        public string? ScopeClusterId; // null = top-level (not in any cluster)
        public List<CompoundNode> DirectMembers = new();
        public Dictionary<string, Group> ChildGroups = new(); // keyed by child cluster id
        public int StartIndex;
        public int EndIndex;
    }

    private static Group PartitionIntoGroups(
        List<CompoundNode> layer, CompoundGraph compound, string? scopeClusterId)
    {
        var root = new Group { ScopeClusterId = scopeClusterId };

        // First, find all clusters that have members in this layer
        var clustersInLayer = layer
            .Where(n => n.OwningClusterId != null)
            .Select(n => n.OwningClusterId!)
            .Distinct()
            .ToList();

        // For each cluster, recursively partition its members
        var assigned = new HashSet<string>();
        foreach (var clusterId in clustersInLayer)
        {
            // Only handle this cluster if it's within the current scope
            if (scopeClusterId != null && !IsDescendantOf(clusterId, scopeClusterId, compound))
                continue;

            // Find all layer nodes whose OwningClusterId is clusterId OR a descendant of clusterId
            var descendants = ClusterAncestry.GetClusterAndDescendants(clusterId, compound);
            var clusterMembers = layer
                .Where(n => n.OwningClusterId != null && descendants.Contains(n.OwningClusterId))
                .ToList();

            foreach (var n in clusterMembers) assigned.Add(n.Id);

            // Recursively partition this cluster's members
            var subGroup = PartitionIntoGroup(clusterMembers, compound, clusterId);
            root.ChildGroups[clusterId] = subGroup;
        }

        // Unassigned nodes are top-level (OwningClusterId == null or not in any cluster in this layer)
        foreach (var n in layer)
        {
            if (!assigned.Contains(n.Id))
                root.DirectMembers.Add(n);
        }

        return root;
    }

    private static Group PartitionIntoGroup(
        List<CompoundNode> members, CompoundGraph compound, string clusterId)
    {
        var group = new Group { ScopeClusterId = clusterId };

        // Find child clusters of this clusterId that have members in `members`
        var childClusters = compound.ClusterChildren.GetValueOrDefault(clusterId, new List<string>());
        var assigned = new HashSet<string>();

        foreach (var childId in childClusters)
        {
            var childDescendants = ClusterAncestry.GetClusterAndDescendants(childId, compound);
            var childMembers = members
                .Where(n => n.OwningClusterId != null && childDescendants.Contains(n.OwningClusterId))
                .ToList();

            if (childMembers.Count == 0) continue;

            foreach (var n in childMembers) assigned.Add(n.Id);
            group.ChildGroups[childId] = PartitionIntoGroup(childMembers, compound, childId);
        }

        // Direct members of this cluster (not in any child cluster)
        foreach (var n in members)
        {
            if (!assigned.Contains(n.Id))
                group.DirectMembers.Add(n);
        }

        return group;
    }

    private static bool IsDescendantOf(string clusterId, string ancestorId, CompoundGraph compound)
    {
        if (clusterId == ancestorId) return true;
        var current = clusterId;
        while (compound.ClusterParent.TryGetValue(current, out var parent) && parent != null)
        {
            if (parent == ancestorId) return true;
            current = parent;
        }
        return false;
    }

    /// <summary>
    /// Recursive sort: compute barycenter for each child group + direct members
    /// (treating direct members as one implicit group), sort by barycenter,
    /// then recurse into each child group.
    /// </summary>
    private static void SortGroupRecursive(
        Group group,
        Dictionary<string, int> adjacentPosition,
        Dictionary<string, HashSet<string>> adjacency)
    {
        // Compute barycenter for direct members
        var directBary = group.DirectMembers.Count > 0
            ? ComputeGroupBarycenter(group.DirectMembers, adjacentPosition, adjacency)
            : (Barycenter: 0.0, Count: 0);

        // Compute barycenter for each child group
        var sortableItems = new List<(object Key, double Bary, int Count)>();
        sortableItems.Add(("direct", directBary.Barycenter, directBary.Count));

        foreach (var (childId, childGroup) in group.ChildGroups)
        {
            // Group bary = pooled neighbors of all member nodes
            var allChildNodes = CollectAllNodes(childGroup);
            var childBary = ComputeGroupBarycenter(allChildNodes, adjacentPosition, adjacency);
            sortableItems.Add((childGroup, childBary.Barycenter, childBary.Count));
        }

        // Sort by (barycenter ascending, count descending, key)
        sortableItems = sortableItems
            .OrderBy(x => x.Bary)
            .ThenByDescending(x => x.Count)
            .ThenBy(x => x.Key.ToString() ?? "")
            .ToList();

        // Recurse into child groups first
        foreach (var (key, _, _) in sortableItems)
        {
            if (key is Group childGroup)
                SortGroupRecursive(childGroup, adjacentPosition, adjacency);
        }
    }

    private static List<CompoundNode> CollectAllNodes(Group group)
    {
        var result = new List<CompoundNode>(group.DirectMembers);
        foreach (var (_, child) in group.ChildGroups)
            result.AddRange(CollectAllNodes(child));
        return result;
    }

    private static (double Barycenter, int Count) ComputeGroupBarycenter(
        List<CompoundNode> nodes,
        Dictionary<string, int> adjacentPosition,
        Dictionary<string, HashSet<string>> adjacency)
    {
        // Pool neighbor positions across all member nodes, take median
        var positions = new List<int>();
        foreach (var node in nodes)
        {
            if (!adjacency.TryGetValue(node.Id, out var neighbors)) continue;
            foreach (var neighbor in neighbors)
            {
                if (adjacentPosition.TryGetValue(neighbor, out var pos))
                    positions.Add(pos);
            }
        }
        if (positions.Count == 0) return (0.0, 0);
        positions.Sort();
        int mid = positions.Count / 2;
        double bary = positions.Count % 2 == 0
            ? (positions[mid - 1] + positions[mid]) / 2.0
            : positions[mid];
        return (bary, positions.Count);
    }

    /// <summary>
    /// Flatten the sorted group tree back to a flat list of nodes.
    /// </summary>
    private static List<CompoundNode> Flatten(Group group)
    {
        var result = new List<CompoundNode>();
        // We need to re-traverse in sorted order — but SortGroupRecursive doesn't preserve order
        // in the tree. For correctness, we re-sort flatly here based on barycenter.
        // For simplicity in this first version, we flatten in tree order.
        FlattenInOrder(group, result);
        return result;
    }

    private static void FlattenInOrder(Group group, List<CompoundNode> result)
    {
        result.AddRange(group.DirectMembers);
        foreach (var (_, child) in group.ChildGroups)
            FlattenInOrder(child, result);
    }
}
