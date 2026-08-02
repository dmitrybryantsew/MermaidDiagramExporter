using System.Collections.Generic;
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
    private readonly IGraphLayoutEngine _forceDirectedLayoutEngine = new ForceDirectedLayoutEngine();
    private readonly IGraphLayoutEngine _zoneFirstLayoutEngine = new ZoneFirstLayoutEngine();
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

        // MSAGL-family engines and the own force engine use a stripped-down
        // prep pipeline — the anchor/boundary passes are workarounds for the
        // custom engines and produce dummy nodes these engines don't understand.
        bool minimalPrep = resolvedOptions.Engine.UsesMinimalPrepPipeline();
        LayoutGraph preparedGraph = minimalPrep
            ? _msaglPipeline.Run(layoutGraph, resolvedOptions)
            : _pipeline.Run(layoutGraph, resolvedOptions);

        // Engine selection. The simple-column fallback is only for empty
        // real-node graphs.
        bool hasRealNodes = preparedGraph.Nodes.Any(n => n.Role == LayoutNodeRole.Real);
        LayoutResult layoutResult = resolvedOptions.Engine switch
        {
            LayoutEngineKind.Force => hasRealNodes
                ? _forceDirectedLayoutEngine.Run(preparedGraph, resolvedOptions)
                : _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions),
            LayoutEngineKind.ZoneFirst => hasRealNodes
                ? _zoneFirstLayoutEngine.Run(preparedGraph, resolvedOptions)
                : _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions),
            _ when resolvedOptions.Engine.IsMsaglFamily() => hasRealNodes
                ? _msaglLayoutEngine.Run(preparedGraph, resolvedOptions)
                : _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions),
            _ when preparedGraph.Nodes.Count == 0 => _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions),
            LayoutEngineKind.Compound => _compoundLayeredLayoutEngine.Run(preparedGraph, resolvedOptions),
            _ => _layeredLayoutEngine.Run(preparedGraph, resolvedOptions),
        };

        // The post-layout pipeline polishes cluster bounds produced by the
        // custom engines. Minimal-prep engines produce their own cluster bounds
        // (MSAGL natively / ClusterBoundsComputer), so we skip the polish
        // passes for them — they assume the custom engines' bound semantics
        // and would distort that output.
        if (!minimalPrep)
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

    /// <summary>
    /// Re-routes edges for an existing LayoutResult without re-running the
    /// layout engine. Used after manual node moves (drag) to refresh edge
    /// paths with current node positions.
    /// </summary>
    public IReadOnlyList<LayoutEdgePath> RouteEdges(Core.TypeGraph graph, LayoutResult result, LayoutOptions? options = null)
    {
        var opts = options ?? new LayoutOptions();
        return _edgeRoutingService.BuildPaths(graph, result, opts);
    }
}
