using System;
using System.IO;
using MermaidDiagramExporter.Gui;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Persistence;
using MermaidDiagramExporter.Gui.Settings;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class ManualLayoutOverridesRoundtripTests
{
    private static string CreateTempCacheDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mermaid_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static TypeGraphCacheService CreateCacheService(string cacheDir)
    {
        var settingsService = new SettingsService();
        // Use reflection or a custom ProjectSettings to control the cache directory
        return new TypeGraphCacheService(settingsService);
    }

    private static ProjectSettings CreateSettings(string cacheDir)
    {
        return new ProjectSettings
        {
            SourceFolderPath = cacheDir,
            CustomCacheFolderPath = cacheDir
        };
    }

    [Fact]
    public void Roundtrip_MultipleNodeOverrides_PreservesAllDeltas()
    {
        string cacheDir = CreateTempCacheDir();
        try
        {
            var settingsService = new SettingsService();
            var cacheService = new TypeGraphCacheService(settingsService);
            var settings = CreateSettings(cacheDir);

            var original = new ManualLayoutOverrides();
            original.SetDelta("node1", new Vector2(10.5f, -20.3f));
            original.SetDelta("node2", new Vector2(0f, 15.7f));
            original.SetDelta("node3", new Vector2(-5.1f, 8.9f));
            original.LastSavedUtc = new DateTime(2025, 6, 15, 12, 0, 0, DateTimeKind.Utc);

            cacheService.SaveManualOverrides(original, settings);
            var loaded = cacheService.LoadManualOverrides(settings);

            Assert.True(loaded.HasOverrides);
            Assert.Equal(3, loaded.NodePositionDeltas.Count);
            Assert.Equal(new Vector2(10.5f, -20.3f), loaded.GetDelta("node1"));
            Assert.Equal(new Vector2(0f, 15.7f), loaded.GetDelta("node2"));
            Assert.Equal(new Vector2(-5.1f, 8.9f), loaded.GetDelta("node3"));
        }
        finally
        {
            try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
        }
    }

    [Fact]
    public void Roundtrip_SingleNodeOverride_PreservesDelta()
    {
        string cacheDir = CreateTempCacheDir();
        try
        {
            var settingsService = new SettingsService();
            var cacheService = new TypeGraphCacheService(settingsService);
            var settings = CreateSettings(cacheDir);

            var original = new ManualLayoutOverrides();
            original.SetDelta("MyClass", new Vector2(100f, 200f));

            cacheService.SaveManualOverrides(original, settings);
            var loaded = cacheService.LoadManualOverrides(settings);

            Assert.True(loaded.HasOverrides);
            Assert.Single(loaded.NodePositionDeltas);
            Assert.Equal(new Vector2(100f, 200f), loaded.GetDelta("MyClass"));
        }
        finally
        {
            try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
        }
    }

    [Fact]
    public void Roundtrip_EmptyOverrides_ReturnsEmpty()
    {
        string cacheDir = CreateTempCacheDir();
        try
        {
            var settingsService = new SettingsService();
            var cacheService = new TypeGraphCacheService(settingsService);
            var settings = CreateSettings(cacheDir);

            // Don't save anything — just load
            var loaded = cacheService.LoadManualOverrides(settings);

            Assert.False(loaded.HasOverrides);
            Assert.Empty(loaded.NodePositionDeltas);
        }
        finally
        {
            try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
        }
    }

    [Fact]
    public void Roundtrip_ZeroDelta_RemovesOverride()
    {
        string cacheDir = CreateTempCacheDir();
        try
        {
            var settingsService = new SettingsService();
            var cacheService = new TypeGraphCacheService(settingsService);
            var settings = CreateSettings(cacheDir);

            var original = new ManualLayoutOverrides();
            // Setting a zero delta should remove the entry
            original.SetDelta("node1", new Vector2(0f, 0f));

            cacheService.SaveManualOverrides(original, settings);

            // Should not create a file since HasOverrides is false
            string path = Path.Combine(cacheDir, "layout.overrides.json");
            Assert.False(File.Exists(path));
        }
        finally
        {
            try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
        }
    }

    /// <summary>
    /// Bug 02 Fix 1 verification: saving empty overrides over an existing file
    /// must delete the stale file so Reset Layout actually resets on next load.
    /// </summary>
    [Fact]
    public void SaveEmptyOverrides_DeletesStaleFile()
    {
        string cacheDir = CreateTempCacheDir();
        try
        {
            var settingsService = new SettingsService();
            var cacheService = new TypeGraphCacheService(settingsService);
            var settings = CreateSettings(cacheDir);

            // Step 1: save a non-empty overrides file
            var original = new ManualLayoutOverrides();
            original.SetDelta("node1", new Vector2(10f, 20f));
            cacheService.SaveManualOverrides(original, settings);
            string path = Path.Combine(cacheDir, "layout.overrides.json");
            Assert.True(File.Exists(path));

            // Step 2: save empty overrides over the same directory
            var empty = new ManualLayoutOverrides();
            cacheService.SaveManualOverrides(empty, settings);

            // Step 3: the stale file must be gone
            Assert.False(File.Exists(path));

            // Step 4: loading must now return an empty overrides object
            var loaded = cacheService.LoadManualOverrides(settings);
            Assert.False(loaded.HasOverrides);
            Assert.Empty(loaded.NodePositionDeltas);
        }
        finally
        {
            try { if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true); } catch { }
        }
    }
}
