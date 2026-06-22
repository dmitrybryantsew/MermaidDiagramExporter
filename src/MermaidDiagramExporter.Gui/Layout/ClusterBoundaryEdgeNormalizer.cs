using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Layout;

public static class ClusterBoundaryEdgeNormalizer
{
    public static LayoutGraph Normalize(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null)
            return new LayoutGraph();

        var clusters = graph.Clusters
            .Select(LayoutCloneUtility.CloneCluster)
            .ToList();

        var clusterById = clusters.ToDictionary(cluster => cluster.Id);
        var nodes = graph.Nodes
            .Select(LayoutCloneUtility.CloneNode)
            .ToList();

        var clusterIdByNodeId = nodes.ToDictionary(node => node.Id, node => node.ClusterId);
        var normalizedEdges = new List<LayoutEdge>();
        var outboundAnchorIds = new Dictionary<string, string>();
        var inboundAnchorIds = new Dictionary<string, string>();

        foreach (var edge in graph.Edges)
        {
            if (!clusterIdByNodeId.TryGetValue(edge.FromNodeId, out var fromClusterId)
                || !clusterIdByNodeId.TryGetValue(edge.ToNodeId, out var toClusterId)
                || fromClusterId == toClusterId)
            {
                normalizedEdges.Add(LayoutCloneUtility.CloneEdge(edge));
                continue;
            }

            var outboundAnchorId = GetOrCreateAnchor(
                outboundAnchorIds,
                nodes,
                clusterById,
                fromClusterId,
                toClusterId,
                LayoutNodeRole.ClusterOutboundAnchor,
                options);

            var inboundAnchorId = GetOrCreateAnchor(
                inboundAnchorIds,
                nodes,
                clusterById,
                toClusterId,
                fromClusterId,
                LayoutNodeRole.ClusterInboundAnchor,
                options);

            var sourceBoundaryNodeId = ResolveBoundaryNodeId(clusterById, fromClusterId, edge.FromNodeId);
            var targetBoundaryNodeId = ResolveBoundaryNodeId(clusterById, toClusterId, edge.ToNodeId);

            if (sourceBoundaryNodeId != edge.FromNodeId)
            {
                normalizedEdges.Add(new LayoutEdge
                {
                    Id = edge.Id + "::representative-source",
                    OriginalEdgeId = string.IsNullOrEmpty(edge.OriginalEdgeId) ? edge.Id : edge.OriginalEdgeId,
                    FromNodeId = edge.FromNodeId,
                    ToNodeId = sourceBoundaryNodeId,
                    Kind = TypeEdgeKind.Association,
                    Role = LayoutEdgeRole.BoundarySourceLink
                });
            }

            normalizedEdges.Add(new LayoutEdge
            {
                Id = edge.Id + "::source",
                OriginalEdgeId = string.IsNullOrEmpty(edge.OriginalEdgeId) ? edge.Id : edge.OriginalEdgeId,
                FromNodeId = sourceBoundaryNodeId,
                ToNodeId = outboundAnchorId,
                Kind = TypeEdgeKind.Association,
                Role = LayoutEdgeRole.BoundarySourceLink
            });

            normalizedEdges.Add(new LayoutEdge
            {
                Id = edge.Id + "::bridge",
                OriginalEdgeId = string.IsNullOrEmpty(edge.OriginalEdgeId) ? edge.Id : edge.OriginalEdgeId,
                FromNodeId = outboundAnchorId,
                ToNodeId = inboundAnchorId,
                Kind = edge.Kind,
                Role = LayoutEdgeRole.BoundaryBridge
            });

            normalizedEdges.Add(new LayoutEdge
            {
                Id = edge.Id + "::target",
                OriginalEdgeId = string.IsNullOrEmpty(edge.OriginalEdgeId) ? edge.Id : edge.OriginalEdgeId,
                FromNodeId = inboundAnchorId,
                ToNodeId = targetBoundaryNodeId,
                Kind = TypeEdgeKind.Association,
                Role = LayoutEdgeRole.BoundaryTargetLink
            });

            if (targetBoundaryNodeId != edge.ToNodeId)
            {
                normalizedEdges.Add(new LayoutEdge
                {
                    Id = edge.Id + "::representative-target",
                    OriginalEdgeId = string.IsNullOrEmpty(edge.OriginalEdgeId) ? edge.Id : edge.OriginalEdgeId,
                    FromNodeId = targetBoundaryNodeId,
                    ToNodeId = edge.ToNodeId,
                    Kind = TypeEdgeKind.Association,
                    Role = LayoutEdgeRole.BoundaryTargetLink
                });
            }
        }

        return new LayoutGraph
        {
            Title = graph.Title,
            Nodes = nodes,
            Edges = normalizedEdges,
            Clusters = clusters,
            ExtractedSubgraphs = graph.ExtractedSubgraphs.Select(LayoutCloneUtility.CloneSubgraph).ToList(),
            Metadata = LayoutCloneUtility.CloneMetadata(graph.Metadata)
        };
    }

    private static string ResolveBoundaryNodeId(
        IReadOnlyDictionary<string, LayoutCluster> clusterById,
        string clusterId,
        string fallbackNodeId)
    {
        if (clusterById.TryGetValue(clusterId, out var cluster)
            && cluster.HasExternalConnections
            && !string.IsNullOrEmpty(cluster.RepresentativeNodeId))
        {
            return cluster.RepresentativeNodeId;
        }

        return fallbackNodeId;
    }

    private static string GetOrCreateAnchor(
        IDictionary<string, string> anchorIds,
        ICollection<LayoutNode> nodes,
        IReadOnlyDictionary<string, LayoutCluster> clusterById,
        string ownerClusterId,
        string peerClusterId,
        LayoutNodeRole role,
        LayoutOptions options)
    {
        string key = ownerClusterId + "=>" + peerClusterId + ":" + role;
        if (anchorIds.TryGetValue(key, out var existingAnchorId))
            return existingAnchorId;

        string anchorId = "anchor::" + ownerClusterId + "::" + peerClusterId + "::" + role;
        var anchorNode = new LayoutNode
        {
            Id = anchorId,
            ClusterId = ownerClusterId,
            Label = peerClusterId,
            Role = role,
            SourceNodeId = string.Empty,
            Width = options.ClusterAnchorWidth,
            Height = options.ClusterAnchorHeight,
            EstimatedWidth = options.ClusterAnchorWidth,
            EstimatedHeight = options.ClusterAnchorHeight,
            MeasuredWidth = options.ClusterAnchorWidth,
            MeasuredHeight = options.ClusterAnchorHeight,
            IsMeasured = true
        };

        nodes.Add(anchorNode);

        if (clusterById.TryGetValue(ownerClusterId, out var cluster))
        {
            cluster.NodeIds = cluster.NodeIds.Concat(new[] { anchorId }).ToArray();
        }

        anchorIds[key] = anchorId;
        return anchorId;
    }
}
