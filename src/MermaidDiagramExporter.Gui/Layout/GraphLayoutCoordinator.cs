using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — coordinator that builds LayoutGraph,
/// runs preparation passes, the layered engine, post-layout passes,
/// and edge routing to produce the final LayoutResult.
/// </summary>
public sealed class GraphLayoutCoordinator
{
    private readonly IGraphLayoutEngine _layeredLayoutEngine = new LayeredLayoutEngine();
    private readonly IGraphLayoutEngine _simpleColumnLayoutEngine = new SimpleColumnLayoutEngine();
    private readonly EdgeRoutingService _edgeRoutingService = new();
    private readonly PostLayoutPipeline _postLayoutPipeline = new PostLayoutPipeline()
        .AddPass(new ClusterTitleMarginPass())
        .AddPass(new ClusterBoundsPolishPass())
        .AddPass(new ClusterOverlapResolutionPass());

    private readonly LayoutPipeline _pipeline = new LayoutPipeline()
        .AddPass(new MeasurementPreparationPass(new LayoutMeasurementService()))
        .AddPass(new ClusterHierarchyPass())
        .AddPass(new ExternalConnectionAnalysisPass())
        .AddPass(new RepresentativeAnchorSelectionPass())
        .AddPass(new SelfLoopExpansionPass())
        .AddPass(new InternalClusterExtractionPass())
        .AddPass(new SubgraphDirectionSelectionPass())
        .AddPass(new RecursiveSpacingPass())
        .AddPass(new BoundaryEdgeNormalizationPass());

    public LayoutResult CreateLayout(Core.TypeGraph graph, LayoutOptions? options = null)
    {
        if (graph == null) return new LayoutResult();

        LayoutOptions resolvedOptions = options ?? new LayoutOptions();
        LayoutGraph layoutGraph = LayoutGraphFactory.Create(graph, resolvedOptions);
        LayoutGraph preparedGraph = _pipeline.Run(layoutGraph, resolvedOptions);

        LayoutResult layoutResult = preparedGraph.Nodes.Count == 0
            ? _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions)
            : _layeredLayoutEngine.Run(preparedGraph, resolvedOptions);

        layoutResult = _postLayoutPipeline.Run(preparedGraph, layoutResult, resolvedOptions);
        layoutResult.NodeClusterIds = preparedGraph.Nodes.ToDictionary(node => node.Id, node => node.ClusterId);
        layoutResult.ClusterVisuals = preparedGraph.Clusters.ToDictionary(
            cluster => cluster.Id,
            cluster => new LayoutClusterVisual
            {
                Id = cluster.Id,
                Label = cluster.Label,
                TitleMetrics = LayoutCloneUtility.CloneTitleMetrics(cluster.TitleMetrics)
            });
        layoutResult.EdgePaths = _edgeRoutingService.BuildPaths(graph, layoutResult, resolvedOptions);
        return layoutResult;
    }
}
