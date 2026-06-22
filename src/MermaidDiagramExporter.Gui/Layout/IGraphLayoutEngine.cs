namespace MermaidDiagramExporter.Gui.Layout;

public interface IGraphLayoutEngine
{
    LayoutResult Run(LayoutGraph graph, LayoutOptions options);
}
