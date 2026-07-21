namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Core layout options shared by all engines and passes, plus one nested group
/// per engine (<see cref="Layered"/>, <see cref="Compound"/>, <see cref="Msagl"/>)
/// and one for the custom prep pipeline (<see cref="Pipeline"/>). Engine-specific
/// settings live in their group so an engine only ever reads what applies to it.
/// </summary>
public sealed class LayoutOptions
{
    /// <summary>Which layout engine runs. Replaces the former boolean flag pair.</summary>
    public LayoutEngineKind Engine { get; set; } = LayoutEngineKind.Layered;

    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;
    public float RankSpacing { get; set; } = 90f;
    public float ClusterSpacing { get; set; } = 30f;
    public float GroupSpacing { get; set; } = 26f;
    public float NodeSpacing { get; set; } = 18f;
    public float OuterMarginX { get; set; } = 40f;
    public float OuterMarginY { get; set; } = 52f;
    public float NodeWidth { get; set; } = 280f;
    public float MaxMeasuredNodeWidth { get; set; } = 420f;
    public float GroupLeftPadding { get; set; } = 18f;
    public float GroupTopPadding { get; set; } = 34f;
    public float GroupWidth { get; set; } = 320f;
    public float GroupBottomPadding { get; set; } = 18f;
    public float ClusterTitleHorizontalPadding { get; set; } = 24f;
    public float ClusterTitleTopMargin { get; set; } = 8f;
    public float ClusterTitleBottomMargin { get; set; } = 8f;
    public float MinimumContentWidth { get; set; } = 2200f;
    public float MinimumContentHeight { get; set; } = 2200f;

    /// <summary>Options for the original layered engine only.</summary>
    public LayeredEngineOptions Layered { get; set; } = new();

    /// <summary>Options for the compound engine only.</summary>
    public CompoundEngineOptions Compound { get; set; } = new();

    /// <summary>Options for the MSAGL engine only.</summary>
    public MsaglEngineOptions Msagl { get; set; } = new();

    /// <summary>Options for the own force-directed engine only.</summary>
    public ForceEngineOptions Force { get; set; } = new();

    /// <summary>Options for the MSAGL MDS engine variant only.</summary>
    public MsaglMdsEngineOptions MsaglMds { get; set; } = new();

    /// <summary>Options for the zone-first hybrid engine only.</summary>
    public ZoneFirstEngineOptions ZoneFirst { get; set; } = new();

    /// <summary>Options for the custom (non-MSAGL) prep-pipeline passes.</summary>
    public CustomPipelineOptions Pipeline { get; set; } = new();

    /// <summary>
    /// Deep-copies this instance (including all groups). Used when a pass needs
    /// a derived option set (e.g. per-subgraph spacing overrides) without
    /// mutating the caller's options.
    /// </summary>
    public LayoutOptions Clone() => new()
    {
        Engine = Engine,
        Direction = Direction,
        RankSpacing = RankSpacing,
        ClusterSpacing = ClusterSpacing,
        GroupSpacing = GroupSpacing,
        NodeSpacing = NodeSpacing,
        OuterMarginX = OuterMarginX,
        OuterMarginY = OuterMarginY,
        NodeWidth = NodeWidth,
        MaxMeasuredNodeWidth = MaxMeasuredNodeWidth,
        GroupLeftPadding = GroupLeftPadding,
        GroupTopPadding = GroupTopPadding,
        GroupWidth = GroupWidth,
        GroupBottomPadding = GroupBottomPadding,
        ClusterTitleHorizontalPadding = ClusterTitleHorizontalPadding,
        ClusterTitleTopMargin = ClusterTitleTopMargin,
        ClusterTitleBottomMargin = ClusterTitleBottomMargin,
        MinimumContentWidth = MinimumContentWidth,
        MinimumContentHeight = MinimumContentHeight,
        Layered = new LayeredEngineOptions
        {
            TargetRowWidth = Layered.TargetRowWidth,
            ComponentSpacing = Layered.ComponentSpacing,
            StructuredClusterMaxRowWidth = Layered.StructuredClusterMaxRowWidth,
            StructuredClusterMaxNodesPerRow = Layered.StructuredClusterMaxNodesPerRow,
            StructuredNodeColumnSpacing = Layered.StructuredNodeColumnSpacing,
            StructuredRankGap = Layered.StructuredRankGap,
            StructuredWrappedRowGap = Layered.StructuredWrappedRowGap,
            StructuredRowIndentStep = Layered.StructuredRowIndentStep,
            StructuredRowMaxIndent = Layered.StructuredRowMaxIndent,
            StructuredRowCenteringBias = Layered.StructuredRowCenteringBias,
        },
        Compound = new CompoundEngineOptions
        {
            ClusterContainmentEdgeWeight = Compound.ClusterContainmentEdgeWeight,
            CoordinateAssignmentPasses = Compound.CoordinateAssignmentPasses,
        },
        Msagl = new MsaglEngineOptions
        {
            Partition = Msagl.Partition,
        },
        Force = new ForceEngineOptions
        {
            Iterations = Force.Iterations,
            RepulsionConstant = Force.RepulsionConstant,
            SpringConstant = Force.SpringConstant,
            ClusterGravity = Force.ClusterGravity,
            Seed = Force.Seed,
            PreventClusterOverlap = Force.PreventClusterOverlap,
        },
        MsaglMds = new MsaglMdsEngineOptions
        {
            PivotNumber = MsaglMds.PivotNumber,
            IterationsWithMajorization = MsaglMds.IterationsWithMajorization,
            ScaleX = MsaglMds.ScaleX,
            ScaleY = MsaglMds.ScaleY,
            RemoveOverlaps = MsaglMds.RemoveOverlaps,
        },
        ZoneFirst = new ZoneFirstEngineOptions
        {
            MicroEngine = ZoneFirst.MicroEngine,
            ZoneSpacing = ZoneFirst.ZoneSpacing,
        },
        Pipeline = new CustomPipelineOptions
        {
            ClusterAnchorWidth = Pipeline.ClusterAnchorWidth,
            ClusterAnchorHeight = Pipeline.ClusterAnchorHeight,
            RecursiveRankSpacingBonus = Pipeline.RecursiveRankSpacingBonus,
        },
    };
}
