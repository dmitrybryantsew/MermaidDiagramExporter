namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Which engine lays out the interior of each zone in the zone-first hybrid
/// (<see cref="LayoutEngineKind.ZoneFirst"/>). The macro placement of zones
/// is always force-directed; this selects only the per-zone micro layout.
/// </summary>
public enum ZoneFirstMicroEngine
{
    /// <summary>
    /// MSAGL Sugiyama layered layout per zone — readable inheritance flow
    /// inside each namespace (recommended: hierarchy reads well locally,
    /// while coupling drives zone placement globally).
    /// </summary>
    Sugiyama,

    /// <summary>
    /// Own force-directed engine per zone — proximity-based interior, same
    /// look as the plain Force engine but with guaranteed zone separation.
    /// </summary>
    Force,
}

/// <summary>
/// Options read only by the zone-first hybrid engine
/// (<see cref="LayoutEngineKind.ZoneFirst"/>, <see cref="ZoneFirstLayoutEngine"/>).
/// </summary>
public sealed class ZoneFirstEngineOptions
{
    /// <summary>Per-zone interior layout engine (default Sugiyama).</summary>
    public ZoneFirstMicroEngine MicroEngine { get; set; } = ZoneFirstMicroEngine.Sugiyama;

    /// <summary>
    /// Minimum gap between zone boxes in the macro placement (default 120).
    /// Also used as the ideal spring distance between coupled zones.
    /// </summary>
    public float ZoneSpacing { get; set; } = 120f;
}
