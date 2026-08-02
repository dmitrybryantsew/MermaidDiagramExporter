using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MermaidDiagramExporter.Gui.Theming;

namespace MermaidDiagramExporter.Gui.Settings;

/// <summary>
/// Persists <see cref="AppSettings"/> to a single JSON file in the app data
/// directory (not per-project). Tolerates missing/corrupt files by returning
/// defaults.
/// </summary>
public sealed class AppSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// File path used by <see cref="Load"/> and <see cref="Save"/>.
    /// </summary>
    public static string GetSettingsFilePath()
    {
        string appDir = SettingsService.GetAppDataDirectory();
        return Path.Combine(appDir, "app.settings.json");
    }

    /// <summary>
    /// Loads app-global settings. Returns defaults if the file is missing or
    /// unparseable.
    /// </summary>
    public AppSettings Load() => LoadFrom(GetSettingsFilePath());

    /// <summary>
    /// Saves app-global settings. Silently no-ops on I/O failure (settings
    /// are non-critical; the defaults will be used next launch).
    /// </summary>
    public void Save(AppSettings settings) => SaveTo(GetSettingsFilePath(), settings);

    /// <summary>
    /// Loads settings from an explicit path. Used by tests; production code
    /// uses the parameterless overload.
    /// </summary>
    public AppSettings LoadFrom(string path)
    {
        if (!File.Exists(path))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            return settings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    /// <summary>
    /// Saves settings to an explicit path. Used by tests; production code
    /// uses the parameterless overload.
    /// </summary>
    public void SaveTo(string path, AppSettings settings)
    {
        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(path, json);
        }
        catch
        {
            // Intentionally swallowed: settings persistence is best-effort.
        }
    }
}
