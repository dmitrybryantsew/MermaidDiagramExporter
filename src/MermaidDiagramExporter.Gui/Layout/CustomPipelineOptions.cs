namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Options read only by passes in the custom prep pipeline
/// (<see cref="GraphLayoutCoordinator"/>'s non-MSAGL pipeline): anchor dummy
/// sizing and recursive subgraph spacing. Never consulted by the MSAGL path.
/// </summary>
public sealed class CustomPipelineOptions
{
    public float ClusterAnchorWidth { get; set; } = 18f;
    public float ClusterAnchorHeight { get; set; } = 18f;
    public float RecursiveRankSpacingBonus { get; set; } = 25f;
}
