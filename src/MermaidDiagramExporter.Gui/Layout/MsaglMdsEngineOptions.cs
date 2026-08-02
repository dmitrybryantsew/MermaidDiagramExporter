namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Options read only by the MDS MSAGL engine variant
/// (<see cref="LayoutEngineKind.MsaglMds"/>, PivotMDS + stress majorization).
/// Defaults mirror MSAGL 1.2.1's own defaults. Not persisted per-project yet
/// (same R6 scope as the other numeric layout options).
/// </summary>
public sealed class MsaglMdsEngineOptions
{
    /// <summary>
    /// Number of pivot nodes for PivotMDS distance approximation (MSAGL
    /// default: 50). Higher = more accurate distances, slower.
    /// </summary>
    public int PivotNumber { get; set; } = 50;

    /// <summary>Stress-majorization refinement passes (MSAGL default: 30).</summary>
    public int IterationsWithMajorization { get; set; } = 30;

    /// <summary>Horizontal distance scale factor (MSAGL default: 200).</summary>
    public double ScaleX { get; set; } = 200;

    /// <summary>Vertical distance scale factor (MSAGL default: 200).</summary>
    public double ScaleY { get; set; } = 200;

    /// <summary>Run the overlap-removal pass after placement (MSAGL default: true).</summary>
    public bool RemoveOverlaps { get; set; } = true;
}
