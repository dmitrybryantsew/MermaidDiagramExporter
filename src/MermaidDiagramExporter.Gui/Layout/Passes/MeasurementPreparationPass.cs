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
            ExtractedSubgraphs = graph.ExtractedSubgraphs.Select(sg => CloneMeasuredSubgraph(sg, options)).ToList(),
            Metadata = LayoutCloneUtility.CloneMetadata(graph.Metadata)
        };

        measuredGraph.Metadata.UsesMeasuredNodes = true;
        return measuredGraph;
    }

    private LayoutSubgraph CloneMeasuredSubgraph(LayoutSubgraph subgraph, LayoutOptions parentOptions)
    {
        var clone = LayoutCloneUtility.CloneSubgraph(subgraph);
        clone.Graph = Run(clone.Graph, CreateSubgraphOptions(parentOptions, subgraph));
        return clone;
    }

    /// <summary>
    /// Derives per-subgraph options by cloning the parent options and
    /// overriding only what the subgraph explicitly specifies. Previously
    /// this started from defaults, silently resetting every field the
    /// subgraph didn't mention (NodeWidth, title margins, etc.).
    /// </summary>
    private static LayoutOptions CreateSubgraphOptions(LayoutOptions parent, LayoutSubgraph subgraph)
    {
        var clone = parent.Clone();
        clone.Direction = subgraph.Direction;
        if (subgraph.Spacing is { } spacing)
        {
            if (spacing.NodeSeparation > 0f) clone.NodeSpacing = spacing.NodeSeparation;
            if (spacing.RankSeparation > 0f) clone.RankSpacing = spacing.RankSeparation;
            clone.OuterMarginX = spacing.MarginX;
            clone.OuterMarginY = spacing.MarginY;
        }
        return clone;
    }
}
