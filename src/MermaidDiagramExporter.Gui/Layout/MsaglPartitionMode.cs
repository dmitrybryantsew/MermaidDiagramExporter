namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Top-level cluster partitioning for the MSAGL engine. Replaces the former
/// SeparateAppAndTests / PartitionByFirstLevelNamespace boolean pair — mutual
/// exclusion is now unrepresentable instead of UI-enforced.
/// </summary>
public enum MsaglPartitionMode
{
    /// <summary>No synthetic top-level clusters; namespaces lay out as-is.</summary>
    None,

    /// <summary>Two buckets — "Application" and "Tests" — by test-namespace pattern.</summary>
    AppVsTests,

    /// <summary>N buckets by first-level sub-namespace after the common prefix
    /// (e.g. PFE.Data, PFE.Systems).</summary>
    FirstLevelNamespace,
}
