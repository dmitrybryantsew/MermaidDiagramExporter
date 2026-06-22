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
