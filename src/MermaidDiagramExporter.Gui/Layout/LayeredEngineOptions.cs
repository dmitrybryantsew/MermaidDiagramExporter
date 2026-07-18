namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Options read only by <see cref="LayeredLayoutEngine"/> (structured row
/// wrapping inside clusters, component row packing).
/// </summary>
public sealed class LayeredEngineOptions
{
    public float TargetRowWidth { get; set; } = 2400f;
    public float ComponentSpacing { get; set; } = 60f;
    public float StructuredClusterMaxRowWidth { get; set; } = 980f;
    public int StructuredClusterMaxNodesPerRow { get; set; } = 3;
    public float StructuredNodeColumnSpacing { get; set; } = 24f;
    public float StructuredRankGap { get; set; } = 34f;
    public float StructuredWrappedRowGap { get; set; } = 14f;
    public float StructuredRowIndentStep { get; set; } = 18f;
    public float StructuredRowMaxIndent { get; set; } = 56f;
    public float StructuredRowCenteringBias { get; set; } = 0.10f;
}
