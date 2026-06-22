using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class RepresentativeAnchorSelectionPass : ILayoutPass
{
    public string Name => "Representative Anchor Selection";

    public LayoutGraph Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null)
            return new LayoutGraph();

        var nodes = graph.Nodes
            .Select(LayoutCloneUtility.CloneNode)
            .ToList();

        var nodeById = nodes.ToDictionary(node => node.Id);
        var connectivityByNodeId = BuildConnectivityMap(graph);

        var clusters = graph.Clusters
            .Select(cluster =>
            {
                var clone = LayoutCloneUtility.CloneCluster(cluster);
                clone.RepresentativeNodeId = SelectRepresentativeNode(clone, nodeById, connectivityByNodeId);
                return clone;
            })
            .ToList();

        return new LayoutGraph
        {
            Title = graph.Title,
            Nodes = nodes,
            Edges = graph.Edges.Select(LayoutCloneUtility.CloneEdge).ToList(),
            Clusters = clusters,
            ExtractedSubgraphs = graph.ExtractedSubgraphs.Select(LayoutCloneUtility.CloneSubgraph).ToList(),
            Metadata = LayoutCloneUtility.CloneMetadata(graph.Metadata)
        };
    }

    private static Dictionary<string, int> BuildConnectivityMap(LayoutGraph graph)
    {
        var connectivityByNodeId = new Dictionary<string, int>();
        foreach (var node in graph.Nodes)
        {
            connectivityByNodeId[node.Id] = 0;
        }

        foreach (var edge in graph.Edges)
        {
            if (connectivityByNodeId.ContainsKey(edge.FromNodeId))
                connectivityByNodeId[edge.FromNodeId]++;

            if (connectivityByNodeId.ContainsKey(edge.ToNodeId))
                connectivityByNodeId[edge.ToNodeId]++;
        }

        return connectivityByNodeId;
    }

    private static string SelectRepresentativeNode(
        LayoutCluster cluster,
        IReadOnlyDictionary<string, LayoutNode> nodeById,
        IReadOnlyDictionary<string, int> connectivityByNodeId)
    {
        return cluster.NodeIds
            .Select(nodeId => nodeById.TryGetValue(nodeId, out var node) ? node : null)
            .Where(node => node != null && node.Role == LayoutNodeRole.Real)
            .OrderByDescending(node => connectivityByNodeId.TryGetValue(node!.Id, out var connectivity) ? connectivity : 0)
            .ThenByDescending(node => node!.MemberLines.Count)
            .ThenBy(node => node!.Label)
            .ThenBy(node => node!.Id)
            .Select(node => node!.Id)
            .FirstOrDefault() ?? string.Empty;
    }
}
