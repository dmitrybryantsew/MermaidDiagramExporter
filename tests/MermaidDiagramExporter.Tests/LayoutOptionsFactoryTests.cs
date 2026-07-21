using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Settings;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for <see cref="LayoutOptionsFactory"/>: the single mapping point
/// from ProjectSettings to LayoutOptions.
/// </summary>
public class LayoutOptionsFactoryTests
{
    [Fact]
    public void FromSettings_Null_ReturnsDefaults()
    {
        var options = LayoutOptionsFactory.FromSettings(null!);

        Assert.Equal(LayoutEngineKind.Layered, options.Engine);
        Assert.Equal(MsaglPartitionMode.None, options.Msagl.Partition);
    }

    [Fact]
    public void FromSettings_Defaults_MapsLayeredAndNoPartition()
    {
        var options = LayoutOptionsFactory.FromSettings(new ProjectSettings());

        Assert.Equal(LayoutEngineKind.Layered, options.Engine);
        Assert.Equal(MsaglPartitionMode.None, options.Msagl.Partition);
    }

    [Fact]
    public void FromSettings_MapsEngineAndPartition()
    {
        var settings = new ProjectSettings
        {
            Engine = LayoutEngineKind.Msagl,
            Msagl = new MsaglLayoutSettings { PartitionMode = MsaglPartitionMode.FirstLevelNamespace },
        };

        var options = LayoutOptionsFactory.FromSettings(settings);

        Assert.Equal(LayoutEngineKind.Msagl, options.Engine);
        Assert.Equal(MsaglPartitionMode.FirstLevelNamespace, options.Msagl.Partition);
    }

    [Fact]
    public void FromSettings_CompoundEngine_MapsThrough()
    {
        var settings = new ProjectSettings { Engine = LayoutEngineKind.Compound };

        var options = LayoutOptionsFactory.FromSettings(settings);

        Assert.Equal(LayoutEngineKind.Compound, options.Engine);
    }

    [Fact]
    public void FromSettings_MapsForcePreventClusterOverlap()
    {
        var settings = new ProjectSettings
        {
            Engine = LayoutEngineKind.Force,
            Force = new ForceLayoutSettings { PreventClusterOverlap = false },
        };

        var options = LayoutOptionsFactory.FromSettings(settings);

        Assert.Equal(LayoutEngineKind.Force, options.Engine);
        Assert.False(options.Force.PreventClusterOverlap);
    }

    [Fact]
    public void FromSettings_NullForceGroup_DefaultsToPreventOverlap()
    {
        var settings = new ProjectSettings { Engine = LayoutEngineKind.Force, Force = null! };

        var options = LayoutOptionsFactory.FromSettings(settings);

        Assert.True(options.Force.PreventClusterOverlap);
    }

    [Fact]
    public void FromSettings_MapsZoneFirstGroup()
    {
        var settings = new ProjectSettings
        {
            Engine = LayoutEngineKind.ZoneFirst,
            ZoneFirst = new ZoneFirstLayoutSettings { MicroEngine = ZoneFirstMicroEngine.Force, ZoneSpacing = 250f },
        };

        var options = LayoutOptionsFactory.FromSettings(settings);

        Assert.Equal(LayoutEngineKind.ZoneFirst, options.Engine);
        Assert.Equal(ZoneFirstMicroEngine.Force, options.ZoneFirst.MicroEngine);
        Assert.Equal(250f, options.ZoneFirst.ZoneSpacing);
    }

    [Fact]
    public void FromSettings_NullZoneFirstGroup_DefaultsToSugiyamaMicro()
    {
        var settings = new ProjectSettings { Engine = LayoutEngineKind.ZoneFirst, ZoneFirst = null! };

        var options = LayoutOptionsFactory.FromSettings(settings);

        Assert.Equal(ZoneFirstMicroEngine.Sugiyama, options.ZoneFirst.MicroEngine);
        Assert.Equal(120f, options.ZoneFirst.ZoneSpacing);
    }

    [Fact]
    public void FromSettings_PreservesNumericDefaults()
    {
        // Behavior-preservation invariant: the factory must not alter any
        // numeric layout value — they are not persisted per-project yet.
        var options = LayoutOptionsFactory.FromSettings(new ProjectSettings());
        var defaults = new LayoutOptions();

        Assert.Equal(defaults.RankSpacing, options.RankSpacing);
        Assert.Equal(defaults.NodeSpacing, options.NodeSpacing);
        Assert.Equal(defaults.NodeWidth, options.NodeWidth);
        Assert.Equal(defaults.OuterMarginX, options.OuterMarginX);
        Assert.Equal(defaults.MinimumContentWidth, options.MinimumContentWidth);
    }

    [Fact]
    public void FromSettings_NullMsaglGroup_Tolerated()
    {
        var settings = new ProjectSettings { Engine = LayoutEngineKind.Msagl, Msagl = null! };

        var options = LayoutOptionsFactory.FromSettings(settings);

        Assert.Equal(LayoutEngineKind.Msagl, options.Engine);
        Assert.Equal(MsaglPartitionMode.None, options.Msagl.Partition);
    }
}
