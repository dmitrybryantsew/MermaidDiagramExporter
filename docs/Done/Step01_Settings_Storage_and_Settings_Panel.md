# Step 01: Settings Storage and Settings Panel

## Overview
Build the foundational settings infrastructure that all subsequent features depend on. This includes a per-project settings storage system (keyed by scanned folder path) and a dedicated Settings window/dialog. Settings are stored in `%LocalAppData%/MermaidDiagramExporter/` on Windows and `~/.local/share/MermaidDiagramExporter/` on Linux.

## Why This Comes First
- Step 02 (Project State Persistence) needs configurable cache and source-bundle locations
- Step 03 (Custom Stereotype Rules) stores user-defined regex patterns in settings
- Step 04 (Semantic Symbol Search) needs configurable search behavior
- Step 05 (Interactive Canvas) stores manual layout override preferences

---

## Files to Create

### 1. `src/MermaidDiagramExporter.Gui/Settings/ProjectSettings.cs`

Create this data model to hold all per-project settings:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace MermaidDiagramExporter.Gui.Settings;

/// <summary>
/// Per-project settings keyed by the scanned folder path.
/// Serialized to JSON and stored in the app data directory.
/// </summary>
public sealed class ProjectSettings
{
    /// <summary>
    /// The folder path that uniquely identifies this project.
    /// </summary>
    public string SourceFolderPath { get; set; } = string.Empty;

    /// <summary>
    /// The folder where cache files are stored. If empty, uses default app data location.
    /// </summary>
    public string? CustomCacheFolderPath { get; set; }

    /// <summary>
    /// The folder where source bundles (.txt) are saved. If empty, uses {ScannedFolder}/.mermaid-export/.
    /// </summary>
    public string? CustomSourceBundleFolderPath { get; set; }

    /// <summary>
    /// Whether to automatically save the relationship cache after each scan.
    /// </summary>
    public bool AutoSaveCache { get; set; } = true;

    /// <summary>
    /// Whether to automatically generate a source bundle after each scan.
    /// </summary>
    public bool AutoSaveSourceBundle { get; set; } = true;

    /// <summary>
    /// Whether to prompt to load from cache on reopening a project.
    /// </summary>
    public bool PromptToLoadCache { get; set; } = true;

    /// <summary>
    /// Whether the cache invalidation threshold (10% file change) should warn or auto-rescan.
    /// </summary>
    public CacheInvalidationMode CacheInvalidationBehavior { get; set; } = CacheInvalidationMode.WarnAndPrompt;

    /// <summary>
    /// Default search behavior: case-sensitive, match-kind, etc.
    /// </summary>
    public bool SearchCaseSensitive { get; set; } = false;

    /// <summary>
    /// Whether semantic search should include member names by default.
    /// </summary>
    public bool SearchIncludeMembers { get; set; } = true;

    /// <summary>
    /// Whether search results should automatically focus the canvas.
    /// </summary>
    public bool AutoFocusSearchResults { get; set; } = false;

    /// <summary>
    /// User-defined stereotype rules. These supplement the hardcoded Unity rules.
    /// </summary>
    public List<StereotypeRule> StereotypeRules { get; set; } = new();

    /// <summary>
    /// Whether to apply user-defined stereotype rules to node rendering.
    /// </summary>
    public bool ApplyCustomStereotypes { get; set; } = true;

    /// <summary>
    /// Whether manual layout overrides should be preserved across sessions.
    /// </summary>
    public bool PersistManualLayout { get; set; } = true;

    /// <summary>
    /// Whether to show the minimap by default.
    /// </summary>
    public bool ShowMinimap { get; set; } = true;

    /// <summary>
    /// Whether to enable drag-to-reposition on the canvas.
    /// </summary>
    public bool EnableNodeDragging { get; set; } = true;
}

public enum CacheInvalidationMode
{
    WarnAndPrompt,
    AutoRescan,
    Ignore
}

/// <summary>
/// A user-defined stereotype rule: regex pattern matched against type names.
/// </summary>
public sealed class StereotypeRule
{
    public string Pattern { get; set; } = ".*";
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "#4ECDC4";
}
```

### 2. `src/MermaidDiagramExporter.Gui/Settings/SettingsService.cs`

This is the storage/retrieval layer using JSON serialization:

```csharp
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
    /// </summary>
    public static string GetDefaultCacheDirectory(string sourceFolderPath)
    {
        string appDir = GetAppDataDirectory();
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
    /// </summary>
    private static string ComputeFolderHash(string folderPath)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(Path.GetFullPath(folderPath).ToLowerInvariant());
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash)[..8]; // first 8 chars of hex = 32-bit equivalent
    }
}
```

### 3. `src/MermaidDiagramExporter.Gui/Settings/SettingsWindow.axaml`

Create the Avalonia XAML for the settings dialog. Note: you will also need the code-behind `.axaml.cs`.

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MermaidDiagramExporter.Gui.Settings.SettingsWindow"
        Title="Project Settings"
        Width="520"
        Height="600"
        CanResize="False"
        WindowStartupLocation="CenterOwner">
  <Border Padding="20">
    <Grid RowDefinitions="*,Auto">
      <ScrollViewer Grid.Row="0">
        <StackPanel Spacing="16">
          <!-- Project Identification -->
          <TextBlock Text="Project" FontWeight="Bold" FontSize="16" />
          <Grid ColumnDefinitions="Auto,*">
            <TextBlock Grid.Column="0" Text="Source Folder:" VerticalAlignment="Center" Width="120" />
            <TextBlock Grid.Column="1" x:Name="SourceFolderText" TextWrapping="Wrap" Opacity="0.7" />
          </Grid>

          <!-- Cache Settings -->
          <TextBlock Text="Cache" FontWeight="Bold" FontSize="16" Margin="0,8,0,0" />
          <CheckBox x:Name="AutoSaveCacheCheck" Content="Auto-save relationship cache after scan" />
          <CheckBox x:Name="AutoSaveSourceBundleCheck" Content="Auto-save source bundle after scan" />
          <CheckBox x:Name="PromptLoadCacheCheck" Content="Prompt to load from cache when reopening project" />
          <Grid ColumnDefinitions="Auto,*">
            <TextBlock Grid.Column="0" Text="On source change:" VerticalAlignment="Center" Width="120" />
            <ComboBox x:Name="CacheInvalidationCombo" Grid.Column="1" Width="260" HorizontalAlignment="Left">
              <ComboBoxItem Content="Warn and prompt to rescan" />
              <ComboBoxItem Content="Automatically rescan" />
              <ComboBoxItem Content="Ignore (use cache regardless)" />
            </ComboBox>
          </Grid>
          <Grid ColumnDefinitions="Auto,*,Auto">
            <TextBlock Grid.Column="0" Text="Cache folder:" VerticalAlignment="Center" Width="120" />
            <TextBox Grid.Column="1" x:Name="CacheFolderText" Watermark="(default)" IsReadOnly="True" />
            <Button Grid.Column="2" x:Name="BrowseCacheButton" Content="Browse..." Margin="8,0,0,0" />
          </Grid>
          <Grid ColumnDefinitions="Auto,*,Auto">
            <TextBlock Grid.Column="0" Text="Bundle folder:" VerticalAlignment="Center" Width="120" />
            <TextBox Grid.Column="1" x:Name="BundleFolderText" Watermark="(default)" IsReadOnly="True" />
            <Button Grid.Column="2" x:Name="BrowseBundleButton" Content="Browse..." Margin="8,0,0,0" />
          </Grid>

          <!-- Search Settings -->
          <TextBlock Text="Search" FontWeight="Bold" FontSize="16" Margin="0,8,0,0" />
          <CheckBox x:Name="SearchCaseSensitiveCheck" Content="Case-sensitive search" />
          <CheckBox x:Name="SearchIncludeMembersCheck" Content="Include member names in semantic search" />
          <CheckBox x:Name="AutoFocusSearchCheck" Content="Auto-focus canvas on search results" />

          <!-- Stereotype Settings -->
          <TextBlock Text="Stereotypes" FontWeight="Bold" FontSize="16" Margin="0,8,0,0" />
          <CheckBox x:Name="ApplyCustomStereotypesCheck" Content="Apply custom stereotype rules" />

          <!-- Canvas Settings -->
          <TextBlock Text="Canvas" FontWeight="Bold" FontSize="16" Margin="0,8,0,0" />
          <CheckBox x:Name="PersistLayoutCheck" Content="Persist manual layout positions" />
          <CheckBox x:Name="EnableDraggingCheck" Content="Enable node drag-to-reposition" />
          <CheckBox x:Name="ShowMinimapCheck" Content="Show minimap" />
        </StackPanel>
      </ScrollViewer>

      <!-- Buttons -->
      <StackPanel Grid.Row="1" Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8" Margin="0,16,0,0">
        <Button x:Name="ResetDefaultsButton" Content="Reset Defaults" />
        <Button x:Name="CancelButton" Content="Cancel" />
        <Button x:Name="SaveButton" Content="Save" />
      </StackPanel>
    </Grid>
  </Border>
</Window>
```

### 4. `src/MermaidDiagramExporter.Gui/Settings/SettingsWindow.axaml.cs`

```csharp
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;

namespace MermaidDiagramExporter.Gui.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService = new();
    private ProjectSettings _settings = new();

    public SettingsWindow()
    {
        InitializeComponent();
        WireEvents();
    }

    private void WireEvents()
    {
        SaveButton.Click += OnSave;
        CancelButton.Click += OnCancel;
        ResetDefaultsButton.Click += OnResetDefaults;
        BrowseCacheButton.Click += async (s, e) => await BrowseFolder(CacheFolderText);
        BrowseBundleButton.Click += async (s, e) => await BrowseFolder(BundleFolderText);
    }

    /// <summary>
    /// Call this BEFORE ShowDialog(). Loads settings for the given source folder into the UI.
    /// </summary>
    public void LoadForProject(string sourceFolderPath)
    {
        _settings = _settingsService.LoadSettings(sourceFolderPath);
        SourceFolderText.Text = _settings.SourceFolderPath;
        AutoSaveCacheCheck.IsChecked = _settings.AutoSaveCache;
        AutoSaveSourceBundleCheck.IsChecked = _settings.AutoSaveSourceBundle;
        PromptLoadCacheCheck.IsChecked = _settings.PromptToLoadCache;
        CacheInvalidationCombo.SelectedIndex = (int)_settings.CacheInvalidationBehavior;
        CacheFolderText.Text = _settings.CustomCacheFolderPath ?? "";
        BundleFolderText.Text = _settings.CustomSourceBundleFolderPath ?? "";
        SearchCaseSensitiveCheck.IsChecked = _settings.SearchCaseSensitive;
        SearchIncludeMembersCheck.IsChecked = _settings.SearchIncludeMembers;
        AutoFocusSearchCheck.IsChecked = _settings.AutoFocusSearchResults;
        ApplyCustomStereotypesCheck.IsChecked = _settings.ApplyCustomStereotypes;
        PersistLayoutCheck.IsChecked = _settings.PersistManualLayout;
        EnableDraggingCheck.IsChecked = _settings.EnableNodeDragging;
        ShowMinimapCheck.IsChecked = _settings.ShowMinimap;
    }

    /// <summary>
    /// The settings that were saved (or null if cancelled).
    /// Check this after the dialog closes.
    /// </summary>
    public ProjectSettings? SavedSettings { get; private set; }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        _settings.AutoSaveCache = AutoSaveCacheCheck.IsChecked == true;
        _settings.AutoSaveSourceBundle = AutoSaveSourceBundleCheck.IsChecked == true;
        _settings.PromptToLoadCache = PromptLoadCacheCheck.IsChecked == true;
        _settings.CacheInvalidationBehavior = (CacheInvalidationMode)Math.Clamp(CacheInvalidationCombo.SelectedIndex, 0, 2);
        _settings.CustomCacheFolderPath = string.IsNullOrWhiteSpace(CacheFolderText.Text) ? null : CacheFolderText.Text;
        _settings.CustomSourceBundleFolderPath = string.IsNullOrWhiteSpace(BundleFolderText.Text) ? null : BundleFolderText.Text;
        _settings.SearchCaseSensitive = SearchCaseSensitiveCheck.IsChecked == true;
        _settings.SearchIncludeMembers = SearchIncludeMembersCheck.IsChecked == true;
        _settings.AutoFocusSearchResults = AutoFocusSearchCheck.IsChecked == true;
        _settings.ApplyCustomStereotypes = ApplyCustomStereotypesCheck.IsChecked == true;
        _settings.PersistManualLayout = PersistLayoutCheck.IsChecked == true;
        _settings.EnableNodeDragging = EnableDraggingCheck.IsChecked == true;
        _settings.ShowMinimap = ShowMinimapCheck.IsChecked == true;

        _settingsService.SaveSettings(_settings);
        SavedSettings = _settings;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        SavedSettings = null;
        Close();
    }

    private void OnResetDefaults(object? sender, RoutedEventArgs e)
    {
        AutoSaveCacheCheck.IsChecked = true;
        AutoSaveSourceBundleCheck.IsChecked = true;
        PromptLoadCacheCheck.IsChecked = true;
        CacheInvalidationCombo.SelectedIndex = 0;
        CacheFolderText.Text = "";
        BundleFolderText.Text = "";
        SearchCaseSensitiveCheck.IsChecked = false;
        SearchIncludeMembersCheck.IsChecked = true;
        AutoFocusSearchCheck.IsChecked = false;
        ApplyCustomStereotypesCheck.IsChecked = true;
        PersistLayoutCheck.IsChecked = true;
        EnableDraggingCheck.IsChecked = true;
        ShowMinimapCheck.IsChecked = true;
    }

    private async Task BrowseFolder(TextBox targetTextBox)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select folder",
            AllowMultiple = false
        });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path)
        {
            targetTextBox.Text = path;
        }
    }
}
```

---

## Files to Modify

### 5. `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`

Add a `_settingsService` field, a `CurrentSettings` property, and hook settings into the scan flow.

**Add field:**
```csharp
private readonly SettingsService _settingsService = new();
private ProjectSettings _currentSettings = new();
```

**Add public property (other features will read this):**
```csharp
public ProjectSettings CurrentSettings => _currentSettings;
```

**Modify `OnScan()`** — after a successful scan, load/save settings:
```csharp
private void OnScan(object? sender, RoutedEventArgs e)
{
    var folder = FolderTextBox.Text?.Trim();
    // ... existing validation ...

    try
    {
        // Load settings for this project before scanning
        _currentSettings = _settingsService.LoadSettings(folder);

        _currentGraph = _scanner.ScanFolder(folder);
        // ... existing focus setup ...

        SetDisplayedGraph(_currentGraph);
        // ... existing render and stats update ...

        // Persist settings if this is a new project
        _settingsService.SaveSettings(_currentSettings);
    }
    catch (Exception ex)
    {
        StatsText.Text = $"Error: {ex.Message}";
    }
}
```

**Add a Settings menu/button handler:**
```csharp
private async void OnOpenSettings(object? sender, RoutedEventArgs e)
{
    string folder = FolderTextBox.Text?.Trim() ?? "";
    if (string.IsNullOrEmpty(folder))
    {
        StatsText.Text = "Select a folder first to configure project settings";
        return;
    }

    var window = new SettingsWindow();
    window.LoadForProject(folder);
    await window.ShowDialog(this);

    if (window.SavedSettings != null)
    {
        _currentSettings = window.SavedSettings;
        StatsText.Text = "Settings saved";
    }
}
```

### 6. `src/MermaidDiagramExporter.Gui/App.axaml`

If you have a menu or toolbar defined in XAML, add a "Settings" button that binds to `OnOpenSettings`. If the XAML uses a simple button panel, add:

```xml
<Button x:Name="SettingsButton" Content="Settings" Click="OnOpenSettings" />
```

If the buttons are wired in code, add the event wire-up in the MainWindow constructor or `InitializeComponent` override.

---

## Integration Points for Future Steps

| Feature | How It Reads Settings |
|---------|----------------------|
| Step 02 (Cache) | Reads `_currentSettings.CustomCacheFolderPath`, `AutoSaveCache`, `PromptToLoadCache` |
| Step 02 (Source Bundle) | Reads `_currentSettings.CustomSourceBundleFolderPath`, `AutoSaveSourceBundle` |
| Step 03 (Stereotypes) | Reads `_currentSettings.StereotypeRules`, `ApplyCustomStereotypes` |
| Step 04 (Search) | Reads `_currentSettings.SearchCaseSensitive`, `SearchIncludeMembers`, `AutoFocusSearchResults` |
| Step 05 (Canvas Drag) | Reads `_currentSettings.EnableNodeDragging`, `PersistManualLayout` |
| Step 06 (Minimap) | Reads `_currentSettings.ShowMinimap` |

---

## Testing Checklist

1. Open the app, select a folder, click "Settings" — the dialog should open with defaults
2. Change "Cache folder" via Browse button, click Save, reopen — the path should persist
3. Check `%LocalAppData%/MermaidDiagramExporter/` (Windows) or `~/.local/share/MermaidDiagramExporter/` (Linux) — a `.settings.json` file should exist
4. The settings filename should contain the project name and an 8-char hash of the folder path
5. Verify the "Reset Defaults" button restores all checkboxes to their default state
6. Verify Cancel discards changes
