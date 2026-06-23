using System;
using System.IO;
using System.Threading.Tasks;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Gui.Persistence;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Result of the scan flow when the user needs to be prompted about cache.
/// </summary>
public sealed class CachePromptRequest
{
    public CacheInfo CacheInfo { get; init; } = null!;
    public CacheValidationResult Validation { get; init; }
}

/// <summary>
/// Coordinates the scan → cache-check → load-or-rescan flow previously inline in MainWindow.
/// </summary>
public sealed class ScanCoordinator
{
    private readonly RoslynTypeScanner _scanner;
    private readonly TypeGraphCacheService _cacheService;
    private readonly SourceBundleService _bundleService;
    private readonly SettingsService _settingsService;

    public event Action<TypeGraph>? ScanCompleted;
    public event Action<string>? ScanFailed;
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
    /// Phase 1: Check if cache prompt is needed. Runs on background thread.
    /// Returns a prompt request if the user should be asked, or null if no prompt needed.
    /// </summary>
    public CachePromptRequest? CheckCachePrompt(string folder)
    {
        var settings = _settingsService.LoadSettings(folder);

        if (settings.PromptToLoadCache && _cacheService.CacheExists(settings))
        {
            var cacheInfo = _cacheService.GetCacheInfo(settings);
            var manifestPath = Path.Combine(
                _settingsService.ResolveCacheDirectory(settings),
                ".cache-manifest.json");
            var validation = _cacheService.ValidateManifest(manifestPath, settings);

            if ((validation == CacheValidationResult.UpToDate || validation == CacheValidationResult.MinorChanges)
                && cacheInfo != null)
            {
                return new CachePromptRequest { CacheInfo = cacheInfo, Validation = validation };
            }
        }

        return null;
    }

    /// <summary>
    /// Phase 2: Execute the actual scan (or load from cache). Runs on background thread.
    /// </summary>
    public TypeGraph ExecuteScan(string folder, bool useCache)
    {
        var settings = _settingsService.LoadSettings(folder);

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

        if (settings.AutoSaveCache)
            _cacheService.SaveCache(graph, settings);

        if (settings.AutoSaveSourceBundle)
        {
            string bundlePath = _bundleService.GenerateBundle(folder, settings);
            StatusChanged?.Invoke($"Bundle: {Path.GetFileName(bundlePath)}");
        }

        _settingsService.SaveSettings(settings);

        ScanCompleted?.Invoke(graph);
        return graph;
    }
}
