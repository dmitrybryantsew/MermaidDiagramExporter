using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class SelfLoopExpansionPass : ILayoutPass
{
    public string Name => "Self Loop Expansion";

    public LayoutGraph Run(LayoutGraph graph, LayoutOptions options)
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

        var nodeById = nodes.ToDictionary(node => node.Id);
        var expandedEdges = new List<LayoutEdge>();

        foreach (var edge in graph.Edges)
        {
            if (edge.FromNodeId != edge.ToNodeId || !nodeById.TryGetValue(edge.FromNodeId, out var sourceNode))
            {
                expandedEdges.Add(LayoutCloneUtility.CloneEdge(edge));
                continue;
            }

            string helperNodeIdA = edge.FromNodeId + "::self-loop::1";
            string helperNodeIdB = edge.FromNodeId + "::self-loop::2";

            var helperNodeA = CreateHelperNode(helperNodeIdA, sourceNode, options);
            var helperNodeB = CreateHelperNode(helperNodeIdB, sourceNode, options);

            nodes.Add(helperNodeA);
            nodes.Add(helperNodeB);

            if (clusterById.TryGetValue(sourceNode.ClusterId, out var cluster))
            {
                cluster.NodeIds = cluster.NodeIds.Concat(new[] { helperNodeIdA, helperNodeIdB }).ToArray();
            }

            string originalEdgeId = string.IsNullOrEmpty(edge.OriginalEdgeId) ? edge.Id : edge.OriginalEdgeId;

            expandedEdges.Add(new LayoutEdge
            {
                Id = edge.Id + "::self-source",
                OriginalEdgeId = originalEdgeId,
                FromNodeId = edge.FromNodeId,
                ToNodeId = helperNodeIdA,
                Kind = TypeEdgeKind.Association,
                Role = LayoutEdgeRole.SelfLoopSourceLink
            });

            expandedEdges.Add(new LayoutEdge
            {
                Id = edge.Id + "::self-bridge",
                OriginalEdgeId = originalEdgeId,
                FromNodeId = helperNodeIdA,
                ToNodeId = helperNodeIdB,
                Kind = edge.Kind,
                Role = LayoutEdgeRole.SelfLoopBridge
            });

            expandedEdges.Add(new LayoutEdge
            {
                Id = edge.Id + "::self-target",
                OriginalEdgeId = originalEdgeId,
                FromNodeId = helperNodeIdB,
                ToNodeId = edge.ToNodeId,
                Kind = TypeEdgeKind.Association,
                Role = LayoutEdgeRole.SelfLoopTargetLink
            });
        }

        return new LayoutGraph
        {
            Title = graph.Title,
            Nodes = nodes,
            Edges = expandedEdges,
            Clusters = clusters,
            ExtractedSubgraphs = graph.ExtractedSubgraphs.Select(LayoutCloneUtility.CloneSubgraph).ToList(),
            Metadata = LayoutCloneUtility.CloneMetadata(graph.Metadata)
        };
    }

    private static LayoutNode CreateHelperNode(string id, LayoutNode sourceNode, LayoutOptions options)
    {
        return new LayoutNode
        {
            Id = id,
            ClusterId = sourceNode.ClusterId,
            Label = sourceNode.Label,
            Role = LayoutNodeRole.SelfLoopHelper,
            SourceNodeId = sourceNode.Id,
            Width = options.ClusterAnchorWidth,
            Height = options.ClusterAnchorHeight,
            EstimatedWidth = options.ClusterAnchorWidth,
            EstimatedHeight = options.ClusterAnchorHeight,
            MeasuredWidth = options.ClusterAnchorWidth,
            MeasuredHeight = options.ClusterAnchorHeight,
            IsMeasured = true
        };
    }
}
