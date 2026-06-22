using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class SubgraphDirectionSelectionPass : ILayoutPass
{
    public string Name => "Subgraph Direction Selection";

    public LayoutGraph Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null)
            return new LayoutGraph();

        var clone = LayoutCloneUtility.CloneGraph(graph);
        var parentDirection = clone.Metadata?.Direction ?? options.Direction;

        clone.ExtractedSubgraphs = clone.ExtractedSubgraphs
            .Select(subgraph => ApplyDirection(subgraph, parentDirection, options))
            .ToList();

        return clone;
    }

    private static LayoutSubgraph ApplyDirection(
        LayoutSubgraph subgraph,
        LayoutDirection parentDirection,
        LayoutOptions options)
    {
        var clone = LayoutCloneUtility.CloneSubgraph(subgraph);
        clone.Direction = parentDirection == LayoutDirection.TopToBottom
            ? LayoutDirection.LeftToRight
            : LayoutDirection.TopToBottom;

        if (clone.Graph.Metadata == null)
            clone.Graph.Metadata = new LayoutGraphMetadata();

        clone.Graph.Metadata.Direction = clone.Direction;
        clone.Graph.ExtractedSubgraphs = clone.Graph.ExtractedSubgraphs
            .Select(child => ApplyDirection(child, clone.Direction, options))
            .ToList();

        return clone;
    }
}
