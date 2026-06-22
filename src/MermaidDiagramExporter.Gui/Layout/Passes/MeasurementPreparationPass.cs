using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class MeasurementPreparationPass : ILayoutPass
{
    private readonly LayoutMeasurementService _measurementService;

    public MeasurementPreparationPass(LayoutMeasurementService measurementService)
    {
        _measurementService = measurementService ?? new LayoutMeasurementService();
    }

    public string Name => "Measurement Preparation";

    public LayoutGraph Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null)
            return new LayoutGraph();

        var nodes = graph.Nodes
            .Select(LayoutCloneUtility.CloneNode)
            .ToList();

        foreach (var node in nodes)
        {
            if (node.Role == LayoutNodeRole.Real)
            {
                var measured = _measurementService.MeasureNode(node, options);
                node.MeasuredWidth = measured.X;
                node.MeasuredHeight = measured.Y;
                node.Width = measured.X;
                node.Height = measured.Y;
                node.IsMeasured = true;
            }
        }

        var clusters = graph.Clusters
            .Select(LayoutCloneUtility.CloneCluster)
            .ToList();

        foreach (var cluster in clusters)
        {
            cluster.TitleMetrics = _measurementService.MeasureClusterTitle(cluster, options);
        }

        var measuredGraph = new LayoutGraph
        {
            Title = graph.Title,
            Nodes = nodes,
            Edges = graph.Edges.Select(LayoutCloneUtility.CloneEdge).ToList(),
            Clusters = clusters,
            ExtractedSubgraphs = graph.ExtractedSubgraphs.Select(CloneMeasuredSubgraph).ToList(),
            Metadata = LayoutCloneUtility.CloneMetadata(graph.Metadata)
        };

        measuredGraph.Metadata.UsesMeasuredNodes = true;
        return measuredGraph;
    }

    private LayoutSubgraph CloneMeasuredSubgraph(LayoutSubgraph subgraph)
    {
        var clone = LayoutCloneUtility.CloneSubgraph(subgraph);
        clone.Graph = Run(clone.Graph, CreateSubgraphOptions(subgraph));
        return clone;
    }

    private static LayoutOptions CreateSubgraphOptions(LayoutSubgraph subgraph)
    {
        return new LayoutOptions
        {
            Direction = subgraph.Direction,
            NodeSpacing = subgraph.Spacing != null && subgraph.Spacing.NodeSeparation > 0f
                ? subgraph.Spacing.NodeSeparation
                : new LayoutOptions().NodeSpacing,
            RankSpacing = subgraph.Spacing != null && subgraph.Spacing.RankSeparation > 0f
                ? subgraph.Spacing.RankSeparation
                : new LayoutOptions().RankSpacing,
            OuterMarginX = subgraph.Spacing?.MarginX ?? 0f,
            OuterMarginY = subgraph.Spacing?.MarginY ?? 0f
        };
    }
}
