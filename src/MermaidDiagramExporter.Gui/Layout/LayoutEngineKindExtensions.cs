namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Classification helpers for <see cref="LayoutEngineKind"/>.
/// </summary>
public static class LayoutEngineKindExtensions
{
    /// <summary>
    /// True for all engines backed by MSAGL (Sugiyama, MDS). These share the
    /// stripped-down prep pipeline (measurement + cluster hierarchy only)
    /// and skip the custom post-layout polish passes, because MSAGL produces
    /// its own cluster bounds (MDS cluster bounds are computed by
    /// ClusterBoundsComputer instead — MSAGL's MDS ignores clusters).
    /// </summary>
    public static bool IsMsaglFamily(this LayoutEngineKind kind)
    {
        return kind is LayoutEngineKind.Msagl
            or LayoutEngineKind.MsaglMds;
    }

    /// <summary>
    /// True for engines that need only measurement + cluster hierarchy from the
    /// prep pipeline (no anchor/boundary dummy nodes) and produce their own
    /// cluster bounds, so the custom post-layout polish passes are skipped:
    /// the MSAGL family, the own force engine, and the zone-first hybrid
    /// (which runs its own per-zone micro layouts).
    /// </summary>
    public static bool UsesMinimalPrepPipeline(this LayoutEngineKind kind)
    {
        return kind.IsMsaglFamily()
            || kind == LayoutEngineKind.Force
            || kind == LayoutEngineKind.ZoneFirst;
    }
}
