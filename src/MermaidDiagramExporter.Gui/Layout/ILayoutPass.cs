namespace MermaidDiagramExporter.Gui.Layout;

public interface ILayoutPass
{
    string Name { get; }
    LayoutGraph Run(LayoutGraph graph, LayoutOptions options);
}
