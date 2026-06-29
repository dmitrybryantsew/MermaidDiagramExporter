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

    /// <summary>
    /// Whether to use the experimental compound layout engine (unified node+border-dummy
    /// ranking, cluster-contiguity ordering). Default false until validated per docs/09.
    /// When false, the original cluster-as-supernode layered engine is used.
    /// </summary>
    public bool UseCompoundLayoutEngine { get; set; } = false;

    /// <summary>
    /// Whether to use the MSAGL-based layout engine (Microsoft Automatic Graph
    /// Layout, Sugiyama framework with native cluster + orthogonal routing
    /// support). Prototype — default false while being evaluated. When true,
    /// UseCompoundLayoutEngine is ignored.
    /// </summary>
    public bool UseMsaglEngine { get; set; } = false;

    /// <summary>
    /// Per-kind edge visual style (color + arrowhead shape). Null = use built-in
    /// UML defaults.
    /// </summary>
    public EdgeStyleSettings? EdgeStyles { get; set; } = null;

    /// <summary>
    /// User-defined keyboard shortcut bindings for Design Mode tools.
    /// Keys are tool names (e.g. "Class", "EdgeInheritance"), values are key names
    /// (e.g. "C", "H"). Only bindings that differ from defaults need to be stored.
    /// </summary>
    public Dictionary<string, string> DesignShortcutBindings { get; set; } = new();
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
