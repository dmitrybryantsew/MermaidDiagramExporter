# Step 02: Project State Persistence (Relationship Cache + Source Bundle)

## Overview
After each scan, serialize the `TypeGraph` to a cache file for instant reload. Also generate a consolidated `.txt` file of all scanned `.cs` source code as ready-to-paste LLM context. Cache locations respect the Settings from Step 01.

## Dependencies
- **Step 01 (Settings)** — must be implemented first. Uses `SettingsService`, `ProjectSettings`, cache/bundle folder paths.

---

## Part A: Cache Manifest Model

### 1. Create `src/MermaidDiagramExporter.Gui/Persistence/CacheManifest.cs`

```csharp
using System;
using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Persistence;

/// <summary>
/// Stored alongside the cache file to enable source-change detection.
/// </summary>
public sealed class CacheManifest
{
    public string SourceFolder { get; set; } = string.Empty;
    public DateTime LastScanUtc { get; set; }
    public Dictionary<string, string> FileHashes { get; set; } = new();
    public int TotalFiles { get; set; }
}
```

---

## Part B: TypeGraph Binary Cache

### 2. Create `src/MermaidDiagramExporter.Gui/Persistence/TypeGraphCacheService.cs`

Uses `System.IO.Compression` + `System.Text.Json` for compact storage:

```csharp
using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Persistence;

/// <summary>
/// Handles serialization and deserialization of the TypeGraph to/from compressed cache files.
/// Also manages cache manifest for source-change detection.
/// </summary>
public sealed class TypeGraphCacheService
{
    private const string CacheFileName = "typegraph.cache.bin";
    private const string ManifestFileName = ".cache-manifest.json";
    private readonly SettingsService _settingsService = new();

    /// <summary>
    /// Returns true if a valid cache exists for the given project settings.
    /// </summary>
    public bool CacheExists(ProjectSettings settings)
    {
        string cacheDir = _settingsService.ResolveCacheDirectory(settings);
        return File.Exists(Path.Combine(cacheDir, CacheFileName))
            && File.Exists(Path.Combine(cacheDir, ManifestFileName));
    }

    /// <summary>
    /// Returns metadata about the existing cache (timestamp, file count) for UI display.
    /// </summary>
    public CacheInfo? GetCacheInfo(ProjectSettings settings)
    {
        string cacheDir = _settingsService.ResolveCacheDirectory(settings);
        string manifestPath = Path.Combine(cacheDir, ManifestFileName);
        if (!File.Exists(manifestPath))
            return null;

        try
        {
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<CacheManifest>(json);
            if (manifest == null) return null;

            return new CacheInfo
            {
                LastScanUtc = manifest.LastScanUtc,
                TotalFiles = manifest.TotalFiles,
                CacheFilePath = Path.Combine(cacheDir, CacheFileName)
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// Saves the TypeGraph to a compressed cache file and writes the manifest.
    /// Call this after a successful scan.
    /// </summary>
    public void SaveCache(TypeGraph graph, ProjectSettings settings)
    {
        string cacheDir = _settingsService.ResolveCacheDirectory(settings);
        Directory.CreateDirectory(cacheDir);

        string cachePath = Path.Combine(cacheDir, CacheFileName);
        string manifestPath = Path.Combine(cacheDir, ManifestFileName);

        // Serialize TypeGraph to JSON then compress with GZip
        string graphJson = SerializeTypeGraph(graph);
        byte[] jsonBytes = Encoding.UTF8.GetBytes(graphJson);
        using (var fs = new FileStream(cachePath, FileMode.Create, FileAccess.Write))
        using (var gzip = new GZipStream(fs, CompressionLevel.Optimal))
        {
            gzip.Write(jsonBytes, 0, jsonBytes.Length);
        }

        // Build manifest with file hashes
        var manifest = BuildManifest(settings.SourceFolderPath);
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>
    /// Loads the TypeGraph from cache if available and valid.
    /// Returns null if cache is missing, corrupt, or source changed significantly.
    /// </summary>
    public TypeGraph? LoadCache(ProjectSettings settings)
    {
        string cacheDir = _settingsService.ResolveCacheDirectory(settings);
        string cachePath = Path.Combine(cacheDir, CacheFileName);
        string manifestPath = Path.Combine(cacheDir, ManifestFileName);

        if (!File.Exists(cachePath) || !File.Exists(manifestPath))
            return null;

        // Validate manifest against current source files
        var validation = ValidateManifest(manifestPath, settings);
        if (validation == CacheValidationResult.MismatchTooLarge)
            return null;

        try
        {
            using var fs = new FileStream(cachePath, FileMode.Open, FileAccess.Read);
            using var gzip = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            string graphJson = reader.ReadToEnd();
            return DeserializeTypeGraph(graphJson);
        }
        catch { return null; }
    }

    /// <summary>
    /// Checks current source files against the manifest without loading the cache.
    /// </summary>
    public CacheValidationResult ValidateManifest(string manifestPath, ProjectSettings settings)
    {
        try
        {
            string json = File.ReadAllText(manifestPath);
            var manifest = JsonSerializer.Deserialize<CacheManifest>(json);
            if (manifest == null) return CacheValidationResult.NoManifest;

            var currentFiles = GetSourceFileHashes(settings.SourceFolderPath);
            int totalFiles = currentFiles.Count;
            if (totalFiles == 0) return CacheValidationResult.NoSourceFiles;

            int changedFiles = 0;
            foreach (var kvp in currentFiles)
            {
                if (!manifest.FileHashes.TryGetValue(kvp.Key, out string? oldHash) || oldHash != kvp.Value)
                    changedFiles++;
            }
            // Also count deleted files
            foreach (var kvp in manifest.FileHashes)
            {
                if (!currentFiles.ContainsKey(kvp.Key))
                    changedFiles++;
            }

            float changeRatio = totalFiles > 0 ? (float)changedFiles / totalFiles : 1f;

            if (changeRatio == 0) return CacheValidationResult.UpToDate;
            if (changeRatio <= 0.10f) return CacheValidationResult.MinorChanges;
            return CacheValidationResult.MismatchTooLarge;
        }
        catch { return CacheValidationResult.Corrupt; }
    }

    // ---------------- Private helpers ----------------

    private CacheManifest BuildManifest(string sourceFolderPath)
    {
        var fileHashes = GetSourceFileHashes(sourceFolderPath);
        return new CacheManifest
        {
            SourceFolder = Path.GetFullPath(sourceFolderPath),
            LastScanUtc = DateTime.UtcNow,
            FileHashes = fileHashes,
            TotalFiles = fileHashes.Count
        };
    }

    private Dictionary<string, string> GetSourceFileHashes(string sourceFolderPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(sourceFolderPath))
            return result;

        foreach (var file in Directory.EnumerateFiles(sourceFolderPath, "*.cs", SearchOption.AllDirectories).OrderBy(f => f))
        {
            try
            {
                string relativePath = Path.GetRelativePath(sourceFolderPath, file);
                string hash = ComputeFileHash(file);
                result[relativePath] = hash;
            }
            catch { /* skip unreadable files */ }
        }
        return result;
    }

    private static string ComputeFileHash(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash)[..16]; // 64 chars -> 16 chars is enough
    }

    /// <summary>
    /// Serializes TypeGraph to JSON. Uses a custom DTO shape for clean round-tripping.
    /// </summary>
    private static string SerializeTypeGraph(TypeGraph graph)
    {
        var dto = new TypeGraphDto
        {
            Title = graph.Title,
            Metadata = graph.Metadata,
            Nodes = graph.Nodes.Select(n => new TypeNodeDataDto
            {
                Id = n.Id,
                DisplayName = n.DisplayName,
                FullName = n.FullName,
                Namespace = n.Namespace,
                AssemblyName = n.AssemblyName,
                AssetPath = n.AssetPath,
                Kind = n.Kind,
                IsProjectType = n.IsProjectType,
                Stereotypes = n.Stereotypes.ToList(),
                Members = n.Members.Select(m => new TypeMemberDataDto
                {
                    Name = m.Name,
                    TypeName = m.TypeName,
                    Kind = m.Kind,
                    Visibility = m.Visibility,
                    IsStatic = m.IsStatic,
                    IsAbstract = m.IsAbstract,
                    Parameters = m.Parameters.Select(p => new TypeMemberParameterDataDto
                    {
                        Name = p.Name,
                        TypeName = p.TypeName
                    }).ToList()
                }).ToList()
            }).ToList(),
            Edges = graph.Edges.Select(e => new TypeEdgeDataDto
            {
                FromNodeId = e.FromNodeId,
                ToNodeId = e.ToNodeId,
                Kind = e.Kind,
                Label = e.Label,
                IsStrongRelation = e.IsStrongRelation
            }).ToList(),
            Groups = graph.Groups.Select(g => new TypeGroupDataDto
            {
                Id = g.Id,
                Label = g.Label,
                Kind = g.Kind,
                ParentGroupId = g.ParentGroupId,
                NodeIds = g.NodeIds.ToList()
            }).ToList()
        };

        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = false });
    }

    private static TypeGraph DeserializeTypeGraph(string json)
    {
        var dto = JsonSerializer.Deserialize<TypeGraphDto>(json);
        if (dto == null) throw new InvalidOperationException("Failed to deserialize TypeGraph");

        var nodes = dto.Nodes.Select(n => new TypeNodeData
        {
            Id = n.Id,
            DisplayName = n.DisplayName,
            FullName = n.FullName,
            Namespace = n.Namespace,
            AssemblyName = n.AssemblyName,
            AssetPath = n.AssetPath,
            Kind = n.Kind,
            IsProjectType = n.IsProjectType,
            Stereotypes = n.Stereotypes,
            Members = n.Members.Select(m => new TypeMemberData
            {
                Name = m.Name,
                TypeName = m.TypeName,
                Kind = m.Kind,
                Visibility = m.Visibility,
                IsStatic = m.IsStatic,
                IsAbstract = m.IsAbstract,
                Parameters = m.Parameters.Select(p => new TypeMemberParameterData
                {
                    Name = p.Name,
                    TypeName = p.TypeName
                }).ToList()
            }).ToList()
        }).ToList();

        var edges = dto.Edges.Select(e => new TypeEdgeData
        {
            FromNodeId = e.FromNodeId,
            ToNodeId = e.ToNodeId,
            Kind = e.Kind,
            Label = e.Label,
            IsStrongRelation = e.IsStrongRelation
        }).ToList();

        var groups = dto.Groups.Select(g => new TypeGroupData
        {
            Id = g.Id,
            Label = g.Label,
            Kind = g.Kind,
            ParentGroupId = g.ParentGroupId,
            NodeIds = g.NodeIds
        }).ToList();

        return new TypeGraph(dto.Title, nodes, edges, groups, dto.Metadata);
    }
}

// ---------- DTOs for clean JSON serialization ----------

public sealed class TypeGraphDto
{
    public string Title { get; set; } = "";
    public TypeGraphMetadata Metadata { get; set; } = new();
    public List<TypeNodeDataDto> Nodes { get; set; } = new();
    public List<TypeEdgeDataDto> Edges { get; set; } = new();
    public List<TypeGroupDataDto> Groups { get; set; } = new();
}

public sealed class TypeNodeDataDto
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string AssemblyName { get; set; } = "";
    public string AssetPath { get; set; } = "";
    public TypeNodeKind Kind { get; set; }
    public bool IsProjectType { get; set; }
    public List<string> Stereotypes { get; set; } = new();
    public List<TypeMemberDataDto> Members { get; set; } = new();
}

public sealed class TypeMemberDataDto
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public TypeMemberKind Kind { get; set; }
    public TypeVisibility Visibility { get; set; }
    public bool IsStatic { get; set; }
    public bool IsAbstract { get; set; }
    public List<TypeMemberParameterDataDto> Parameters { get; set; } = new();
}

public sealed class TypeMemberParameterDataDto
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
}

public sealed class TypeEdgeDataDto
{
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public TypeEdgeKind Kind { get; set; }
    public string Label { get; set; } = "";
    public bool IsStrongRelation { get; set; }
}

public sealed class TypeGroupDataDto
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public TypeGroupKind Kind { get; set; }
    public string ParentGroupId { get; set; } = "";
    public List<string> NodeIds { get; set; } = new();
}

public enum CacheValidationResult
{
    UpToDate,
    MinorChanges,
    MismatchTooLarge,
    NoManifest,
    Corrupt,
    NoSourceFiles
}

public sealed class CacheInfo
{
    public DateTime LastScanUtc { get; set; }
    public int TotalFiles { get; set; }
    public string CacheFilePath { get; set; } = "";
}
```

---

## Part C: Source Bundle Generator

### 3. Create `src/MermaidDiagramExporter.Gui/Persistence/SourceBundleService.cs`

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Persistence;

/// <summary>
/// Generates a consolidated .txt file containing all scanned .cs files with headers.
/// Ready to paste into LLM prompts for context.
/// </summary>
public sealed class SourceBundleService
{
    private readonly SettingsService _settingsService = new();

    /// <summary>
    /// Generates a source bundle and returns the path to the created file.
    /// The filename includes a timestamp: source-bundle-{yyyyMMdd_HHmmss}.txt
    /// </summary>
    public string GenerateBundle(string sourceFolderPath, ProjectSettings settings)
    {
        string outputDir = _settingsService.ResolveSourceBundleDirectory(settings);
        Directory.CreateDirectory(outputDir);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePath = Path.Combine(outputDir, $"source-bundle-{timestamp}.txt");

        var sourceFiles = Directory.EnumerateFiles(sourceFolderPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Source Bundle: {Path.GetFileName(sourceFolderPath)}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:O}");
        sb.AppendLine($"# Files: {sourceFiles.Count}");
        sb.AppendLine($"# Folder: {Path.GetFullPath(sourceFolderPath)}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (var file in sourceFiles)
        {
            string relativePath = Path.GetRelativePath(sourceFolderPath, file);
            sb.AppendLine($"--- FILE: {relativePath} ---");
            try
            {
                string content = File.ReadAllText(file);
                sb.AppendLine(content);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Error reading file: {ex.Message}]");
            }
            sb.AppendLine();
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }
}
```

---

## Part D: Cache Prompt Dialog

### 4. Create `src/MermaidDiagramExporter.Gui/Persistence/CachePromptDialog.axaml`

Simple dialog asking the user whether to load from cache or rescan:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="MermaidDiagramExporter.Gui.Persistence.CachePromptDialog"
        Title="Cache Available"
        Width="420"
        Height="220"
        CanResize="False"
        WindowStartupLocation="CenterOwner">
  <Border Padding="20">
    <StackPanel Spacing="12">
      <TextBlock Text="A cached scan was found for this project." FontWeight="Bold" />
      <TextBlock x:Name="CacheInfoText" Opacity="0.7" TextWrapping="Wrap" />
      <TextBlock x:Name="ValidationText" TextWrapping="Wrap" />
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8" Margin="0,12,0,0">
        <Button x:Name="RescanButton" Content="Rescan" />
        <Button x:Name="LoadCacheButton" Content="Load from Cache" />
      </StackPanel>
    </StackPanel>
  </Border>
</Window>
```

### 5. Create `src/MermaidDiagramExporter.Gui/Persistence/CachePromptDialog.axaml.cs`

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MermaidDiagramExporter.Gui.Persistence;

public partial class CachePromptDialog : Window
{
    public CachePromptDialog()
    {
        InitializeComponent();
        RescanButton.Click += (s, e) => { Result = CachePromptResult.Rescan; Close(); };
        LoadCacheButton.Click += (s, e) => { Result = CachePromptResult.LoadCache; Close(); };
    }

    public CachePromptResult Result { get; private set; } = CachePromptResult.Cancelled;

    public void SetInfo(CacheInfo info, CacheValidationResult validation)
    {
        CacheInfoText.Text = $"Last scan: {info.LastScanUtc:yyyy-MM-dd HH:mm UTC} | Files: {info.TotalFiles}";
        ValidationText.Text = validation switch
        {
            CacheValidationResult.UpToDate => "Source files are unchanged.",
            CacheValidationResult.MinorChanges => "Minor source changes detected (under 10%).",
            _ => "Source has changed significantly. Rescan recommended."
        };
    }
}

public enum CachePromptResult
{
    Rescan,
    LoadCache,
    Cancelled
}
```

---

## Part E: Integrate into MainWindow

### 6. Modify `MainWindow.axaml.cs`

**Add fields:**
```csharp
private readonly TypeGraphCacheService _cacheService = new();
private readonly SourceBundleService _bundleService = new();
```

**Modify `OnScan()`** to support cache load and auto-save:

```csharp
private async void OnScan(object? sender, RoutedEventArgs e)
{
    var folder = FolderTextBox.Text?.Trim();
    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
    {
        StatsText.Text = "Invalid folder";
        return;
    }

    try
    {
        // Load settings
        _currentSettings = _settingsService.LoadSettings(folder);

        // Check cache and prompt if available
        bool useCache = false;
        if (_currentSettings.PromptToLoadCache && _cacheService.CacheExists(_currentSettings))
        {
            var cacheInfo = _cacheService.GetCacheInfo(_currentSettings);
            var manifestPath = Path.Combine(
                new SettingsService().ResolveCacheDirectory(_currentSettings),
                ".cache-manifest.json");
            var validation = _cacheService.ValidateManifest(manifestPath, _currentSettings);

            if (validation == CacheValidationResult.UpToDate || validation == CacheValidationResult.MinorChanges)
            {
                var dialog = new CachePromptDialog();
                if (cacheInfo != null)
                    dialog.SetInfo(cacheInfo, validation);
                await dialog.ShowDialog(this);
                useCache = dialog.Result == CachePromptResult.LoadCache;
                if (dialog.Result == CachePromptResult.Cancelled)
                    return; // user cancelled
            }
        }

        TypeGraph graph;
        if (useCache)
        {
            var cached = _cacheService.LoadCache(_currentSettings);
            if (cached != null)
            {
                graph = cached;
                StatsText.Text = "Loaded from cache";
            }
            else
            {
                // Fallback to scan
                graph = _scanner.ScanFolder(folder);
            }
        }
        else
        {
            graph = _scanner.ScanFolder(folder);
        }

        _currentGraph = graph;
        _focusNavigationController.SetRootGraph(_currentGraph, folder);
        _seedSelectionState.Clear();

        SetDisplayedGraph(_currentGraph);
        GraphCanvasView.WaitForRender();
        UpdateStats(_currentGraph);

        // Auto-save cache if enabled
        if (_currentSettings.AutoSaveCache)
        {
            _cacheService.SaveCache(_currentGraph, _currentSettings);
        }

        // Auto-save source bundle if enabled
        if (_currentSettings.AutoSaveSourceBundle)
        {
            string bundlePath = _bundleService.GenerateBundle(folder, _currentSettings);
            StatsText.Text += $" | Bundle: {Path.GetFileName(bundlePath)}";
        }

        // Save settings (ensures project is registered)
        _settingsService.SaveSettings(_currentSettings);

        // Save PNG screenshot (existing behavior)
        var exportDir = Path.Combine(AppContext.BaseDirectory, "export");
        Directory.CreateDirectory(exportDir);
        var pngPath = Path.Combine(exportDir, $"diagram_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        GraphCanvasView.SaveToPng(pngPath);
        StatsText.Text += $" | Saved: {Path.GetFileName(pngPath)}";
    }
    catch (Exception ex)
    {
        StatsText.Text = $"Error: {ex.Message}";
    }
}
```

---

## Testing Checklist

1. **Cache Save**: Scan a folder with `AutoSaveCache=true`. Verify `%LocalAppData%/MermaidDiagramExporter/Caches/{Project}_{Hash}/typegraph.cache.bin` exists. Verify `.cache-manifest.json` exists alongside it.
2. **Cache Load**: Reopen the same folder. The cache prompt should appear. Click "Load from Cache" — graph should load instantly (no Roslyn scan).
3. **Cache Validation**: Edit one `.cs` file, reopen the project. Should show "Minor source changes detected." Edit many files — should show mismatch and skip the prompt (auto-rescan).
4. **Source Bundle**: Scan with `AutoSaveSourceBundle=true`. Verify `{Folder}/.mermaid-export/source-bundle-{timestamp}.txt` exists with all `.cs` files concatenated with `--- FILE: ... ---` headers.
5. **Custom paths**: Change cache/bundle folders in Settings, rescan — verify files go to the custom locations.
6. **Bundle content**: Open the `.txt` — should start with `# Source Bundle:` header, contain all source files in relative-path order.
