using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MermaidDiagramExporter.Gui.Settings;

/// <summary>
/// Handles persistence of per-project settings to %LocalAppData%/MermaidDiagramExporter/.
/// Settings files are named by a stable hash of the source folder path.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Returns the base app data directory: %LocalAppData%/MermaidDiagramExporter/ on Windows,
    /// ~/.local/share/MermaidDiagramExporter/ on Linux.
    /// </summary>
    public static string GetAppDataDirectory()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appDir = Path.Combine(localAppData, "MermaidDiagramExporter");
        Directory.CreateDirectory(appDir);
        return appDir;
    }

    /// <summary>
    /// Returns the default cache directory for a given source folder.
    /// Format: %LocalAppData%/MermaidDiagramExporter/Caches/{ProjectName}_{FolderHash}/
    /// Returns the base app data directory if sourceFolderPath is empty
    /// (prevents ArgumentException from Path.GetFullPath on empty strings).
    /// </summary>
    public static string GetDefaultCacheDirectory(string sourceFolderPath)
    {
        string appDir = GetAppDataDirectory();
        if (string.IsNullOrWhiteSpace(sourceFolderPath))
            return Path.Combine(appDir, "Caches", "Untitled");
        string projectName = Path.GetFileName(sourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        string folderHash = ComputeFolderHash(sourceFolderPath);
        string cacheDir = Path.Combine(appDir, "Caches", $"{projectName}_{folderHash}");
        Directory.CreateDirectory(cacheDir);
        return cacheDir;
    }

    /// <summary>
    /// Returns the default source bundle directory.
    /// Format: {ScannedFolder}/.mermaid-export/
    /// </summary>
    public static string GetDefaultSourceBundleDirectory(string sourceFolderPath)
    {
        string bundleDir = Path.Combine(sourceFolderPath, ".mermaid-export");
        Directory.CreateDirectory(bundleDir);
        return bundleDir;
    }

    /// <summary>
    /// Resolves the effective cache directory for a project (custom or default).
    /// </summary>
    public string ResolveCacheDirectory(ProjectSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CustomCacheFolderPath))
            return settings.CustomCacheFolderPath;
        return GetDefaultCacheDirectory(settings.SourceFolderPath);
    }

    /// <summary>
    /// Resolves the effective source bundle directory for a project (custom or default).
    /// </summary>
    public string ResolveSourceBundleDirectory(ProjectSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.CustomSourceBundleFolderPath))
            return settings.CustomSourceBundleFolderPath;
        return GetDefaultSourceBundleDirectory(settings.SourceFolderPath);
    }

    /// <summary>
    /// Loads settings for a given source folder. Returns defaults if no settings exist yet.
    /// </summary>
    public ProjectSettings LoadSettings(string sourceFolderPath)
    {
        if (string.IsNullOrWhiteSpace(sourceFolderPath))
            return new ProjectSettings();

        string settingsPath = GetSettingsFilePath(sourceFolderPath);
        if (File.Exists(settingsPath))
        {
            try
            {
                string json = File.ReadAllText(settingsPath);
                var settings = JsonSerializer.Deserialize<ProjectSettings>(json, JsonOptions);
                if (settings != null)
                {
                    settings.SourceFolderPath = sourceFolderPath;
                    return settings;
                }
            }
            catch { /* fall through to defaults */ }
        }

        return new ProjectSettings { SourceFolderPath = sourceFolderPath };
    }

    /// <summary>
    /// Saves settings for a given source folder.
    /// </summary>
    public void SaveSettings(ProjectSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.SourceFolderPath))
            return;

        string settingsPath = GetSettingsFilePath(settings.SourceFolderPath);
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(settingsPath, json);
    }

    /// <summary>
    /// Gets the path to the settings JSON file for a source folder.
    /// Uses a stable hash of the folder path as the filename to avoid invalid chars.
    /// </summary>
    private static string GetSettingsFilePath(string sourceFolderPath)
    {
        string appDir = GetAppDataDirectory();
        string folderHash = ComputeFolderHash(sourceFolderPath);
        string projectName = Path.GetFileName(sourceFolderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return Path.Combine(appDir, $"{projectName}_{folderHash}.settings.json");
    }

    /// <summary>
    /// Computes a short stable hash of a folder path for use in filenames.
    /// Returns a fixed hash for empty paths to avoid ArgumentException.
    /// </summary>
    private static string ComputeFolderHash(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return "00000000";
        byte[] bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(folderPath).ToLowerInvariant());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..8];
    }
}
