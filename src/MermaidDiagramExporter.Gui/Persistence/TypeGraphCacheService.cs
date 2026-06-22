using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Saves manual layout overrides to a companion file in the cache directory.
    /// </summary>
    public void SaveManualOverrides(ManualLayoutOverrides overrides, ProjectSettings settings)
    {
        if (overrides == null || !overrides.HasOverrides) return;

        string cacheDir = _settingsService.ResolveCacheDirectory(settings);
        string path = Path.Combine(cacheDir, "layout.overrides.json");
        overrides.LastSavedUtc = DateTime.UtcNow;
        var json = JsonSerializer.Serialize(overrides, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new Vector2JsonConverter() }
        });
        File.WriteAllText(path, json);
    }

    /// <summary>
    /// Loads manual layout overrides from the cache directory.
    /// </summary>
    public ManualLayoutOverrides LoadManualOverrides(ProjectSettings settings)
    {
        string cacheDir = _settingsService.ResolveCacheDirectory(settings);
        string path = Path.Combine(cacheDir, "layout.overrides.json");
        if (!File.Exists(path)) return new ManualLayoutOverrides();

        try
        {
            var json = File.ReadAllText(path);
            var overrides = JsonSerializer.Deserialize<ManualLayoutOverrides>(json, new JsonSerializerOptions
            {
                Converters = { new Vector2JsonConverter() }
            });
            return overrides ?? new ManualLayoutOverrides();
        }
        catch { return new ManualLayoutOverrides(); }
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
        return Convert.ToHexString(hash)[..16];
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
