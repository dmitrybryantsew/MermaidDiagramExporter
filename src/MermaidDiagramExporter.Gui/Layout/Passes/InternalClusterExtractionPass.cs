using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class InternalClusterExtractionPass : ILayoutPass
{
    public string Name => "Internal Cluster Extraction";

    public LayoutGraph Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null)
            return new LayoutGraph();

        var nodes = graph.Nodes
            .Select(LayoutCloneUtility.CloneNode)
            .ToList();

        var edges = graph.Edges
            .Select(LayoutCloneUtility.CloneEdge)
            .ToList();

        var clusters = graph.Clusters
            .Select(LayoutCloneUtility.CloneCluster)
            .ToList();

        var extractedSubgraphs = new List<LayoutSubgraph>();

        foreach (var cluster in clusters)
        {
            if (!ShouldExtract(cluster, nodes, edges))
                continue;

            cluster.IsExtractedSubgraph = true;
            extractedSubgraphs.Add(ExtractCluster(cluster, nodes, edges, clusters, graph.Metadata));
        }

        return new LayoutGraph
        {
            Title = graph.Title,
            Nodes = nodes,
            Edges = edges,
            Clusters = clusters,
            ExtractedSubgraphs = extractedSubgraphs,
            Metadata = LayoutCloneUtility.CloneMetadata(graph.Metadata)
        };
    }

    private static bool ShouldExtract(
        LayoutCluster cluster,
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyList<LayoutEdge> edges)
    {
        if (cluster == null || cluster.HasExternalConnections || cluster.NodeIds.Count == 0)
            return false;

        var nodeIds = new HashSet<string>(cluster.NodeIds);
        int realNodeCount = nodes.Count(node =>
            nodeIds.Contains(node.Id)
            && (node.Role == LayoutNodeRole.Real || node.Role == LayoutNodeRole.SelfLoopHelper));

        if (realNodeCount < 2)
            return false;

        int internalEdgeCount = edges.Count(edge =>
            nodeIds.Contains(edge.FromNodeId)
            && nodeIds.Contains(edge.ToNodeId)
            && edge.FromNodeId != edge.ToNodeId);

        return internalEdgeCount > 0;
    }

    private static LayoutSubgraph ExtractCluster(
        LayoutCluster cluster,
        IReadOnlyList<LayoutNode> allNodes,
        IReadOnlyList<LayoutEdge> allEdges,
        IReadOnlyList<LayoutCluster> allClusters,
        LayoutGraphMetadata parentMetadata)
    {
        var nodeIds = new HashSet<string>(cluster.NodeIds);

        var subgraphNodes = allNodes
            .Where(node => nodeIds.Contains(node.Id))
            .Select(LayoutCloneUtility.CloneNode)
            .ToList();

        var subgraphEdges = allEdges
            .Where(edge => nodeIds.Contains(edge.FromNodeId) && nodeIds.Contains(edge.ToNodeId))
            .Select(LayoutCloneUtility.CloneEdge)
            .ToList();

        var childClusters = allClusters
            .Where(candidate => candidate.ParentClusterId == cluster.Id)
            .Select(LayoutCloneUtility.CloneCluster)
            .ToList();

        var subgraphClusters = new List<LayoutCluster>
        {
            new()
            {
                Id = cluster.Id,
                Label = cluster.Label,
                Kind = cluster.Kind,
                ParentClusterId = string.Empty,
                NodeIds = cluster.NodeIds.ToArray(),
                ChildClusterIds = childClusters.Select(candidate => candidate.Id).OrderBy(id => id).ToArray(),
                HasExternalConnections = false,
                RepresentativeNodeId = cluster.RepresentativeNodeId,
                IsExtractedSubgraph = false,
                TitleMetrics = LayoutCloneUtility.CloneTitleMetrics(cluster.TitleMetrics)
            }
        };

        subgraphClusters.AddRange(childClusters);

        return new LayoutSubgraph
        {
            ClusterId = cluster.Id,
            Direction = parentMetadata?.Direction ?? LayoutDirection.LeftToRight,
            Spacing = LayoutCloneUtility.CloneSpacing(parentMetadata?.Spacing),
            Graph = new LayoutGraph
            {
                Title = cluster.Label,
                Nodes = subgraphNodes,
                Edges = subgraphEdges,
                Clusters = subgraphClusters,
                ExtractedSubgraphs = System.Array.Empty<LayoutSubgraph>(),
                Metadata = new LayoutGraphMetadata
                {
                    SourceDescription = cluster.Label,
                    Direction = parentMetadata?.Direction ?? LayoutDirection.LeftToRight,
                    UsesMeasuredNodes = true,
                    Spacing = LayoutCloneUtility.CloneSpacing(parentMetadata?.Spacing)
                }
            }
        };
    }
}
