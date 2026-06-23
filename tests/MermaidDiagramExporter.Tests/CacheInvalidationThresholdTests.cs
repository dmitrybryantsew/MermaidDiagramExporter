using System;
using System.IO;
using System.Text.Json;
using MermaidDiagramExporter.Gui.Persistence;
using MermaidDiagramExporter.Gui.Settings;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class CacheInvalidationThresholdTests
{
    private static string CreateTempDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), "mermaid_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteFile(string dir, string name, string content)
    {
        File.WriteAllText(Path.Combine(dir, name), content);
    }

    private static string ComputeHash(string filePath)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash)[..16];
    }

    private static void WriteManifest(string manifestPath, string sourceDir, Dictionary<string, string> relativePathHashes)
    {
        var manifest = new CacheManifest
        {
            SourceFolder = sourceDir,
            LastScanUtc = DateTime.UtcNow,
            FileHashes = relativePathHashes,
            TotalFiles = relativePathHashes.Count
        };
        var json = JsonSerializer.Serialize(manifest);
        File.WriteAllText(manifestPath, json);
    }

    private static TypeGraphCacheService CreateCacheService()
    {
        return new TypeGraphCacheService(new SettingsService());
    }

    private static ProjectSettings CreateSettings(string sourceDir, string cacheDir)
    {
        return new ProjectSettings
        {
            SourceFolderPath = sourceDir,
            CustomCacheFolderPath = cacheDir
        };
    }

    [Fact]
    public void ValidateManifest_NoChanges_ReturnsUpToDate()
    {
        string sourceDir = CreateTempDir();
        string cacheDir = CreateTempDir();
        try
        {
            WriteFile(sourceDir, "A.cs", "class A {}");
            WriteFile(sourceDir, "B.cs", "class B {}");

            // Build manifest with relative path keys (matching GetSourceFileHashes)
            var hashes = new Dictionary<string, string>
            {
                { "A.cs", ComputeHash(Path.Combine(sourceDir, "A.cs")) },
                { "B.cs", ComputeHash(Path.Combine(sourceDir, "B.cs")) }
            };

            string manifestPath = Path.Combine(cacheDir, ".cache-manifest.json");
            WriteManifest(manifestPath, sourceDir, hashes);

            var result = CreateCacheService().ValidateManifest(manifestPath, CreateSettings(sourceDir, cacheDir));
            Assert.Equal(CacheValidationResult.UpToDate, result);
        }
        finally
        {
            try { Directory.Delete(sourceDir, true); } catch { }
            try { Directory.Delete(cacheDir, true); } catch { }
        }
    }

    [Fact]
    public void ValidateManifest_BelowThresholdChanges_ReturnsMinorChanges()
    {
        string sourceDir = CreateTempDir();
        string cacheDir = CreateTempDir();
        try
        {
            // Create 10 files
            for (int i = 0; i < 10; i++)
                WriteFile(sourceDir, $"File{i}.cs", $"class File{i} {{}}");

            // Build manifest with original hashes using relative paths
            var originalHashes = new Dictionary<string, string>();
            foreach (var file in Directory.GetFiles(sourceDir, "*.cs"))
            {
                string relativePath = Path.GetRelativePath(sourceDir, file);
                originalHashes[relativePath] = ComputeHash(file);
            }

            // Modify 1 file (10% = exactly at threshold, should be MinorChanges since <= 0.10)
            WriteFile(sourceDir, "File0.cs", "class File0 { int X; }");

            string manifestPath = Path.Combine(cacheDir, ".cache-manifest.json");
            WriteManifest(manifestPath, sourceDir, originalHashes);

            var result = CreateCacheService().ValidateManifest(manifestPath, CreateSettings(sourceDir, cacheDir));
            Assert.Equal(CacheValidationResult.MinorChanges, result);
        }
        finally
        {
            try { Directory.Delete(sourceDir, true); } catch { }
            try { Directory.Delete(cacheDir, true); } catch { }
        }
    }

    [Fact]
    public void ValidateManifest_AboveThresholdChanges_ReturnsMismatchTooLarge()
    {
        string sourceDir = CreateTempDir();
        string cacheDir = CreateTempDir();
        try
        {
            // Create 10 files
            for (int i = 0; i < 10; i++)
                WriteFile(sourceDir, $"File{i}.cs", $"class File{i} {{}}");

            var originalHashes = new Dictionary<string, string>();
            foreach (var file in Directory.GetFiles(sourceDir, "*.cs"))
            {
                string relativePath = Path.GetRelativePath(sourceDir, file);
                originalHashes[relativePath] = ComputeHash(file);
            }

            // Modify 2 files (20% > 10% threshold)
            WriteFile(sourceDir, "File0.cs", "class File0 { int X; }");
            WriteFile(sourceDir, "File1.cs", "class File1 { int Y; }");

            string manifestPath = Path.Combine(cacheDir, ".cache-manifest.json");
            WriteManifest(manifestPath, sourceDir, originalHashes);

            var result = CreateCacheService().ValidateManifest(manifestPath, CreateSettings(sourceDir, cacheDir));
            Assert.Equal(CacheValidationResult.MismatchTooLarge, result);
        }
        finally
        {
            try { Directory.Delete(sourceDir, true); } catch { }
            try { Directory.Delete(cacheDir, true); } catch { }
        }
    }

    [Fact]
    public void ValidateManifest_NewFilesOnly_AboveThreshold_ReturnsMismatchTooLarge()
    {
        string sourceDir = CreateTempDir();
        string cacheDir = CreateTempDir();
        try
        {
            // Create 10 files
            for (int i = 0; i < 10; i++)
                WriteFile(sourceDir, $"File{i}.cs", $"class File{i} {{}}");

            var originalHashes = new Dictionary<string, string>();
            foreach (var file in Directory.GetFiles(sourceDir, "*.cs"))
            {
                string relativePath = Path.GetRelativePath(sourceDir, file);
                originalHashes[relativePath] = ComputeHash(file);
            }

            // Add 2 new files (now 12 total, 2 changed = 16.7% > 10%)
            WriteFile(sourceDir, "NewFile1.cs", "class NewFile1 {}");
            WriteFile(sourceDir, "NewFile2.cs", "class NewFile2 {}");

            string manifestPath = Path.Combine(cacheDir, ".cache-manifest.json");
            WriteManifest(manifestPath, sourceDir, originalHashes);

            var result = CreateCacheService().ValidateManifest(manifestPath, CreateSettings(sourceDir, cacheDir));
            Assert.Equal(CacheValidationResult.MismatchTooLarge, result);
        }
        finally
        {
            try { Directory.Delete(sourceDir, true); } catch { }
            try { Directory.Delete(cacheDir, true); } catch { }
        }
    }

    [Fact]
    public void ValidateManifest_DeletedFilesOnly_AboveThreshold_ReturnsMismatchTooLarge()
    {
        string sourceDir = CreateTempDir();
        string cacheDir = CreateTempDir();
        try
        {
            // Create 10 files
            for (int i = 0; i < 10; i++)
                WriteFile(sourceDir, $"File{i}.cs", $"class File{i} {{}}");

            var originalHashes = new Dictionary<string, string>();
            foreach (var file in Directory.GetFiles(sourceDir, "*.cs"))
            {
                string relativePath = Path.GetRelativePath(sourceDir, file);
                originalHashes[relativePath] = ComputeHash(file);
            }

            // Delete 2 files (now 8 total, 2 changed = 25% > 10%)
            File.Delete(Path.Combine(sourceDir, "File0.cs"));
            File.Delete(Path.Combine(sourceDir, "File1.cs"));

            string manifestPath = Path.Combine(cacheDir, ".cache-manifest.json");
            WriteManifest(manifestPath, sourceDir, originalHashes);

            var result = CreateCacheService().ValidateManifest(manifestPath, CreateSettings(sourceDir, cacheDir));
            Assert.Equal(CacheValidationResult.MismatchTooLarge, result);
        }
        finally
        {
            try { Directory.Delete(sourceDir, true); } catch { }
            try { Directory.Delete(cacheDir, true); } catch { }
        }
    }
}
