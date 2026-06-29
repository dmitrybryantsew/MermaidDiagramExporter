using System.Linq;
using MermaidDiagramExporter.Gui.Layout.Compound;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — coordinator that builds LayoutGraph,
/// runs preparation passes, the layered engine, post-layout passes,
/// and edge routing to produce the final LayoutResult.
/// </summary>
public sealed class GraphLayoutCoordinator
{
    private readonly IGraphLayoutEngine _layeredLayoutEngine = new LayeredLayoutEngine();
    private readonly IGraphLayoutEngine _compoundLayeredLayoutEngine = new CompoundLayeredLayoutEngine();
    private readonly IGraphLayoutEngine _simpleColumnLayoutEngine = new SimpleColumnLayoutEngine();
    private readonly IGraphLayoutEngine _msaglLayoutEngine = new MsaglLayoutEngine();
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

    // Minimal pipeline for the MSAGL engine: MSAGL handles cluster routing,
    // self-loops, and ordering natively, so the anchor/boundary passes are
    // unnecessary. We still need measured node sizes and the cluster hierarchy.
    private readonly LayoutPipeline _msaglPipeline = new LayoutPipeline()
        .AddPass(new MeasurementPreparationPass(new LayoutMeasurementService()))
        .AddPass(new ClusterHierarchyPass());

    public LayoutResult CreateLayout(Core.TypeGraph graph, LayoutOptions? options = null)
    {
        if (graph == null) return new LayoutResult();

        LayoutOptions resolvedOptions = options ?? new LayoutOptions();
        LayoutGraph layoutGraph = LayoutGraphFactory.Create(graph, resolvedOptions);

        // MSAGL engine uses a stripped-down prep pipeline — the anchor/boundary
        // passes are workarounds for the custom engines and produce dummy nodes
        // that MSAGL doesn't understand.
        LayoutGraph preparedGraph = resolvedOptions.UseMsaglEngine
            ? _msaglPipeline.Run(layoutGraph, resolvedOptions)
            : _pipeline.Run(layoutGraph, resolvedOptions);

        // Engine selection. MSAGL takes precedence over the compound flag.
        // The simple-column fallback is only for empty real-node graphs.
        LayoutResult layoutResult;
        if (resolvedOptions.UseMsaglEngine)
        {
            layoutResult = preparedGraph.Nodes.Any(n => n.Role == LayoutNodeRole.Real)
                ? _msaglLayoutEngine.Run(preparedGraph, resolvedOptions)
                : _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions);
        }
        else
        {
            layoutResult = preparedGraph.Nodes.Count == 0
                ? _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions)
                : resolvedOptions.UseCompoundLayoutEngine
                    ? _compoundLayeredLayoutEngine.Run(preparedGraph, resolvedOptions)
                    : _layeredLayoutEngine.Run(preparedGraph, resolvedOptions);
        }

        // The post-layout pipeline polishes cluster bounds produced by the
        // custom engines. MSAGL produces its own cluster bounds (including
        // padding), so we skip the polish passes for it — they assume the
        // custom engines' bound semantics and would distort MSAGL output.
        if (!resolvedOptions.UseMsaglEngine)
        {
            layoutResult = _postLayoutPipeline.Run(preparedGraph, layoutResult, resolvedOptions);
        }

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
