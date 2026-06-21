using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — coordinator that builds LayoutGraph,
/// runs the layered engine, and returns final LayoutResult.
/// </summary>
internal sealed class GraphLayoutCoordinator
{
    private readonly LayeredLayoutEngine _engine = new();

    public LayoutResult CreateLayout(Core.TypeGraph graph, LayoutOptions? options = null)
    {
        if (graph == null) return new LayoutResult();

        var opts = options ?? new LayoutOptions();
        var layoutGraph = LayoutGraphFactory.Create(graph, opts);
        var result = _engine.Run(layoutGraph, opts);
        result.NodeClusterIds = layoutGraph.Nodes.ToDictionary(n => n.Id, n => n.ClusterId);
        return result;
    }
}
