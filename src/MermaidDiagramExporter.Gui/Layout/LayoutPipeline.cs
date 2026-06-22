using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class LayoutPipeline
{
    private readonly List<ILayoutPass> _passes = new();

    public LayoutPipeline AddPass(ILayoutPass pass)
    {
        if (pass != null)
            _passes.Add(pass);
        return this;
    }

    public LayoutGraph Run(LayoutGraph graph, LayoutOptions options)
    {
        LayoutGraph currentGraph = graph ?? new LayoutGraph();
        foreach (var pass in _passes)
        {
            currentGraph = pass.Run(currentGraph, options) ?? currentGraph;
        }
        return currentGraph;
    }
}
