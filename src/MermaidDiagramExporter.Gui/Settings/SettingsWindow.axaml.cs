using System;
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
    private System.Collections.ObjectModel.ObservableCollection<StereotypeRule> _stereotypeRules = new();

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
        _settings.StereotypeRules = _stereotypeRules.ToList();

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

    private void OnRemoveStereotypeRule(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is StereotypeRule rule)
        {
            _stereotypeRules.Remove(rule);
        }
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
