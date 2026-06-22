using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class PostLayoutPipeline
{
    private readonly List<IPostLayoutPass> _passes = new();

    public PostLayoutPipeline AddPass(IPostLayoutPass pass)
    {
        if (pass != null)
            _passes.Add(pass);
        return this;
    }

    public LayoutResult Run(LayoutGraph graph, LayoutResult result, LayoutOptions options)
    {
        LayoutResult currentResult = result ?? new LayoutResult();
        foreach (var pass in _passes)
        {
            currentResult = pass.Run(graph, currentResult, options) ?? currentResult;
        }
        return currentResult;
    }
}
