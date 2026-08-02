using System;
using System.IO;
using MermaidDiagramExporter.Gui.Settings;
using MermaidDiagramExporter.Gui.Theming;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for <see cref="AppSettingsService"/>: roundtrip, corrupt-file
/// tolerance, and default values.
/// </summary>
public class AppSettingsServiceTests
{
    private static string CreateTempPath()
    {
        return Path.Combine(Path.GetTempPath(), $"app-settings-{Guid.NewGuid():N}.json");
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefaults()
    {
        var svc = new AppSettingsService();
        var settings = svc.LoadFrom(Path.Combine(Path.GetTempPath(), $"nonexistent-{Guid.NewGuid():N}.json"));
        Assert.Equal(UiTheme.System, settings.Theme);
    }

    [Fact]
    public void SaveThenLoad_RoundtripsTheme()
    {
        var path = CreateTempPath();
        try
        {
            var svc = new AppSettingsService();
            svc.SaveTo(path, new AppSettings { Theme = UiTheme.Dark });

            var loaded = svc.LoadFrom(path);
            Assert.Equal(UiTheme.Dark, loaded.Theme);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadFrom_CorruptJson_ReturnsDefaults()
    {
        var path = CreateTempPath();
        try
        {
            File.WriteAllText(path, "{ this is not valid json");
            var svc = new AppSettingsService();
            var loaded = svc.LoadFrom(path);
            Assert.Equal(UiTheme.System, loaded.Theme);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void SaveTo_CreatesDirectoryIfMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"app-settings-test-{Guid.NewGuid():N}");
        var path = Path.Combine(root, "nested", "app.settings.json");
        try
        {
            var svc = new AppSettingsService();
            svc.SaveTo(path, new AppSettings { Theme = UiTheme.Light });
            Assert.True(File.Exists(path));
            Assert.Equal(UiTheme.Light, svc.LoadFrom(path).Theme);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
