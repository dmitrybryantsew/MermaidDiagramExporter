using MermaidDiagramExporter.Gui.Layout;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for <see cref="LayoutOptions.Clone"/>: value fidelity and deep-copy
/// semantics (groups must not be shared between original and clone).
/// </summary>
public class LayoutOptionsCloneTests
{
    [Fact]
    public void Clone_CopiesAllValues()
    {
        var original = new LayoutOptions
        {
            Engine = LayoutEngineKind.Msagl,
            Direction = LayoutDirection.TopToBottom,
            RankSpacing = 111f,
            NodeSpacing = 22f,
            NodeWidth = 333f,
            OuterMarginX = 44f,
            Layered = { TargetRowWidth = 555f, StructuredRankGap = 66f },
            Compound = { ClusterContainmentEdgeWeight = 77f, CoordinateAssignmentPasses = 8 },
            Msagl = { Partition = MsaglPartitionMode.AppVsTests },
            Pipeline = { ClusterAnchorWidth = 99f, RecursiveRankSpacingBonus = 12f },
        };

        var clone = original.Clone();

        Assert.Equal(LayoutEngineKind.Msagl, clone.Engine);
        Assert.Equal(LayoutDirection.TopToBottom, clone.Direction);
        Assert.Equal(111f, clone.RankSpacing);
        Assert.Equal(22f, clone.NodeSpacing);
        Assert.Equal(333f, clone.NodeWidth);
        Assert.Equal(44f, clone.OuterMarginX);
        Assert.Equal(555f, clone.Layered.TargetRowWidth);
        Assert.Equal(66f, clone.Layered.StructuredRankGap);
        Assert.Equal(77f, clone.Compound.ClusterContainmentEdgeWeight);
        Assert.Equal(8, clone.Compound.CoordinateAssignmentPasses);
        Assert.Equal(MsaglPartitionMode.AppVsTests, clone.Msagl.Partition);
        Assert.Equal(99f, clone.Pipeline.ClusterAnchorWidth);
        Assert.Equal(12f, clone.Pipeline.RecursiveRankSpacingBonus);
    }

    [Fact]
    public void Clone_GroupsAreDeepCopied()
    {
        var original = new LayoutOptions();
        var clone = original.Clone();

        Assert.NotSame(original.Layered, clone.Layered);
        Assert.NotSame(original.Compound, clone.Compound);
        Assert.NotSame(original.Msagl, clone.Msagl);
        Assert.NotSame(original.Pipeline, clone.Pipeline);
    }

    [Fact]
    public void Clone_MutationDoesNotLeakToOriginal()
    {
        var original = new LayoutOptions();
        var clone = original.Clone();

        clone.RankSpacing = 999f;
        clone.Layered.TargetRowWidth = 1f;
        clone.Compound.CoordinateAssignmentPasses = 42;
        clone.Msagl.Partition = MsaglPartitionMode.FirstLevelNamespace;
        clone.Pipeline.RecursiveRankSpacingBonus = 7f;

        var defaults = new LayoutOptions();
        Assert.Equal(defaults.RankSpacing, original.RankSpacing);
        Assert.Equal(defaults.Layered.TargetRowWidth, original.Layered.TargetRowWidth);
        Assert.Equal(defaults.Compound.CoordinateAssignmentPasses, original.Compound.CoordinateAssignmentPasses);
        Assert.Equal(MsaglPartitionMode.None, original.Msagl.Partition);
        Assert.Equal(defaults.Pipeline.RecursiveRankSpacingBonus, original.Pipeline.RecursiveRankSpacingBonus);
    }
}
