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
        ApplyCustomStereotypesCheck.IsChecked = _settings.ApplyCustomStereotypes;
        PersistLayoutCheck.IsChecked = _settings.PersistManualLayout;
        EnableDraggingCheck.IsChecked = _settings.EnableNodeDragging;
        ShowMinimapCheck.IsChecked = _settings.ShowMinimap;
        UseCompoundEngineCheck.IsChecked = _settings.UseCompoundLayoutEngine;
        UseMsaglEngineCheck.IsChecked = _settings.UseMsaglEngine;

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
        _settings.ApplyCustomStereotypes = ApplyCustomStereotypesCheck.IsChecked == true;
        _settings.PersistManualLayout = PersistLayoutCheck.IsChecked == true;
        _settings.EnableNodeDragging = EnableDraggingCheck.IsChecked == true;
        _settings.ShowMinimap = ShowMinimapCheck.IsChecked == true;
        _settings.UseCompoundLayoutEngine = UseCompoundEngineCheck.IsChecked == true;
        _settings.UseMsaglEngine = UseMsaglEngineCheck.IsChecked == true;
        _settings.StereotypeRules = _stereotypeRules.ToList();

        _settings.DesignShortcutBindings = CollectShortcutBindings();

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
        UseCompoundEngineCheck.IsChecked = false;
        UseMsaglEngineCheck.IsChecked = false;
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
}
