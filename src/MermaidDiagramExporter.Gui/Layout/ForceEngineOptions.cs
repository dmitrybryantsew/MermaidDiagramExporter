namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Options read only by the own force-directed engine
/// (<see cref="LayoutEngineKind.Force"/>, <see cref="ForceDirectedLayoutEngine"/>).
/// Not persisted per-project yet (same R6 scope as the other numeric layout
/// options).
/// </summary>
public sealed class ForceEngineOptions
{
    /// <summary>Number of force iterations (default 300).</summary>
    public int Iterations { get; set; } = 300;

    /// <summary>
    /// Multiplier for node-node repulsion (default 0.7). Higher spreads the
    /// drawing out.
    /// </summary>
    public double RepulsionConstant { get; set; } = 0.7;

    /// <summary>
    /// Multiplier for edge springs (default 1.5). Higher pulls connected nodes
    /// closer together. Per-edge force is additionally scaled by
    /// LayoutEdgeWeights (inheritance 3×, implements 2.5×).
    /// </summary>
    public double SpringConstant { get; set; } = 1.5;

    /// <summary>
    /// How strongly nodes are pulled toward their top-level cluster centroid
    /// per iteration (default 0.5). Higher = tighter namespace zones, lower =
    /// zones allowed to interleave.
    /// </summary>
    public double ClusterGravity { get; set; } = 0.5;

    /// <summary>
    /// Random seed for the initial placement. Fixed seed = deterministic
    /// layout: same graph, same result.
    /// </summary>
    public int Seed { get; set; } = 42;

    /// <summary>
    /// When true (default), runs ClusterOverlapResolutionPass after layout so
    /// namespace cluster rectangles are guaranteed not to overlap (overlapping
    /// sibling clusters are rigidly translated apart). When false, clusters are
    /// drawn tight around their members and may overlap where zones interleave.
    /// </summary>
    public bool PreventClusterOverlap { get; set; } = true;
}
