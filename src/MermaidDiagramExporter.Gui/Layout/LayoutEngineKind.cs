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

    /// <summary>
    /// MSAGL MDS engine (PivotMDS + stress majorization): places nodes so 2D
    /// distances approximate graph-theoretic distances — connected classes land
    /// near each other, unrelated ones far apart. No flow/hierarchy direction.
    /// </summary>
    MsaglMds,

    /// <summary>
    /// Own force-directed engine (Fruchterman–Reingold style, rectangle-aware,
    /// cluster gravity): "connected = close" by construction. Deterministic
    /// (fixed seed). Not MSAGL-backed — MSAGL's public force path is a cheap
    /// seed layout that degenerates on large clustered graphs.
    /// Value kept at 4 for persisted-settings compatibility with the removed
    /// MSAGL-FIL-backed MsaglForce variant.
    /// </summary>
    Force = 4,

    /// <summary>
    /// Zone-first hybrid (macro/micro two-level layout): each top-level
    /// namespace becomes a zone supernode; the small zone graph is laid out
    /// with the force engine (coupled zones adjacent, unrelated far apart),
    /// then each zone's interior is laid out independently (Sugiyama or
    /// force) and placed into its reserved zone box. Zone boxes are
    /// guaranteed non-overlapping by construction.
    /// </summary>
    ZoneFirst = 5,
}
