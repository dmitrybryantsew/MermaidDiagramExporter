using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — converts TypeGraph to LayoutGraph.
/// </summary>
internal static class LayoutGraphFactory
{
    public static LayoutGraph Create(Core.TypeGraph graph, LayoutOptions options)
    {
        var clusters = NamespaceClusterBuilder.Build(graph);
        var clusterIdByNodeId = BuildClusterMap(clusters);
        var referencedClusterIds = new HashSet<string>();

        var nodes = graph.Nodes.Select(node =>
        {
            var clusterId = ResolveClusterId(clusterIdByNodeId, node.Id);
            referencedClusterIds.Add(clusterId);
            float estimatedHeight = EstimateHeight(node.Members.Count);
            string badgeText = BuildBadgeText(node);
            var memberLines = BuildVisibleMemberLines(node).ToArray();
            return new LayoutNode
            {
                Id = node.Id,
                ClusterId = clusterId,
                Label = node.DisplayName,
                Role = LayoutNodeRole.Real,
                SourceNodeId = node.Id,
                BadgeText = badgeText,
                MemberLines = memberLines,
                Width = options.NodeWidth,
                Height = estimatedHeight
            };
        }).ToList();

        EnsureReferencedClustersExist(clusters, referencedClusterIds, nodes);

        var edges = graph.Edges.Select(edge => new LayoutEdge
        {
            Id = edge.FromNodeId + "->" + edge.ToNodeId + ":" + edge.Kind,
            FromNodeId = edge.FromNodeId,
            ToNodeId = edge.ToNodeId,
            Kind = edge.Kind,
            Role = LayoutEdgeRole.Direct
        }).ToList();

        return new LayoutGraph
        {
            Title = graph.Title,
            Nodes = nodes,
            Edges = edges,
            Clusters = clusters
        };
    }

    private static float EstimateHeight(int memberCount)
    {
        const float headerHeight = 28f;
        const float memberHeight = 16f;
        const float padding = 8f;
        return headerHeight + memberCount * memberHeight + padding * 2;
    }

    private static IEnumerable<string> BuildVisibleMemberLines(Core.TypeNodeData node)
    {
        return node.Members
            .Take(6)
            .Select(member =>
            {
                if (member.Kind == Core.TypeMemberKind.Method)
                {
                    var paramSummary = string.Join(", ", member.Parameters.Select(p => p.TypeName));
                    return member.Name + "(" + paramSummary + ") : " + member.TypeName;
                }
                return member.Name + " : " + member.TypeName;
            });
    }

    private static string BuildBadgeText(Core.TypeNodeData node)
    {
        return node.Kind switch
        {
            Core.TypeNodeKind.Interface => "Interface",
            Core.TypeNodeKind.Enum => "Enum",
            Core.TypeNodeKind.Struct => "Struct",
            Core.TypeNodeKind.StaticClass => "Static",
            Core.TypeNodeKind.AbstractClass => "Abstract",
            _ => "Class"
        };
    }

    private static string ResolveClusterId(Dictionary<string, string> map, string nodeId)
    {
        return map.TryGetValue(nodeId, out var id) ? id : "fallback";
    }

    private static Dictionary<string, string> BuildClusterMap(IEnumerable<LayoutCluster> clusters)
    {
        var map = new Dictionary<string, string>();
        foreach (var cluster in clusters)
            foreach (var nodeId in cluster.NodeIds)
                map[nodeId] = cluster.Id;
        return map;
    }

    private static void EnsureReferencedClustersExist(
        ICollection<LayoutCluster> clusters,
        IEnumerable<string> referencedIds,
        IEnumerable<LayoutNode> nodes)
    {
        var existing = new HashSet<string>(clusters.Select(c => c.Id));
        foreach (var clusterId in referencedIds)
        {
            if (existing.Contains(clusterId)) continue;
            clusters.Add(new LayoutCluster
            {
                Id = clusterId,
                Label = clusterId == "fallback" ? "Ungrouped" : clusterId,
                NodeIds = nodes.Where(n => n.ClusterId == clusterId).Select(n => n.Id).ToList()
            });
            existing.Add(clusterId);
        }
    }
}
