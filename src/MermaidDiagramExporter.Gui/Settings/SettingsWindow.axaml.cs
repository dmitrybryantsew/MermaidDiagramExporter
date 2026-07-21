using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MermaidDiagramExporter.Gui.Stereotypes;
using MermaidDiagramExporter.Gui.Design;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Llm;

namespace MermaidDiagramExporter.Gui.Settings;

public partial class SettingsWindow : Window
{
    private readonly SettingsService _settingsService;
    private ProjectSettings _settings = new();
    private System.Collections.ObjectModel.ObservableCollection<StereotypeRule> _stereotypeRules = new();
    private Dictionary<string, string> _shortcutBindings = new();

    private static readonly Dictionary<string, string> ShortcutFieldMap = new()
    {
        ["Select"] = nameof(ShortcutSelect),
        ["Class"] = nameof(ShortcutClass),
        ["Interface"] = nameof(ShortcutInterface),
        ["Enum"] = nameof(ShortcutEnum),
        ["Struct"] = nameof(ShortcutStruct),
        ["AbstractClass"] = nameof(ShortcutAbstractClass),
        ["StaticClass"] = nameof(ShortcutStaticClass),
        ["Namespace"] = nameof(ShortcutNamespace),
        ["EdgeInheritance"] = nameof(ShortcutEdgeInheritance),
        ["EdgeImplements"] = nameof(ShortcutEdgeImplements),
        ["EdgeAssociation"] = nameof(ShortcutEdgeAssociation),
        ["EdgeDependency"] = nameof(ShortcutEdgeDependency),
        ["EdgeAggregation"] = nameof(ShortcutEdgeAggregation),
        ["EdgeComposition"] = nameof(ShortcutEdgeComposition),
    };

    public SettingsWindow(SettingsService settingsService)
    {
        _settingsService = settingsService;
        InitializeComponent();
        WireEvents();
    }

    private void WireEvents()
    {
        SaveButton.Click += OnSave;
        CancelButton.Click += OnCancel;
        ResetDefaultsButton.Click += OnResetDefaults;
        ResetShortcutsButton.Click += OnResetShortcuts;
        BrowseCacheButton.Click += async (s, e) => await BrowseFolder(CacheFolderText);
        BrowseBundleButton.Click += async (s, e) => await BrowseFolder(BundleFolderText);
        AddStereotypeRuleButton.Click += (s, e) =>
        {
            _stereotypeRules.Add(new StereotypeRule
            {
                Pattern = ".*",
                Label = "New",
                ColorHex = "#4ECDC4"
            });
        };
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
        CodeExtractionOutputCombo.SelectedIndex = (int)_settings.CodeExtractionOutput;
        ApplyCustomStereotypesCheck.IsChecked = _settings.ApplyCustomStereotypes;
        PersistLayoutCheck.IsChecked = _settings.PersistManualLayout;
        EnableDraggingCheck.IsChecked = _settings.EnableNodeDragging;
        ShowMinimapCheck.IsChecked = _settings.ShowMinimap;
        AggregateHighwaysCheck.IsChecked = _settings.AggregateHighways;
        EngineCombo.SelectedIndex = (int)_settings.Engine;
        MsaglPartitionCombo.SelectedIndex = (int)_settings.Msagl.PartitionMode;
        MsaglPartitionCombo.IsEnabled = _settings.Engine == LayoutEngineKind.Msagl;
        ForcePreventOverlapCheck.IsChecked = _settings.Force.PreventClusterOverlap;
        ForcePreventOverlapCheck.IsEnabled = _settings.Engine == LayoutEngineKind.Force;
        ZoneFirstMicroCombo.SelectedIndex = (int)_settings.ZoneFirst.MicroEngine;
        ZoneFirstMicroCombo.IsEnabled = _settings.Engine == LayoutEngineKind.ZoneFirst;
        ZoneFirstSpacingText.Text = _settings.ZoneFirst.ZoneSpacing.ToString("0");
        ZoneFirstSpacingText.IsEnabled = _settings.Engine == LayoutEngineKind.ZoneFirst;

        // Engine-specific options only apply to their engine — disable them
        // otherwise so the dependency is visible instead of silently ignored.
        EngineCombo.SelectionChanged += (s, e) =>
        {
            MsaglPartitionCombo.IsEnabled = EngineCombo.SelectedIndex == (int)LayoutEngineKind.Msagl;
            ForcePreventOverlapCheck.IsEnabled = EngineCombo.SelectedIndex == (int)LayoutEngineKind.Force;
            bool zoneFirst = EngineCombo.SelectedIndex == (int)LayoutEngineKind.ZoneFirst;
            ZoneFirstMicroCombo.IsEnabled = zoneFirst;
            ZoneFirstSpacingText.IsEnabled = zoneFirst;
        };
        LoadEdgeStyleFields();

        _shortcutBindings = new Dictionary<string, string>(_settings.DesignShortcutBindings);
        LoadShortcutFields();

        _stereotypeRules.Clear();
        foreach (var rule in _settings.StereotypeRules)
            _stereotypeRules.Add(new StereotypeRule
            {
                Pattern = rule.Pattern,
                Label = rule.Label,
                ColorHex = rule.ColorHex
            });
        StereotypeRulesList.ItemsSource = _stereotypeRules;

        // LLM settings
        LoadLlmFields();
    }

    /// <summary>
    /// The settings that were saved (or null if cancelled).
    /// Check this after the dialog closes.
    /// </summary>
    public ProjectSettings? SavedSettings { get; private set; }

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        // Validate all stereotype regex patterns before saving
        var invalidRules = new List<(StereotypeRule Rule, string Error)>();
        foreach (var rule in _stereotypeRules)
        {
            if (!CustomStereotypeEngine.TryValidatePattern(rule.Pattern, out var error))
            {
                invalidRules.Add((rule, error ?? "Invalid regex pattern"));
            }
        }

        if (invalidRules.Count > 0)
        {
            var message = string.Join("\n", invalidRules.Select(ir => $"Pattern \"{ir.Rule.Pattern}\": {ir.Error}"));
            StereotypeErrorText.Text = message;
            StereotypeErrorText.IsVisible = true;
            StereotypesTab.IsSelected = true;
            return;
        }
        else
        {
            StereotypeErrorText.IsVisible = false;
        }

        _settings.AutoSaveCache = AutoSaveCacheCheck.IsChecked == true;
        _settings.AutoSaveSourceBundle = AutoSaveSourceBundleCheck.IsChecked == true;
        _settings.PromptToLoadCache = PromptLoadCacheCheck.IsChecked == true;
        _settings.CacheInvalidationBehavior = (CacheInvalidationMode)Math.Clamp(CacheInvalidationCombo.SelectedIndex, 0, 2);
        _settings.CustomCacheFolderPath = string.IsNullOrWhiteSpace(CacheFolderText.Text) ? null : CacheFolderText.Text;
        _settings.CustomSourceBundleFolderPath = string.IsNullOrWhiteSpace(BundleFolderText.Text) ? null : BundleFolderText.Text;
        _settings.SearchCaseSensitive = SearchCaseSensitiveCheck.IsChecked == true;
        _settings.SearchIncludeMembers = SearchIncludeMembersCheck.IsChecked == true;
        _settings.AutoFocusSearchResults = AutoFocusSearchCheck.IsChecked == true;
        _settings.CodeExtractionOutput = (CodeExtractionOutputMode)Math.Clamp(CodeExtractionOutputCombo.SelectedIndex, 0, 1);
        _settings.ApplyCustomStereotypes = ApplyCustomStereotypesCheck.IsChecked == true;
        _settings.PersistManualLayout = PersistLayoutCheck.IsChecked == true;
        _settings.EnableNodeDragging = EnableDraggingCheck.IsChecked == true;
        _settings.ShowMinimap = ShowMinimapCheck.IsChecked == true;
        _settings.AggregateHighways = AggregateHighwaysCheck.IsChecked == true;
        _settings.Engine = (LayoutEngineKind)Math.Clamp(EngineCombo.SelectedIndex, 0, 5);
        _settings.Msagl.PartitionMode = (MsaglPartitionMode)Math.Clamp(MsaglPartitionCombo.SelectedIndex, 0, 2);
        _settings.Force.PreventClusterOverlap = ForcePreventOverlapCheck.IsChecked == true;
        _settings.ZoneFirst.MicroEngine = (ZoneFirstMicroEngine)Math.Clamp(ZoneFirstMicroCombo.SelectedIndex, 0, 1);
        _settings.ZoneFirst.ZoneSpacing = float.TryParse(ZoneFirstSpacingText.Text, out float zoneSpacing)
            ? Math.Clamp(zoneSpacing, 20f, 2000f)
            : 120f;
        _settings.EdgeStyles = CollectEdgeStyles();
        _settings.StereotypeRules = _stereotypeRules.ToList();

        _settings.DesignShortcutBindings = CollectShortcutBindings();

        // LLM settings
        CollectLlmFields();

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
        CodeExtractionOutputCombo.SelectedIndex = 0;
        ApplyCustomStereotypesCheck.IsChecked = true;
        PersistLayoutCheck.IsChecked = true;
        EnableDraggingCheck.IsChecked = true;
        ShowMinimapCheck.IsChecked = true;
        AggregateHighwaysCheck.IsChecked = false;
        EngineCombo.SelectedIndex = 0;
        MsaglPartitionCombo.SelectedIndex = 0;
        MsaglPartitionCombo.IsEnabled = false;
        ForcePreventOverlapCheck.IsChecked = true;
        ForcePreventOverlapCheck.IsEnabled = false;
        ZoneFirstMicroCombo.SelectedIndex = 0;
        ZoneFirstMicroCombo.IsEnabled = false;
        ZoneFirstSpacingText.Text = "120";
        ZoneFirstSpacingText.IsEnabled = false;
        ResetEdgeStyleFields();
    }

    private void OnRemoveStereotypeRule(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StereotypeRule rule)
        {
            _stereotypeRules.Remove(rule);
        }
    }

    private void OnResetShortcuts(object? sender, RoutedEventArgs e)
    {
        _shortcutBindings.Clear();
        LoadShortcutFields();
    }

    private void LoadShortcutFields()
    {
        foreach (var (toolName, fieldName) in ShortcutFieldMap)
        {
            var field = this.FindControl<TextBox>(fieldName);
            if (field == null) continue;
            var key = DesignShortcutDefaults.GetEffectiveKey(toolName, _shortcutBindings);
            field.Text = key;
            field.Watermark = DesignShortcutDefaults.DefaultBindings.TryGetValue(toolName, out var d) ? d : "";
        }
    }

    private Dictionary<string, string> CollectShortcutBindings()
    {
        var bindings = new Dictionary<string, string>();
        foreach (var (toolName, fieldName) in ShortcutFieldMap)
        {
            var field = this.FindControl<TextBox>(fieldName);
            if (field == null) continue;
            var text = field.Text?.Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(text) && text.Length <= 2)
            {
                var defaultKey = DesignShortcutDefaults.DefaultBindings.TryGetValue(toolName, out var d) ? d : "";
                if (text != defaultKey.ToUpperInvariant())
                    bindings[toolName] = text;
            }
        }
        return bindings;
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

    // ── Edge style helpers ──

    private void LoadEdgeStyleFields()
    {
        var styles = _settings.EdgeStyles ?? new EdgeStyleSettings();
        SetStyleRow(InheritanceColorText, InheritanceArrowCombo, styles.Inheritance);
        SetStyleRow(ImplementsColorText, ImplementsArrowCombo, styles.Implements);
        SetStyleRow(AssociationColorText, AssociationArrowCombo, styles.Association);
    }

    private static void SetStyleRow(TextBox colorText, ComboBox arrowCombo, EdgeKindStyle style)
    {
        colorText.Text = style.ColorHex;
        arrowCombo.SelectedIndex = (int)style.Arrowhead;
    }

    private EdgeStyleSettings CollectEdgeStyles()
    {
        var styles = new EdgeStyleSettings();
        styles.Inheritance = ReadStyleRow(InheritanceColorText, InheritanceArrowCombo, styles.Inheritance);
        styles.Implements = ReadStyleRow(ImplementsColorText, ImplementsArrowCombo, styles.Implements);
        styles.Association = ReadStyleRow(AssociationColorText, AssociationArrowCombo, styles.Association);
        return styles;
    }

    private static EdgeKindStyle ReadStyleRow(TextBox colorText, ComboBox arrowCombo, EdgeKindStyle fallback)
    {
        var hex = colorText.Text?.Trim() ?? "";
        if (!hex.StartsWith("#") || hex.Length != 7) hex = fallback.ColorHex;
        var arrowIdx = arrowCombo.SelectedIndex;
        var arrow = arrowIdx >= 0 ? (EdgeArrowheadStyle)arrowIdx : fallback.Arrowhead;
        return new EdgeKindStyle { ColorHex = hex, Arrowhead = arrow };
    }

    private void ResetEdgeStyleFields()
    {
        var defaults = new EdgeStyleSettings();
        SetStyleRow(InheritanceColorText, InheritanceArrowCombo, defaults.Inheritance);
        SetStyleRow(ImplementsColorText, ImplementsArrowCombo, defaults.Implements);
        SetStyleRow(AssociationColorText, AssociationArrowCombo, defaults.Association);
    }

    // ── LLM settings helpers ──

    private void LoadLlmFields()
    {
        var llm = _settings.Llm;
        LlmProviderCombo.SelectedIndex = (int)llm.Provider;
        LlmBaseUrlText.Text = llm.BaseUrl;
        LlmApiKeyText.Text = llm.ApiKey;
        LlmModelText.Text = llm.Model;
        LlmTemperatureSlider.Value = llm.Temperature;
        LlmTemperatureLabel.Text = llm.Temperature.ToString("F1");
        LlmMaxTokensText.Text = llm.MaxTokens.ToString();
        LlmTimeoutText.Text = llm.TimeoutSeconds.ToString();

        // Wire slider change to label
        LlmTemperatureSlider.PropertyChanged += (s, e) =>
        {
            if (e.Property == Slider.ValueProperty)
                LlmTemperatureLabel.Text = LlmTemperatureSlider.Value.ToString("F1");
        };
    }

    private void CollectLlmFields()
    {
        var llm = _settings.Llm;
        llm.Provider = (LlmProvider)Math.Clamp(LlmProviderCombo.SelectedIndex, 0, 3);
        llm.BaseUrl = LlmBaseUrlText.Text?.Trim() ?? "";
        llm.ApiKey = LlmApiKeyText.Text?.Trim() ?? "";
        llm.Model = LlmModelText.Text?.Trim() ?? "";
        llm.Temperature = LlmTemperatureSlider.Value;
        llm.MaxTokens = int.TryParse(LlmMaxTokensText.Text, out var mt) ? Math.Clamp(mt, 1, 128000) : 2048;
        llm.TimeoutSeconds = int.TryParse(LlmTimeoutText.Text, out var to) ? Math.Clamp(to, 5, 600) : 120;
    }

    private void OnLlmProviderChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Auto-fill base URL when provider changes
        var idx = LlmProviderCombo.SelectedIndex;
        if (idx < 0) return;
        var provider = (LlmProvider)idx;
        var defaultUrl = LlmSettings.GetDefaultBaseUrl(provider);
        LlmBaseUrlText.Text = defaultUrl;
    }

    private async void OnLlmRefreshModels(object? sender, RoutedEventArgs e)
    {
        CollectLlmFields();
        var llm = _settings.Llm;
        if (!llm.IsConfigured)
        {
            LlmModelsStatus.Text = "Configure provider and model first";
            return;
        }

        LlmModelsStatus.Text = "Fetching models...";
        LlmRefreshModelsButton.IsEnabled = false;

        try
        {
            var modelService = new LlmModelService(llm);
            var models = await modelService.ListModelsAsync();

            LlmModelsList.ItemsSource = models;
            LlmModelsStatus.Text = models.Count > 0
                ? $"{models.Count} models available"
                : "No models found (enter model manually)";
        }
        catch (Exception ex)
        {
            LlmModelsStatus.Text = $"Error: {ex.Message}";
            LlmModelsList.ItemsSource = null;
        }
        finally
        {
            LlmRefreshModelsButton.IsEnabled = true;
        }
    }

    private void OnLlmModelSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (LlmModelsList.SelectedItem is string model)
        {
            LlmModelText.Text = model;
        }
    }
}
