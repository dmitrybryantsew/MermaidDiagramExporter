using System;
using System.IO;
using System.Threading.Tasks;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Gui.Persistence;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Coordinates the scan → cache-check → load-or-rescan flow previously inline in MainWindow.
/// Extracted in Step 18 to separate orchestration from UI.
///
/// This class is UI-framework-agnostic — it communicates results back via events
/// and async callbacks so the caller (MainWindow) can update its own UI controls.
/// </summary>
public sealed class ScanCoordinator
{
    private readonly RoslynTypeScanner _scanner;
    private readonly TypeGraphCacheService _cacheService;
    private readonly SourceBundleService _bundleService;
    private readonly SettingsService _settingsService;

    /// <summary>
    /// Raised when a scan completes successfully. The argument is the resulting graph.
    /// </summary>
    public event Action<TypeGraph>? ScanCompleted;

    /// <summary>
    /// Raised when a scan fails. The argument is the error message.
    /// </summary>
    public event Action<string>? ScanFailed;

    /// <summary>
    /// Raised to report a status message (e.g. "Loaded from cache", "Saved: diagram.png").
    /// </summary>
    public event Action<string>? StatusChanged;

    public ScanCoordinator(
        RoslynTypeScanner scanner,
        TypeGraphCacheService cacheService,
        SourceBundleService bundleService,
        SettingsService settingsService)
    {
        _scanner = scanner;
        _cacheService = cacheService;
        _bundleService = bundleService;
        _settingsService = settingsService;
    }

    /// <summary>
    /// Executes the full scan-or-load-from-cache flow for the given folder path.
    /// The <paramref name="promptForCache"/> async callback is invoked when the user
    /// should be asked whether to load from cache. It returns the user's choice.
    /// Returns the loaded or scanned graph, or null if the user cancelled.
    /// </summary>
    public async Task<TypeGraph?> ExecuteScanFlowAsync(
        string folder,
        Func<CacheInfo, CacheValidationResult, Task<CachePromptResult>> promptForCache)
    {
        // Load settings for this project
        var settings = _settingsService.LoadSettings(folder);

        // Check cache and prompt if available
        bool useCache = false;
        if (settings.PromptToLoadCache && _cacheService.CacheExists(settings))
        {
            var cacheInfo = _cacheService.GetCacheInfo(settings);
            var manifestPath = Path.Combine(
                _settingsService.ResolveCacheDirectory(settings),
                ".cache-manifest.json");
            var validation = _cacheService.ValidateManifest(manifestPath, settings);

            if (validation == CacheValidationResult.UpToDate || validation == CacheValidationResult.MinorChanges)
            {
                if (cacheInfo != null)
                {
                    var result = await promptForCache(cacheInfo, validation);
                    if (result == CachePromptResult.Cancelled)
                        return null;
                    if (result == CachePromptResult.LoadCache)
                        useCache = true;
                }
            }
        }

        // Build scan options with custom stereotypes from settings
        var buildOptions = new GraphBuildOptions();
        if (settings.ApplyCustomStereotypes && settings.StereotypeRules.Count > 0)
        {
            foreach (var rule in settings.StereotypeRules)
            {
                buildOptions.CustomStereotypes.Add(new StereotypeConfig
                {
                    Pattern = rule.Pattern,
                    Label = rule.Label,
                    ColorHex = rule.ColorHex
                });
            }
        }

        // Scan or load from cache
        TypeGraph graph;
        if (useCache)
        {
            var cached = _cacheService.LoadCache(settings);
            if (cached != null)
            {
                graph = cached;
                StatusChanged?.Invoke("Loaded from cache");
            }
            else
            {
                graph = _scanner.ScanFolder(folder, buildOptions);
            }
        }
        else
        {
            graph = _scanner.ScanFolder(folder, buildOptions);
        }

        // Auto-save cache if enabled
        if (settings.AutoSaveCache)
        {
            _cacheService.SaveCache(graph, settings);
        }

        // Auto-save source bundle if enabled
        if (settings.AutoSaveSourceBundle)
        {
            string bundlePath = _bundleService.GenerateBundle(folder, settings);
            StatusChanged?.Invoke($"Bundle: {Path.GetFileName(bundlePath)}");
        }

        // Persist settings
        _settingsService.SaveSettings(settings);

        ScanCompleted?.Invoke(graph);
        return graph;
    }
}
