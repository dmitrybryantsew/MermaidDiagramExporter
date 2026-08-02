using System.Text.Json;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Settings;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for <see cref="ProjectSettings.Normalize"/>: legacy boolean flag
/// migration into the enum model, precedence rules, and idempotency.
/// </summary>
public class ProjectSettingsMigrationTests
{
    // Mirrors SettingsService's serializer options (camelCase).
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static ProjectSettings DeserializeAndNormalize(string json)
    {
        var settings = JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions)!;
        settings.Normalize();
        return settings;
    }

    [Fact]
    public void LegacyJson_MsaglEngine_MigratesToEngineEnum()
    {
        var settings = DeserializeAndNormalize(
            """{ "useMsaglEngine": true, "separateAppAndTests": true }""");

        Assert.Equal(LayoutEngineKind.Msagl, settings.Engine);
        Assert.Equal(MsaglPartitionMode.AppVsTests, settings.Msagl.PartitionMode);
    }

    [Fact]
    public void LegacyJson_CompoundEngine_MigratesToEngineEnum()
    {
        var settings = DeserializeAndNormalize("""{ "useCompoundLayoutEngine": true }""");

        Assert.Equal(LayoutEngineKind.Compound, settings.Engine);
        Assert.Equal(MsaglPartitionMode.None, settings.Msagl.PartitionMode);
    }

    [Fact]
    public void LegacyJson_BothEngineBools_MsaglWins()
    {
        // Matches the old GraphLayoutCoordinator precedence (MSAGL first).
        var settings = DeserializeAndNormalize(
            """{ "useMsaglEngine": true, "useCompoundLayoutEngine": true }""");

        Assert.Equal(LayoutEngineKind.Msagl, settings.Engine);
    }

    [Fact]
    public void LegacyJson_BothPartitionBools_FirstLevelWins()
    {
        // Matches the old MsaglLayoutEngine precedence (first-level checked first).
        var settings = DeserializeAndNormalize(
            """{ "useMsaglEngine": true, "separateAppAndTests": true, "partitionByFirstLevelNamespace": true }""");

        Assert.Equal(MsaglPartitionMode.FirstLevelNamespace, settings.Msagl.PartitionMode);
    }

    [Fact]
    public void Normalize_ResetsLegacyFlags()
    {
        var settings = DeserializeAndNormalize(
            """{ "useMsaglEngine": true, "separateAppAndTests": true }""");

#pragma warning disable CS0618
        Assert.False(settings.UseMsaglEngine);
        Assert.False(settings.UseCompoundLayoutEngine);
        Assert.False(settings.SeparateAppAndTests);
        Assert.False(settings.PartitionByFirstLevelNamespace);
#pragma warning restore CS0618
    }

    [Fact]
    public void Normalize_DoesNotDowngradeExplicitNewFormat()
    {
        // New-format engine plus a stale legacy bool: the enum wins.
        var settings = DeserializeAndNormalize(
            """{ "engine": 2, "useCompoundLayoutEngine": true }""");

        Assert.Equal(LayoutEngineKind.Msagl, settings.Engine);
    }

    [Fact]
    public void Normalize_NullMsaglGroup_Tolerated()
    {
        var settings = DeserializeAndNormalize("""{ "engine": 2, "msagl": null }""");

        Assert.Equal(LayoutEngineKind.Msagl, settings.Engine);
        Assert.NotNull(settings.Msagl);
        Assert.Equal(MsaglPartitionMode.None, settings.Msagl.PartitionMode);
    }

    [Fact]
    public void Normalize_IsIdempotent()
    {
        var settings = DeserializeAndNormalize(
            """{ "useMsaglEngine": true, "partitionByFirstLevelNamespace": true }""");

        settings.Normalize();

        Assert.Equal(LayoutEngineKind.Msagl, settings.Engine);
        Assert.Equal(MsaglPartitionMode.FirstLevelNamespace, settings.Msagl.PartitionMode);
    }

    [Fact]
    public void NewFormat_RoundTripsThroughJson()
    {
        var original = new ProjectSettings
        {
            Engine = LayoutEngineKind.Msagl,
            Msagl = new MsaglLayoutSettings { PartitionMode = MsaglPartitionMode.FirstLevelNamespace },
        };

        string json = JsonSerializer.Serialize(original, JsonOptions);
        var loaded = DeserializeAndNormalize(json);

        Assert.Equal(LayoutEngineKind.Msagl, loaded.Engine);
        Assert.Equal(MsaglPartitionMode.FirstLevelNamespace, loaded.Msagl.PartitionMode);
    }

    [Fact]
    public void EmptyJson_KeepsDefaults()
    {
        var settings = DeserializeAndNormalize("""{}""");

        Assert.Equal(LayoutEngineKind.Layered, settings.Engine);
        Assert.Equal(MsaglPartitionMode.None, settings.Msagl.PartitionMode);
    }
}
