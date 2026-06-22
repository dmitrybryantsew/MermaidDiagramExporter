using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class RecursiveSpacingPass : ILayoutPass
{
    public string Name => "Recursive Spacing";

    public LayoutGraph Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null)
            return new LayoutGraph();

        var clone = LayoutCloneUtility.CloneGraph(graph);
        var parentSpacing = ResolveParentSpacing(clone.Metadata, options);

        clone.ExtractedSubgraphs = clone.ExtractedSubgraphs
            .Select(subgraph => ApplySpacing(subgraph, parentSpacing, options))
            .ToList();

        return clone;
    }

    private static LayoutSubgraph ApplySpacing(
        LayoutSubgraph subgraph,
        LayoutSpacingProfile parentSpacing,
        LayoutOptions options)
    {
        var clone = LayoutCloneUtility.CloneSubgraph(subgraph);
        clone.Spacing = new LayoutSpacingProfile
        {
            NodeSeparation = parentSpacing.NodeSeparation > 0f ? parentSpacing.NodeSeparation : options.NodeSpacing,
            RankSeparation = (parentSpacing.RankSeparation > 0f ? parentSpacing.RankSeparation : options.RankSpacing)
                + options.RecursiveRankSpacingBonus,
            MarginX = parentSpacing.MarginX,
            MarginY = parentSpacing.MarginY
        };

        if (clone.Graph.Metadata == null)
            clone.Graph.Metadata = new LayoutGraphMetadata();

        clone.Graph.Metadata.Spacing = LayoutCloneUtility.CloneSpacing(clone.Spacing);
        clone.Graph.ExtractedSubgraphs = clone.Graph.ExtractedSubgraphs
            .Select(child => ApplySpacing(child, clone.Spacing, options))
            .ToList();

        return clone;
    }

    private static LayoutSpacingProfile ResolveParentSpacing(LayoutGraphMetadata metadata, LayoutOptions options)
    {
        if (metadata?.Spacing != null
            && (metadata.Spacing.NodeSeparation > 0f || metadata.Spacing.RankSeparation > 0f))
        {
            return LayoutCloneUtility.CloneSpacing(metadata.Spacing);
        }

        return new LayoutSpacingProfile
        {
            NodeSeparation = options.NodeSpacing,
            RankSeparation = options.RankSpacing,
            MarginX = options.OuterMarginX,
            MarginY = options.OuterMarginY
        };
    }
}
