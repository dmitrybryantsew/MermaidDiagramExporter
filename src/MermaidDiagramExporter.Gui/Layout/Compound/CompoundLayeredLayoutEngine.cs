namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// New compound-aware layout engine that ranks/orders/positions all real nodes +
/// cluster borders + edge-segment dummies in one unified pass.
/// Per docs/05 §5.3, docs/08 Part C.
/// </summary>
public sealed class CompoundLayeredLayoutEngine : IGraphLayoutEngine
{
    public LayoutResult Run(LayoutGraph graph, LayoutOptions options)
    {
        var compound = CompoundGraphBuilder.Build(graph, options);
        RankAssignment.Run(compound, options);
        OrderAssignment.Run(compound, options);
        CoordinateAssignment.Run(compound, options);
        return CompoundResultProjector.Project(compound, graph, options);
    }
}
