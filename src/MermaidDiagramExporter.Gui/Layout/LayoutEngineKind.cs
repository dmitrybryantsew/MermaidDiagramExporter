namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Selects which layout engine runs in <see cref="GraphLayoutCoordinator"/>.
/// Replaces the former UseCompoundLayoutEngine / UseMsaglEngine boolean pair —
/// with an enum, "MSAGL takes precedence over Compound" is unrepresentable
/// rather than a comment-enforced rule.
/// </summary>
public enum LayoutEngineKind
{
    /// <summary>Original cluster-as-supernode layered engine (default).</summary>
    Layered,

    /// <summary>Experimental compound engine (unified node+border-dummy ranking).</summary>
    Compound,

    /// <summary>MSAGL-based engine (Sugiyama framework + native cluster support).</summary>
    Msagl,
}
