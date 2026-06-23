using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Builds a CompoundGraph from a LayoutGraph (per docs/06 Step 1-2).
/// </summary>
public static class CompoundGraphBuilder
{
    /// <summary>
    /// Builds the compound graph: real nodes, direct edges, and cluster border chains
    /// with containment edges.
    /// </summary>
    public static CompoundGraph Build(LayoutGraph graph, LayoutOptions options)
    {
        var compound = new CompoundGraph();
        var idByLayoutNodeId = new Dictionary<string, string>();

        // Step 1a: create one Real CompoundNode per LayoutNode
        foreach (var node in graph.Nodes)
        {
            var compoundId = $"real:{node.Id}";
            idByLayoutNodeId[node.Id] = compoundId;
            compound.Nodes.Add(new CompoundNode
            {
                Id = compoundId,
                Kind = CompoundNodeKind.Real,
                SourceLayoutNodeId = node.Id,
                OwningClusterId = string.IsNullOrEmpty(node.ClusterId) ? null : node.ClusterId,
                Width = node.Width > 0 ? node.Width : node.MeasuredWidth,
                Height = node.Height > 0 ? node.Height : node.MeasuredHeight,
            });
        }

        // Step 1b: copy cluster hierarchy (already computed by ClusterHierarchyPass)
        foreach (var cluster in graph.Clusters)
        {
            compound.ClusterParent[cluster.Id] =
                string.IsNullOrEmpty(cluster.ParentClusterId) ? null : cluster.ParentClusterId;
            compound.ClusterChildren[cluster.Id] = cluster.ChildClusterIds.ToList();
        }

        // Step 1c: direct edges only
        foreach (var edge in graph.Edges)
        {
            if (edge.Role != LayoutEdgeRole.Direct) continue;
            if (!idByLayoutNodeId.TryGetValue(edge.FromNodeId, out var fromId)) continue;
            if (!idByLayoutNodeId.TryGetValue(edge.ToNodeId, out var toId)) continue;
            if (fromId == toId) continue; // self-loops handled by SelfLoopExpansionPass upstream

            compound.Edges.Add(new CompoundEdge
            {
                FromId = fromId,
                ToId = toId,
                Weight = LayoutEdgeWeights.GetWeight(edge.Kind),
                MinRankSpan = 1,
                OriginalLayoutEdgeId = edge.Id,
            });
        }

        // Step 2: cluster border chains (two-pass as per docs/06 Step 2 note)
        BuildClusterBorders(compound, graph, options);
        return compound;
    }

    /// <summary>
    /// Creates border nodes for every cluster, then wires containment edges
    /// in leaf-first order (per docs/06 Step 2).
    /// </summary>
    private static void BuildClusterBorders(CompoundGraph compound, LayoutGraph graph, LayoutOptions options)
    {
        // Pass 1: create every cluster's top/bottom border node pair (any order, no edges yet)
        foreach (var cluster in graph.Clusters)
        {
            var topBorderId = $"borderTop:{cluster.Id}";
            var bottomBorderId = $"borderBottom:{cluster.Id}";
            compound.Nodes.Add(new CompoundNode
            {
                Id = topBorderId,
                Kind = CompoundNodeKind.ClusterBorderTop,
                OwningClusterId = cluster.Id,
                Width = 0,
                Height = 0
            });
            compound.Nodes.Add(new CompoundNode
            {
                Id = bottomBorderId,
                Kind = CompoundNodeKind.ClusterBorderBottom,
                OwningClusterId = cluster.Id,
                Width = 0,
                Height = 0
            });
            compound.ClusterBorders[cluster.Id] = new ClusterBorderChain { ClusterId = cluster.Id };
        }

        // Pass 2: leaf-first containment edges (per docs/06 Step 2c/2d)
        var clustersByDepthDescending = graph.Clusters
            .OrderByDescending(c => GetClusterDepth(c.Id, compound))
            .ToList();

        foreach (var cluster in clustersByDepthDescending)
        {
            var memberCompoundNodeIds = GetTransitiveMemberCompoundNodeIds(cluster.Id, compound, graph);
            if (memberCompoundNodeIds.Count == 0) continue;

            var topBorderId = $"borderTop:{cluster.Id}";
            var bottomBorderId = $"borderBottom:{cluster.Id}";

            // Containment edges: top border -> every member, every member -> bottom border
            foreach (var memberId in memberCompoundNodeIds)
            {
                compound.Edges.Add(new CompoundEdge
                {
                    FromId = topBorderId,
                    ToId = memberId,
                    Weight = options.ClusterContainmentEdgeWeight,
                    MinRankSpan = 1,
                    IsContainment = true
                });
                compound.Edges.Add(new CompoundEdge
                {
                    FromId = memberId,
                    ToId = bottomBorderId,
                    Weight = options.ClusterContainmentEdgeWeight,
                    MinRankSpan = 1,
                    IsContainment = true
                });
            }

            // Parent-nesting edges (MinRankSpan = 0 means <=/>= not strict >)
            var parentId = compound.ClusterParent.GetValueOrDefault(cluster.Id);
            if (!string.IsNullOrEmpty(parentId))
            {
                var parentTop = $"borderTop:{parentId}";
                var parentBottom = $"borderBottom:{parentId}";
                compound.Edges.Add(new CompoundEdge
                {
                    FromId = parentTop,
                    ToId = topBorderId,
                    Weight = options.ClusterContainmentEdgeWeight,
                    MinRankSpan = 0,
                    IsContainment = true
                });
                compound.Edges.Add(new CompoundEdge
                {
                    FromId = bottomBorderId,
                    ToId = parentBottom,
                    Weight = options.ClusterContainmentEdgeWeight,
                    MinRankSpan = 0,
                    IsContainment = true
                });
            }
        }
    }

    private static int GetClusterDepth(string clusterId, CompoundGraph compound)
    {
        int depth = 0;
        var current = clusterId;
        while (compound.ClusterParent.TryGetValue(current, out var parent) && parent != null)
        {
            depth++;
            current = parent;
        }
        return depth;
    }

    private static HashSet<string> GetTransitiveMemberCompoundNodeIds(
        string clusterId, CompoundGraph compound, LayoutGraph graph)
    {
        var descendants = ClusterAncestry.GetClusterAndDescendants(clusterId, compound);
        var compoundIds = new HashSet<string>();
        foreach (var node in compound.Nodes)
        {
            if (node.Kind != CompoundNodeKind.Real) continue;
            if (node.OwningClusterId != null && descendants.Contains(node.OwningClusterId))
                compoundIds.Add(node.Id);
        }
        return compoundIds;
    }
}
