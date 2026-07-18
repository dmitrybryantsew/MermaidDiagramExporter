using System;
using System.Collections.Generic;
using System.IO;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Llm;

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
    /// Which layout engine to use (Layered / Compound / MSAGL). Replaces the
    /// legacy UseCompoundLayoutEngine / UseMsaglEngine boolean pair.
    /// </summary>
    public LayoutEngineKind Engine { get; set; } = LayoutEngineKind.Layered;

    /// <summary>
    /// MSAGL-engine-specific layout settings. Applies only when
    /// <see cref="Engine"/> is <see cref="LayoutEngineKind.Msagl"/>.
    /// </summary>
    public MsaglLayoutSettings Msagl { get; set; } = new();

    // ── Legacy layout flags (kept only to migrate old settings JSON) ──
    // Normalize() folds these into Engine / Msagl.PartitionMode and resets
    // them to false, so re-saved files carry only the new shape.

    [Obsolete("Use Engine. Kept for deserializing old settings files.")]
    public bool UseCompoundLayoutEngine { get; set; } = false;

    [Obsolete("Use Engine. Kept for deserializing old settings files.")]
    public bool UseMsaglEngine { get; set; } = false;

    [Obsolete("Use Msagl.PartitionMode. Kept for deserializing old settings files.")]
    public bool SeparateAppAndTests { get; set; } = false;

    [Obsolete("Use Msagl.PartitionMode. Kept for deserializing old settings files.")]
    public bool PartitionByFirstLevelNamespace { get; set; } = false;

    /// <summary>
    /// Folds legacy boolean layout flags into the enum model and applies
    /// cross-field dependency rules. Called by <see cref="SettingsService"/>
    /// after every load. Idempotent. This is the single home for setting
    /// dependencies that the type system (enums, nested groups) can't encode.
    /// </summary>
    public void Normalize()
    {
        // Tolerate hand-edited JSON with "msagl": null.
        Msagl ??= new MsaglLayoutSettings();

#pragma warning disable CS0618 // legacy flags are migration inputs only
        if (Engine == LayoutEngineKind.Layered)
        {
            if (UseMsaglEngine) Engine = LayoutEngineKind.Msagl;
            else if (UseCompoundLayoutEngine) Engine = LayoutEngineKind.Compound;
        }

        if (Msagl.PartitionMode == MsaglPartitionMode.None)
        {
            // Matches the old engine precedence: first-level namespace won
            // when both legacy bools were true.
            if (PartitionByFirstLevelNamespace) Msagl.PartitionMode = MsaglPartitionMode.FirstLevelNamespace;
            else if (SeparateAppAndTests) Msagl.PartitionMode = MsaglPartitionMode.AppVsTests;
        }

        UseMsaglEngine = false;
        UseCompoundLayoutEngine = false;
        SeparateAppAndTests = false;
        PartitionByFirstLevelNamespace = false;
#pragma warning restore CS0618
    }

    /// <summary>
    /// When true, edges are re-routed automatically after node/cluster drag
    /// (and after Design Mode mutations). When false, press Ctrl+R to redraw.
    /// Default true for testing; can be disabled if re-routing is too slow.
    /// </summary>
    public bool AutoRedrawEdges { get; set; } = true;

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

    /// <summary>
    /// LLM settings for generating class diagrams from natural language prompts.
    /// Configured in the Settings window under the "LLM" section. Persisted
    /// per-project so different projects can use different providers/models.
    /// </summary>
    public LlmSettings Llm { get; set; } = new();

    /// <summary>
    /// Where "Get Code" actions (Analyze Mode RMB context menu) send the
    /// assembled source. Clipboard = copy text to OS clipboard; File = write
    /// a code-bundle .txt to the source bundle folder. Default Clipboard.
    /// </summary>
    public CodeExtractionOutputMode CodeExtractionOutput { get; set; } = CodeExtractionOutputMode.Clipboard;
}

public enum CodeExtractionOutputMode
{
    Clipboard,
    File
}

public enum CacheInvalidationMode
{
    WarnAndPrompt,
    AutoRescan,
    Ignore
}

/// <summary>
/// MSAGL-engine-specific per-project layout settings. Persisted as a nested
/// object in ProjectSettings JSON (mirrors the LlmSettings precedent).
/// Applies only when <see cref="ProjectSettings.Engine"/> is Msagl.
/// </summary>
public sealed class MsaglLayoutSettings
{
    /// <summary>
    /// Top-level cluster partitioning for the MSAGL engine (columns by
    /// app/tests or by first-level sub-namespace).
    /// </summary>
    public MsaglPartitionMode PartitionMode { get; set; } = MsaglPartitionMode.None;
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
