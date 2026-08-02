namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Options read only by <see cref="MsaglLayoutEngine"/>. Because the MSAGL engine
/// is the only reader, these settings apply exactly when
/// <see cref="LayoutOptions.Engine"/> is <see cref="LayoutEngineKind.Msagl"/> —
/// applicability is structural, not convention.
/// </summary>
public sealed class MsaglEngineOptions
{
    /// <summary>
    /// Top-level partitioning: creates synthetic clusters above the namespace
    /// clusters so MSAGL packs them side-by-side (columns).
    /// </summary>
    public MsaglPartitionMode Partition { get; set; } = MsaglPartitionMode.None;
}
