using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Input.Platform;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Export;
using MermaidDiagramExporter.Focus;
using MermaidDiagramExporter.Gui.Design;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Search;
using MermaidDiagramExporter.Gui.Settings;
using MermaidDiagramExporter.Gui.Persistence;
using MermaidDiagramExporter.Gui.Matrix;

namespace MermaidDiagramExporter.Gui;

public partial class MainWindow : Window
{
    private readonly RoslynTypeScanner _scanner;
    private readonly LayoutEngine _layoutEngine;
    private readonly FocusedGraphNavigationController _focusNavigationController = new();
    private readonly GraphSeedSelectionState _seedSelectionState = new();
    private readonly SettingsService _settingsService;
    private readonly DesignModeController _designModeController = new();
    private readonly DesignCanvasController _designCanvasController;
    private DesignGraph? _designGraph;
    private string? _designFilePath;
    private bool _designIsDirty;
    private DateTime _lastAutoSaveUtc = DateTime.MinValue;
    private const int AutoSaveIntervalSeconds = 30;
    private readonly DesignRecentFiles _recentDesignFiles = new();
    private readonly TypeGraphCacheService _cacheService;
    private readonly SourceBundleService _bundleService;
    private readonly SymbolSearchEngine _searchEngine = new();
    private readonly ScanCoordinator _scanCoordinator;
    private ManualLayoutOverrides _manualOverrides = new();
    private ProjectSettings _currentSettings = new();

    private List<GraphNode> _allNodes = new();
    private List<GraphEdge> _allEdges = new();
    private Dictionary<string, GraphNode> _nodeMap = new();
    private TypeGraph? _currentGraph;
    private bool _updatingClassList;

    private GraphFocusTraversalMode _currentTraversalMode = GraphFocusTraversalMode.UndirectedAssociations;
    private int _focusDepth = 1;
    private string _currentSelectedNodeId = string.Empty;

    public ProjectSettings CurrentSettings => _currentSettings;

    public MainWindow(SettingsService settingsService, LayoutEngine layoutEngine, RoslynTypeScanner scanner)
    {
        _settingsService = settingsService;
        _layoutEngine = layoutEngine;
        _scanner = scanner;
        _cacheService = new TypeGraphCacheService(_settingsService);
        _bundleService = new SourceBundleService(_settingsService);
        _scanCoordinator = new ScanCoordinator(_scanner, _cacheService, _bundleService, _settingsService);
        _designCanvasController = new DesignCanvasController(_designModeController);
        _scanCoordinator.StatusChanged += OnScanStatusChanged;

        InitializeComponent();
        GraphCanvasView.SelectionChanged += OnCanvasSelectionChanged;
        GraphCanvasView.ManualLayoutChanged += OnManualLayoutChanged;
        GraphCanvasView.ViewportChanged += OnViewportChanged;
        SymbolSearchPanel.NodeSelected += OnSearchNodeSelected;
        SymbolSearchPanel.FocusOnResultsRequested += OnFocusSearchResults;
        SymbolSearchPanel.SearchCleared += OnSearchCleared;
        MinimapView.ViewportJumpRequested += OnMinimapViewportJump;

        // Wire Design Mode controller to canvas (W1)
        GraphCanvasView.SetDesignController(_designCanvasController);
        _designCanvasController.GraphMutated += OnDesignGraphMutated;
        GraphCanvasView.DesignClassDoubleClicked += OnDesignClassDoubleClicked;

        // Initialize mode toggle UI (default is Analyze Mode)
        UpdateModeUi();
        MatrixView.CellClicked += OnMatrixCellClicked;
    }

    // --- Mode toggle (M0 scaffold) ---

    private void OnAnalyzeModeClick(object? sender, RoutedEventArgs e)
    {
        // Clear Design Mode wiring so Analyze Mode pointer routing takes over
        GraphCanvasView.SetDesignGraph(null);
        _designModeController.EnterAnalyzeMode();
        UpdateModeUi();
        // Restore the Analyze Mode graph (re-set it on the canvas)
        if (_currentGraph != null)
            SetDisplayedGraph(_currentGraph, reloadManualOverridesFromDisk: false);
    }

    private void OnDesignModeClick(object? sender, RoutedEventArgs e)
    {
        // Create a fresh design if none exists yet (first entry to Design Mode)
        if (_designModeController.CurrentDesign == null)
            _designModeController.EnterDesignMode(new DesignGraph { Title = "Untitled Design" });

        _designGraph = _designModeController.CurrentDesign;
        _designModeController.EnterDesignMode(_designGraph);
        UpdateModeUi();
        RenderDesignModeGraph();
    }

    /// <summary>
    /// Renders the current Design Mode graph by converting it to a TypeGraph,
    /// running it through the layout engine, and feeding the result into the
    /// canvas. Called on mode entry and after every mutation (via
    /// <see cref="OnDesignGraphMutated"/>).
    /// </summary>
    private void RenderDesignModeGraph()
    {
        if (_designGraph == null) return;

        var typeGraph = DesignExporter.ToTypeGraph(_designGraph);
        GraphCanvasView.SetDesignGraph(_designGraph);
        var (nodes, edges) = _layoutEngine.Layout(typeGraph);
        GraphCanvasView.SetGraph(nodes, edges);
        MinimapView.SetGraph(nodes, edges);
        StatsText.Text = $"Design: {_designGraph.Classes.Count} classes, {_designGraph.Edges.Count} edges";
    }

    /// <summary>
    /// Re-renders the canvas after any Design Mode mutation. Subscribed to
    /// <see cref="DesignCanvasController.GraphMutated"/>.
    /// </summary>
    private void OnDesignGraphMutated(object? sender, EventArgs e)
    {
        _designIsDirty = true;
        RenderDesignModeGraph();
        TryAutoSave();
    }

    /// <summary>
    /// Shows the inline edit TextBox overlay positioned over a class header
    /// when the user double-clicks it. Per docs/design/04 — the one real
    /// Avalonia Control in Design Mode.
    /// </summary>
    private void OnDesignClassDoubleClicked(string classId)
    {
        if (_designGraph == null) return;
        var cls = _designGraph.Classes.FirstOrDefault(c => c.Id == classId);
        if (cls == null) return;

        // Position the TextBox over the class header (world coords → screen coords)
        // The canvas pan/zoom transforms are applied via the existing screen-to-world
        // math in reverse. We need to read the current pan/zoom from the canvas.
        // For simplicity, use a fixed offset from canvas top-left + class position.
        var (panX, panY, zoom) = GraphCanvasView.GetViewportTransform();
        var screenX = cls.X * zoom + panX;
        var screenY = cls.Y * zoom + panY;

        InlineEditTextBox.Text = cls.Name;
        InlineEditTextBox.Width = Math.Max(120, cls.Width * zoom);
        Canvas.SetLeft(InlineEditTextBox, screenX);
        Canvas.SetTop(InlineEditTextBox, screenY);
        InlineEditTextBox.IsVisible = true;
        InlineEditTextBox.Tag = classId; // remember which class we're editing

        // Focus and select all text
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            InlineEditTextBox.Focus();
            InlineEditTextBox.SelectAll();
        }, Avalonia.Threading.DispatcherPriority.Input);

        // Wire up commit handlers (one-shot — detach after commit)
        InlineEditTextBox.KeyDown -= OnInlineEditKeyDown;
        InlineEditTextBox.LostFocus -= OnInlineEditLostFocus;
        InlineEditTextBox.KeyDown += OnInlineEditKeyDown;
        InlineEditTextBox.LostFocus += OnInlineEditLostFocus;
    }

    private void OnInlineEditKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter)
        {
            CommitInlineEdit();
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Escape)
        {
            CancelInlineEdit();
            e.Handled = true;
        }
    }

    private void OnInlineEditLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CommitInlineEdit();
    }

    private void CommitInlineEdit()
    {
        if (_designGraph == null || !InlineEditTextBox.IsVisible) return;
        var classId = InlineEditTextBox.Tag as string;
        if (classId == null) return;

        var newName = InlineEditTextBox.Text?.Trim();
        if (!string.IsNullOrEmpty(newName))
        {
            var cls = _designGraph.Classes.FirstOrDefault(c => c.Id == classId);
            if (cls != null && cls.Name != newName)
            {
                var cmd = new DesignCommands.RenameClass(classId, cls.Name, newName);
                _designCanvasController.UndoManager.Execute(cmd, _designGraph);
                _designCanvasController.ExecuteCommand(new DesignCommands.RenameClass(classId, cls.Name, newName), _designGraph);
                RenderDesignModeGraph();
            }
        }
        InlineEditTextBox.IsVisible = false;
        InlineEditTextBox.Tag = null;
    }

    private void CancelInlineEdit()
    {
        InlineEditTextBox.IsVisible = false;
        InlineEditTextBox.Tag = null;
    }

    /// <summary>
    /// Auto-saves to a temp file every <see cref="AutoSaveIntervalSeconds"/>
    /// seconds if the design is dirty. Per docs/design/07 W6.
    /// </summary>
    private void TryAutoSave()
    {
        if (!_designIsDirty || _designGraph == null) return;
        if ((DateTime.UtcNow - _lastAutoSaveUtc).TotalSeconds < AutoSaveIntervalSeconds) return;

        try
        {
            var tempDir = Path.Combine(Path.GetTempPath(), ".mermaid-diagram-exporter");
            Directory.CreateDirectory(tempDir);
            var autosavePath = Path.Combine(tempDir, $"autosave-{_designGraph.Title.GetHashCode():X}.dgraph.json");
            DesignSerialization.Save(_designGraph, autosavePath);
            _lastAutoSaveUtc = DateTime.UtcNow;
        }
        catch
        {
            // Auto-save failures are silent — user can still save manually
        }
    }

    // ── Design Mode toolbar handlers (W2) ──

    /// <summary>
    /// Creates a fresh empty design, replacing the current one (no confirmation
    /// in W2 — could be added later).
    /// </summary>
    private void OnDesignNew(object? sender, RoutedEventArgs e)
    {
        var fresh = new DesignGraph { Title = "Untitled Design" };
        _designModeController.EnterDesignMode(fresh);
        _designGraph = _designModeController.CurrentDesign;
        RenderDesignModeGraph();
        StatsText.Text = "New design created";
    }

    /// <summary>
    /// Opens a .dgraph.json file via file picker.
    /// </summary>
    private async void OnDesignOpen(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Design",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Design Graph") { Patterns = new[] { "*.dgraph.json" } } }
        });

        if (files.Count == 0) return;
        var path = files[0].Path.LocalPath;
        var loaded = DesignSerialization.Load(path);
        _designModeController.EnterDesignMode(loaded);
        _designGraph = _designModeController.CurrentDesign;
        _designFilePath = path;
        _designIsDirty = false;
        _recentDesignFiles.Add(path);
        RenderDesignModeGraph();
        StatsText.Text = $"Opened: {Path.GetFileName(path)}";
    }

    /// <summary>
    /// Saves to the current file path. If no path is set, falls back to Save As.
    /// </summary>
    private async void OnDesignSave(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        var path = _designFilePath;
        if (string.IsNullOrEmpty(path))
        {
            await SaveDesignAsAsync();
            return;
        }
        DesignSerialization.Save(_designGraph, path);
        _designIsDirty = false;
        StatsText.Text = $"Saved: {Path.GetFileName(path)}";
    }

    /// <summary>
    /// Saves to a new file via file picker.
    /// </summary>
    private async void OnDesignSaveAs(object? sender, RoutedEventArgs e)
    {
        await SaveDesignAsAsync();
    }

    private async System.Threading.Tasks.Task SaveDesignAsAsync()
    {
        if (_designGraph == null) return;
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Design As",
            DefaultExtension = ".dgraph.json",
            FileTypeChoices = new[] { new FilePickerFileType("Design Graph") { Patterns = new[] { "*.dgraph.json" } } }
        });

        if (file == null) return;
        var path = file.Path.LocalPath;
        DesignSerialization.Save(_designGraph, path);
        _designFilePath = path;
        _designIsDirty = false;
        _recentDesignFiles.Add(path);
        StatsText.Text = $"Saved: {Path.GetFileName(path)}";
    }

    /// <summary>
    /// Undoes the last mutation in Design Mode.
    /// </summary>
    private void OnDesignUndo(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        if (_designCanvasController.Undo(_designGraph))
            StatsText.Text = "Undone";
        else
            StatsText.Text = "Nothing to undo";
    }

    /// <summary>
    /// Redoes the most recently undone mutation.
    /// </summary>
    private void OnDesignRedo(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        if (_designCanvasController.Redo(_designGraph))
            StatsText.Text = "Redone";
        else
            StatsText.Text = "Nothing to redo";
    }

    /// <summary>
    /// Adds a new class at the center of the canvas.
    /// </summary>
    private void OnDesignAddClass(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        // Center of canvas (approximate — canvas is ~800x600 by default)
        var centerX = 400f;
        var centerY = 300f;
        _designGraph.Classes.Add(new DesignClass
        {
            Name = "NewClass",
            X = centerX - 100f,
            Y = centerY - 30f,
            Width = 200f,
            Height = 60f
        });
        _designCanvasController.UndoManager.Clear();
        RenderDesignModeGraph();
        StatsText.Text = "Class added at canvas center";
    }

    /// <summary>
    /// Placeholder: edge creation happens via drag-from-port on the canvas.
    /// This button just shows a hint.
    /// </summary>
    private void OnDesignAddEdge(object? sender, RoutedEventArgs e)
    {
        StatsText.Text = "Drag from a class's edge port to another to create an edge";
    }

    /// <summary>
    /// Exports the design to a .mmd file.
    /// </summary>
    private async void OnDesignExportMermaid(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Mermaid",
            DefaultExtension = ".mmd",
            FileTypeChoices = new[] { new FilePickerFileType("Mermaid") { Patterns = new[] { "*.mmd" } } }
        });

        if (file == null) return;
        var path = file.Path.LocalPath;
        var mermaid = DesignExporter.ToMermaid(_designGraph);
        await System.IO.File.WriteAllTextAsync(path, mermaid);
        StatsText.Text = $"Exported: {Path.GetFileName(path)}";
    }

    /// <summary>
    /// Exports the design to a .cs stub file.
    /// </summary>
    private async void OnDesignExportCSharp(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export C# Stub",
            DefaultExtension = ".cs",
            FileTypeChoices = new[] { new FilePickerFileType("C# Source") { Patterns = new[] { "*.cs" } } }
        });

        if (file == null) return;
        var path = file.Path.LocalPath;
        var stub = DesignExporter.ToCSharpStub(_designGraph);
        await System.IO.File.WriteAllTextAsync(path, stub);
        StatsText.Text = $"Exported: {Path.GetFileName(path)}";
    }

    /// <summary>
    /// Exports the design to a .dgraph.json file.
    /// </summary>
    private async void OnDesignExportJson(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export JSON",
            DefaultExtension = ".dgraph.json",
            FileTypeChoices = new[] { new FilePickerFileType("Design Graph") { Patterns = new[] { "*.dgraph.json" } } }
        });

        if (file == null) return;
        var path = file.Path.LocalPath;
        DesignSerialization.Save(_designGraph, path);
        StatsText.Text = $"Exported: {Path.GetFileName(path)}";
    }

    /// <summary>
    /// Shows/hides the Analyze and Design Mode panels based on the current mode,
    /// and updates the toggle button backgrounds to indicate which is active.
    /// Called on startup (default Analyze) and after every mode switch.
    /// </summary>
    private void UpdateModeUi()
    {
        bool isDesign = _designModeController.CurrentMode == AppMode.Design;
        AnalyzeModePanel.IsVisible = !isDesign;
        DesignModePanel.IsVisible = isDesign;

        // Highlight the active mode toggle button
        AnalyzeModeButton.Background = isDesign
            ? Avalonia.Media.Brush.Parse("#3A4250")
            : Avalonia.Media.Brush.Parse("#4CAF50");
        DesignModeButton.Background = isDesign
            ? Avalonia.Media.Brush.Parse("#4CAF50")
            : Avalonia.Media.Brush.Parse("#3A4250");
    }

    private async void OnBrowse(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select source folder"
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            FolderTextBox.Text = path;
    }

    private async void OnScan(object? sender, RoutedEventArgs e)
    {
        var rawPath = FolderTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(rawPath))
        {
            StatsText.Text = "Please select a folder.";
            return;
        }

        string folder;
        try
        {
            folder = Path.GetFullPath(rawPath);
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            StatsText.Text = "The folder path is not valid.";
            return;
        }

        if (!Directory.Exists(folder))
        {
            StatsText.Text = "The selected folder does not exist.";
            return;
        }

        // Disable UI during scan to prevent double-clicks
        IsEnabled = false;
        StatsText.Text = "Scanning...";

        try
        {
            // Phase 1: Check cache on background thread (disk I/O)
            var promptRequest = await Task.Run(() => _scanCoordinator.CheckCachePrompt(folder));

            // Phase 2: If cache prompt needed, show dialog on UI thread
            bool useCache = false;
            if (promptRequest != null)
            {
                var dialog = new CachePromptDialog();
                dialog.SetInfo(promptRequest.CacheInfo, promptRequest.Validation);
                await dialog.ShowDialog(this);
                if (dialog.Result == CachePromptResult.Cancelled)
                {
                    IsEnabled = true;
                    return;
                }
                if (dialog.Result == CachePromptResult.LoadCache)
                    useCache = true;
            }

            // Phase 3: Execute scan on background thread (Roslyn compilation, disk I/O)
            var graph = await Task.Run(() => _scanCoordinator.ExecuteScan(folder, useCache));
            if (graph == null)
            {
                IsEnabled = true;
                return;
            }

            _currentSettings = _settingsService.LoadSettings(graph.Metadata.SourceDescription);
            _currentGraph = graph;
            _focusNavigationController.SetRootGraph(_currentGraph, _currentSettings.SourceFolderPath);
            _seedSelectionState.Clear();

            // Phase 4: Update UI on the main thread (touches Avalonia controls)
            SetDisplayedGraph(_currentGraph);
            GraphCanvasView.WaitForRender();
            UpdateStats(_currentGraph);

            // Auto-save screenshot
            var dir = Path.Combine(AppContext.BaseDirectory, "export");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"diagram_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            GraphCanvasView.SaveToPng(path);
            StatsText.Text += $" | Saved: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatsText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            IsEnabled = true;
        }
    }

    private void OnScanStatusChanged(string status)
    {
        StatsText.Text = status;
    }

    private void SetDisplayedGraph(TypeGraph? graph, string selectedNodeId = "", bool reloadManualOverridesFromDisk = true)
    {
        if (graph == null)
        {
            _allNodes = new List<GraphNode>();
            _allEdges = new List<GraphEdge>();
            _nodeMap = new Dictionary<string, GraphNode>();
            GraphCanvasView.SetGraph(_allNodes, _allEdges);
            UpdateClassList(null);
            UpdateNavigationButtons();
            return;
        }

        // Load manual overrides before layout (Bug 02 Fix 2):
        // only reload from disk on "entering a view" paths, not when the caller
        // has already deliberately set _manualOverrides itself (e.g. after Reset).
        if (reloadManualOverridesFromDisk)
        {
            _manualOverrides = _currentSettings.PersistManualLayout
                ? _cacheService.LoadManualOverrides(_currentSettings)
                : new ManualLayoutOverrides();
        }
        _layoutEngine.ManualOverrides = _manualOverrides;

        // Build LayoutOptions from current settings so the compound engine toggle
        // (and any future per-setting overrides) actually reaches the engine.
        _layoutEngine.LayoutOptions = new LayoutOptions
        {
            UseCompoundLayoutEngine = _currentSettings.UseCompoundLayoutEngine
        };

        var (nodes, edges) = _layoutEngine.Layout(graph);
        _allNodes = nodes;
        _allEdges = edges;
        _nodeMap = nodes.ToDictionary(n => n.Id);

        GraphCanvasView.ManualOverrides = _manualOverrides;
        GraphCanvasView.SetGraph(nodes, edges);
        MinimapView.SetGraph(nodes, edges);
        MinimapView.IsVisible = _currentSettings.ShowMinimap;
        UpdateClassList(graph);
        UpdateStats(graph);

        if (graph != null)
        {
            SymbolSearchPanel.SetGraph(graph, _currentSettings);
            _searchEngine.RebuildIndex(graph);
        }
        else
        {
            SymbolSearchPanel.Clear();
        }

        if (!string.IsNullOrEmpty(selectedNodeId))
        {
            var selectedNode = graph.Nodes.FirstOrDefault(n => n.Id == selectedNodeId);
            if (selectedNode != null)
                UpdateInspector(selectedNode);
        }

        UpdateNavigationButtons();
    }

    private void UpdateClassList(TypeGraph? graph)
    {
        _updatingClassList = true;
        var items = (graph?.Nodes ?? Array.Empty<TypeNodeData>())
            .OrderBy(n => n.Namespace)
            .ThenBy(n => n.DisplayName)
            .Select(n => $"{n.Namespace}.{n.DisplayName}")
            .ToList();
        ClassListBox.ItemsSource = items;
        ClassListBox.SelectedItem = null;
        _updatingClassList = false;
    }

    private void UpdateStats(TypeGraph? graph)
    {
        if (graph == null) return;
        string stats = $"{graph.Nodes.Count} types, {graph.Edges.Count} relationships";
        if (graph.Metadata.IsDerivedView && !string.IsNullOrEmpty(graph.Metadata.FocusSummary))
            stats += $" | {graph.Metadata.FocusSummary}";
        stats += $" | Depth: {_focusDepth} | Traversal: {_currentTraversalMode} | Seeds: {_seedSelectionState.Count}";
        StatsText.Text = stats;
    }

    private void OnClassSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_updatingClassList)
            return;

        if (ClassListBox.SelectedItem is not string selected) return;
        TypeGraph? graph = _focusNavigationController.CurrentGraph;
        if (graph == null) return;

        var node = graph.Nodes.FirstOrDefault(n => $"{n.Namespace}.{n.DisplayName}" == selected);
        if (node == null) return;

        _currentSelectedNodeId = node.Id;
        UpdateInspector(node);
        UpdateSeedButtons();
    }

    private void OnCanvasSelectionChanged(GraphNode? node)
    {
        if (node == null)
        {
            SelectedNodeText.Text = "";
            _currentSelectedNodeId = string.Empty;
            UpdateSeedButtons();
            return;
        }

        _currentSelectedNodeId = node.Id;
        SelectedNodeText.Text = $"{node.Namespace}.{node.DisplayName}\nKind: {node.Kind}\nFile: {Path.GetFileName(node.AssetPath)}";

        // Update inspector from the actual graph node
        TypeGraph? graph = _focusNavigationController.CurrentGraph;
        if (graph != null)
        {
            var typeNode = graph.Nodes.FirstOrDefault(n => n.Id == node.Id);
            if (typeNode != null)
                UpdateInspector(typeNode);
        }

        UpdateSeedButtons();
    }

    // --- Focus Controls ---

    private void OnFocusD1(object? sender, RoutedEventArgs e) => FocusCurrentSelection(1);
    private void OnFocusD2(object? sender, RoutedEventArgs e) => FocusCurrentSelection(2);
    private void OnFocusD3(object? sender, RoutedEventArgs e) => FocusCurrentSelection(3);

    private void OnFocusCurrent(object? sender, RoutedEventArgs e) => FocusCurrentSelection(_focusDepth);

    private void FocusCurrentSelection(int depth)
    {
        _focusDepth = depth;
        IReadOnlyList<string> seeds = ResolveFocusSeedIds();
        if (!_focusNavigationController.CanFocusSelection(seeds))
            return;

        TypeGraph? focused = _focusNavigationController.FocusSelection(seeds, depth, _currentTraversalMode);
        if (focused != null)
        {
            _seedSelectionState.PruneToGraph(focused);
            SetDisplayedGraph(focused, seeds.Count > 0 ? seeds[0] : _currentSelectedNodeId);
        }
    }

    private IReadOnlyList<string> ResolveFocusSeedIds()
    {
        if (_seedSelectionState.HasSeeds)
            return _seedSelectionState.SeedNodeIds;

        return string.IsNullOrEmpty(_currentSelectedNodeId)
            ? Array.Empty<string>()
            : new[] { _currentSelectedNodeId };
    }

    private void OnTraversalChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (TraversalComboBox.SelectedIndex < 0) return;
        _currentTraversalMode = TraversalComboBox.SelectedIndex switch
        {
            0 => GraphFocusTraversalMode.UndirectedAssociations,
            1 => GraphFocusTraversalMode.OutgoingAssociationsOnly,
            2 => GraphFocusTraversalMode.IncomingAssociationsOnly,
            3 => GraphFocusTraversalMode.AllVisibleRelations,
            _ => GraphFocusTraversalMode.UndirectedAssociations
        };
        if (_currentGraph != null)
            UpdateStats(_focusNavigationController.CurrentGraph);
    }

    // --- Seed Controls ---

    private void OnAddSeed(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentSelectedNodeId))
        {
            _seedSelectionState.Add(_currentSelectedNodeId);
            UpdateSeedButtons();
            if (_currentGraph != null)
                UpdateStats(_focusNavigationController.CurrentGraph);
        }
    }

    private void OnRemoveSeed(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_currentSelectedNodeId))
        {
            _seedSelectionState.Remove(_currentSelectedNodeId);
            UpdateSeedButtons();
            if (_currentGraph != null)
                UpdateStats(_focusNavigationController.CurrentGraph);
        }
    }

    private void OnClearSeeds(object? sender, RoutedEventArgs e)
    {
        _seedSelectionState.Clear();
        UpdateSeedButtons();
        if (_currentGraph != null)
            UpdateStats(_focusNavigationController.CurrentGraph);
    }

    private void UpdateSeedButtons()
    {
        // Could update button enabled states here based on selection
        if (_currentGraph != null)
            UpdateStats(_focusNavigationController.CurrentGraph);
    }

    // --- Search ---

    private void OnSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        string text = SearchTextBox.Text ?? string.Empty;
        GraphCanvasView.SetSearchText(text);
    }

    private void OnSearchNodeSelected(string nodeId)
    {
        if (_nodeMap.TryGetValue(nodeId, out var node))
        {
            GraphCanvasView.CenterOnNode(node);
            if (_currentGraph != null)
            {
                var typeNode = _currentGraph.Nodes.FirstOrDefault(n => n.Id == nodeId);
                if (typeNode != null)
                    UpdateInspector(typeNode);
            }
        }
    }

    private void OnFocusSearchResults(IReadOnlyList<string> nodeIds)
    {
        var visibleSet = new HashSet<string>(nodeIds);
        var filteredNodes = _allNodes.Where(n => visibleSet.Contains(n.Id)).ToList();
        var filteredEdges = _allEdges.Where(e =>
            visibleSet.Contains(e.FromNode?.Id ?? "") && visibleSet.Contains(e.ToNode?.Id ?? ""))
            .ToList();
        GraphCanvasView.SetGraph(filteredNodes, filteredEdges);
    }

    private void OnSearchCleared()
    {
        if (_currentGraph != null)
        {
            SetDisplayedGraph(_currentGraph);
        }
    }

    // --- Edge Filters ---

    private void OnEdgeFilterChanged(object? sender, RoutedEventArgs e)
    {
        GraphCanvasView.SetEdgeVisibility(
            ShowInheritanceCheck.IsChecked == true,
            ShowImplementsCheck.IsChecked == true,
            ShowAssociationsCheck.IsChecked == true);
    }

    // --- Inspector ---

    private void UpdateInspector(TypeNodeData node)
    {
        SelectedNodeText.Text = $"{node.Namespace}.{node.DisplayName}\nKind: {node.Kind}\nFile: {Path.GetFileName(node.AssetPath)}";

        InspectorNodeText.Text =
            $"Name: {node.DisplayName}\n" +
            $"Namespace: {node.Namespace}\n" +
            $"Kind: {node.Kind}\n" +
            $"File: {Path.GetFileName(node.AssetPath)}\n" +
            (node.Stereotypes.Count > 0 ? $"Stereotypes: {string.Join(", ", node.Stereotypes)}\n" : "");

        var memberItems = node.Members
            .Select(m => $"{m.Visibility} {m.Kind} {m.TypeName} {m.Name}{(m.Kind == TypeMemberKind.Method ? "()" : "")}")
            .ToList();
        InspectorMembersList.ItemsSource = memberItems;

        TypeGraph? graph = _focusNavigationController.CurrentGraph;
        if (graph == null)
        {
            InspectorOutgoingList.ItemsSource = Array.Empty<string>();
            InspectorIncomingList.ItemsSource = Array.Empty<string>();
            return;
        }

        var outgoing = graph.Edges
            .Where(e => e.FromNodeId == node.Id)
            .Select(e =>
            {
                var target = graph.Nodes.FirstOrDefault(n => n.Id == e.ToNodeId);
                string targetName = target != null ? $"{target.Namespace}.{target.DisplayName}" : e.ToNodeId;
                return $"{e.Kind} -> {targetName}" + (string.IsNullOrEmpty(e.Label) ? "" : $" [{e.Label}]");
            })
            .ToList();
        InspectorOutgoingList.ItemsSource = outgoing;

        var incoming = graph.Edges
            .Where(e => e.ToNodeId == node.Id)
            .Select(e =>
            {
                var source = graph.Nodes.FirstOrDefault(n => n.Id == e.FromNodeId);
                string sourceName = source != null ? $"{source.Namespace}.{source.DisplayName}" : e.FromNodeId;
                return $"{sourceName} -> {e.Kind}" + (string.IsNullOrEmpty(e.Label) ? "" : $" [{e.Label}]");
            })
            .ToList();
        InspectorIncomingList.ItemsSource = incoming;
    }

    // --- Manual Layout ---

    private void OnViewportChanged(float zoom, float panX, float panY, float viewW, float viewH)
    {
        MinimapView.UpdateViewport(zoom, panX, panY, viewW, viewH);
    }

    private void OnMinimapViewportJump(float newPanX, float newPanY)
    {
        GraphCanvasView.SetPan(newPanX, newPanY);
    }

    private void OnManualLayoutChanged()
    {
        if (_currentSettings.PersistManualLayout)
        {
            _cacheService.SaveManualOverrides(GraphCanvasView.ManualOverrides, _currentSettings);
        }
    }

    private void OnResetLayout(object? sender, RoutedEventArgs e)
    {
        _manualOverrides.Clear();
        _cacheService.SaveManualOverrides(_manualOverrides, _currentSettings);

        if (_currentGraph != null)
        {
            _layoutEngine.ManualOverrides = _manualOverrides;
            // Bug 02 Fix 2: don't reload from disk — we just deliberately cleared in-memory state
            SetDisplayedGraph(_currentGraph, reloadManualOverridesFromDisk: false);
        }
    }

    // --- Matrix View ---

    private bool _matrixVisible;

    private void OnToggleMatrix(object? sender, RoutedEventArgs e)
    {
        _matrixVisible = !_matrixVisible;
        MatrixView.IsVisible = _matrixVisible;
        MatrixButton.Content = _matrixVisible ? "Graph" : "Matrix";

        if (_matrixVisible && _currentGraph != null)
        {
            MatrixView.SetGraph(_currentGraph);
        }
    }

    private void OnMatrixCellClicked(string fromNamespace, string toNamespace)
    {
        // Switch back to graph view and filter to show types from both namespaces
        _matrixVisible = false;
        MatrixView.IsVisible = false;
        MatrixButton.Content = "Matrix";

        if (_currentGraph != null)
        {
            var nsSet = new HashSet<string> { fromNamespace, toNamespace };
            var filteredNodes = _allNodes.Where(n => nsSet.Contains(n.Namespace)).ToList();
            var nodeIds = new HashSet<string>(filteredNodes.Select(n => n.Id));
            var filteredEdges = _allEdges.Where(e =>
                nodeIds.Contains(e.FromNode?.Id ?? "") && nodeIds.Contains(e.ToNode?.Id ?? "")).ToList();
            GraphCanvasView.SetGraph(filteredNodes, filteredEdges);
        }
    }

    // --- Export ---

    private void OnSavePng(object? sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "export");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"diagram_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        GraphCanvasView.SaveToPng(path);
        StatsText.Text = $"Saved: {path}";
    }

    private void OnSaveMmd(object? sender, RoutedEventArgs e)
    {
        TypeGraph? graph = _focusNavigationController.CurrentGraph ?? _currentGraph;
        if (graph == null) return;

        var dir = Path.Combine(AppContext.BaseDirectory, "export");
        Directory.CreateDirectory(dir);
        string mermaid = MermaidGraphExporter.BuildDiagram(graph);
        var path = Path.Combine(dir, $"{MakeSafeFileName(graph.Title)}.mmd");
        File.WriteAllText(path, mermaid);
        StatsText.Text = $"Saved: {path}";
    }

    private void OnSaveMd(object? sender, RoutedEventArgs e)
    {
        TypeGraph? graph = _focusNavigationController.CurrentGraph ?? _currentGraph;
        if (graph == null) return;

        var dir = Path.Combine(AppContext.BaseDirectory, "export");
        Directory.CreateDirectory(dir);
        string mermaid = MermaidGraphExporter.BuildDiagram(graph);
        var path = Path.Combine(dir, $"{MakeSafeFileName(graph.Title)}.md");
        File.WriteAllText(path, "# " + graph.Title + "\n\n```mermaid\n" + mermaid + "\n```\n");
        StatsText.Text = $"Saved: {path}";
    }

    private async void OnCopyMermaid(object? sender, RoutedEventArgs e)
    {
        TypeGraph? graph = _focusNavigationController.CurrentGraph ?? _currentGraph;
        if (graph == null) return;

        string mermaid = MermaidGraphExporter.BuildDiagram(graph);
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(mermaid);
            StatsText.Text = "Mermaid copied to clipboard";
        }
    }

    private void OnOpenLiveEditor(object? sender, RoutedEventArgs e)
    {
        TypeGraph? graph = _focusNavigationController.CurrentGraph ?? _currentGraph;
        if (graph == null) return;

        string mermaid = MermaidGraphExporter.BuildDiagram(graph);
        // Mermaid Live Editor supports base64-encoded JSON with code field
        string json = "{\"code\":\"" + mermaid.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\",\"mermaid\":{}}";
        string base64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        string url = $"https://mermaid.live/edit#base64:{base64}";

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            StatsText.Text = "Opened Mermaid Live Editor";
        }
        catch { StatsText.Text = "Could not open browser"; }
    }

    private static string MakeSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "diagram";
        return safe;
    }

    private void OnOpenInExplorer(object? sender, RoutedEventArgs e)
    {
        var selected = GraphCanvasView.SelectedNode;
        if (selected == null || string.IsNullOrEmpty(selected.AssetPath)) return;

        var file = selected.AssetPath;
        if (!File.Exists(file)) return;

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{file}\"",
            UseShellExecute = true
        });
    }

    private void UpdateNavigationButtons()
    {
        BackButton.IsEnabled = _focusNavigationController.CanGoBack();
        ForwardButton.IsEnabled = _focusNavigationController.CanGoForward();
        ResetButton.IsEnabled = _focusNavigationController.IsFocusedView;
    }

    private void OnBack(object? sender, RoutedEventArgs e)
    {
        GraphViewSnapshot? snapshot = _focusNavigationController.GoBack();
        if (snapshot?.Graph != null)
            SetDisplayedGraph(snapshot.Graph, snapshot.SelectedNodeId);
    }

    private void OnForward(object? sender, RoutedEventArgs e)
    {
        GraphViewSnapshot? snapshot = _focusNavigationController.GoForward();
        if (snapshot?.Graph != null)
            SetDisplayedGraph(snapshot.Graph, snapshot.SelectedNodeId);
    }

    private void OnReset(object? sender, RoutedEventArgs e)
    {
        TypeGraph? rootGraph = _focusNavigationController.ResetToRoot();
        if (rootGraph != null)
            SetDisplayedGraph(rootGraph);
    }

    private void OnFit(object? sender, RoutedEventArgs e) => GraphCanvasView.FitToScreen();

    private void OnZoomIn(object? sender, RoutedEventArgs e) => GraphCanvasView.ZoomBy(1.2f);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => GraphCanvasView.ZoomBy(1.0f / 1.2f);

    // --- Settings ---

    private async void OnOpenSettings(object? sender, RoutedEventArgs e)
    {
        string folder = FolderTextBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(folder))
        {
            StatsText.Text = "Select a folder first to configure project settings";
            return;
        }

        var window = new SettingsWindow(_settingsService);
        window.LoadForProject(folder);
        await window.ShowDialog(this);

        if (window.SavedSettings != null)
        {
            _currentSettings = window.SavedSettings;
            StatsText.Text = "Settings saved";
            // Apply settings that affect UI immediately
            if (MinimapView != null)
                MinimapView.IsVisible = _currentSettings.ShowMinimap;
        }
    }

    // ── Keyboard shortcuts (W3) ──

    /// <summary>
    /// Handles keyboard shortcuts. Only active in Design Mode for most keys
    /// (Analyze Mode shortcuts are handled elsewhere). Per docs/design/07 W3.
    /// </summary>
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // Only handle Design Mode shortcuts when in Design Mode
        if (_designModeController.CurrentMode != AppMode.Design)
            return;

        // Ignore shortcuts during inline text editing (so typing isn't intercepted)
        if (e.Source is TextBox)
            return;

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        // Ctrl+Z — Undo
        if (ctrl && e.Key == Key.Z && !shift)
        {
            OnDesignUndo(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // Ctrl+Y or Ctrl+Shift+Z — Redo
        if ((ctrl && e.Key == Key.Y) || (ctrl && shift && e.Key == Key.Z))
        {
            OnDesignRedo(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // Delete or Backspace — delete selected
        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (_designGraph != null && _designCanvasController.HandleDeleteKey(_designGraph))
                RenderDesignModeGraph();
            e.Handled = true;
            return;
        }

        // Escape — cancel edge creation
        if (e.Key == Key.Escape)
        {
            if (_designCanvasController.IsCreatingEdge)
            {
                _designCanvasController.CancelEdgeCreation();
                GraphCanvasView.ForceRedraw();
                StatsText.Text = "Edge creation cancelled";
            }
            e.Handled = true;
            return;
        }

        // Ctrl+S — Save
        if (ctrl && e.Key == Key.S)
        {
            OnDesignSave(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // Ctrl+N — New design
        if (ctrl && e.Key == Key.N)
        {
            OnDesignNew(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        // Ctrl+O — Open design
        if (ctrl && e.Key == Key.O)
        {
            OnDesignOpen(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
    }
}
