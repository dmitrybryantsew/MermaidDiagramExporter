namespace MermaidDiagramExporter.Gui.Layout;

public sealed class LayoutOptions
{
    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;
    public float RankSpacing { get; set; } = 90f;
    public float ClusterSpacing { get; set; } = 30f;
    public float ComponentSpacing { get; set; } = 60f;
    public float GroupLeftPadding { get; set; } = 18f;
    public float GroupTopPadding { get; set; } = 34f;
    public float GroupWidth { get; set; } = 320f;
    public float GroupSpacing { get; set; } = 26f;
    public float NodeSpacing { get; set; } = 18f;
    public float OuterMarginX { get; set; } = 40f;
    public float OuterMarginY { get; set; } = 52f;
    public float NodeWidth { get; set; } = 280f;
    public float MaxMeasuredNodeWidth { get; set; } = 420f;
    public float GroupBottomPadding { get; set; } = 18f;
    public float ClusterTitleHorizontalPadding { get; set; } = 24f;
    public float ClusterTitleTopMargin { get; set; } = 8f;
    public float ClusterTitleBottomMargin { get; set; } = 8f;
    public float NodeColumnSpacing { get; set; } = 16f;
    public int MaxClusterColumns { get; set; } = 3;
    public float TargetRowWidth { get; set; } = 2400f;
    public float StructuredClusterMaxRowWidth { get; set; } = 980f;
    public int StructuredClusterMaxNodesPerRow { get; set; } = 3;
    public float StructuredNodeColumnSpacing { get; set; } = 24f;
    public float StructuredRankGap { get; set; } = 34f;
    public float StructuredWrappedRowGap { get; set; } = 14f;
    public float StructuredRowIndentStep { get; set; } = 18f;
    public float StructuredRowMaxIndent { get; set; } = 56f;
    public float StructuredRowCenteringBias { get; set; } = 0.10f;
    public float ClusterAnchorWidth { get; set; } = 18f;
    public float ClusterAnchorHeight { get; set; } = 18f;
    public float RecursiveRankSpacingBonus { get; set; } = 25f;
    public float MinimumContentWidth { get; set; } = 2200f;
    public float MinimumContentHeight { get; set; } = 2200f;

    // ── Compound layout engine (docs/08) ──
    /// <summary>
    /// Feature flag: when true, uses the new CompoundLayeredLayoutEngine
    /// (unified node+border-dummy ranking, compound-aware ordering).
    /// Default false until validated per docs/09.
    /// </summary>
    public bool UseCompoundLayoutEngine { get; set; } = false;

    /// <summary>Weight used for cluster containment edges (docs/06 Step 2c).</summary>
    public float ClusterContainmentEdgeWeight { get; set; } = 24f;

    /// <summary>Number of coordinate-assignment passes (docs/08 Part A2).</summary>
    public int CoordinateAssignmentPasses { get; set; } = 6;
}
