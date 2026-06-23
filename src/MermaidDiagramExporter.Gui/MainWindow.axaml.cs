using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Input;
using Avalonia.Input.Platform;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Export;
using MermaidDiagramExporter.Focus;
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
        _scanCoordinator.CachePromptRequested += OnCachePromptRequested;
        _scanCoordinator.ScanCompleted += OnScanCompleted;
        _scanCoordinator.StatusChanged += OnScanStatusChanged;
        InitializeComponent();
        GraphCanvasView.SelectionChanged += OnCanvasSelectionChanged;
        GraphCanvasView.ManualLayoutChanged += OnManualLayoutChanged;
        GraphCanvasView.ViewportChanged += OnViewportChanged;
        SymbolSearchPanel.NodeSelected += OnSearchNodeSelected;
        SymbolSearchPanel.FocusOnResultsRequested += OnFocusSearchResults;
        SymbolSearchPanel.SearchCleared += OnSearchCleared;
        MinimapView.ViewportJumpRequested += OnMinimapViewportJump;
        MatrixView.CellClicked += OnMatrixCellClicked;
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

    private void OnScan(object? sender, RoutedEventArgs e)
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

        _scanCoordinator.CachePromptResult = null;
        var graph = _scanCoordinator.ExecuteScanFlow(folder);
        // If null, user cancelled the cache prompt — OnScanCompleted will handle success
    }

    private async void OnCachePromptRequested(CacheInfo cacheInfo, CacheValidationResult validation)
    {
        var dialog = new CachePromptDialog();
        dialog.SetInfo(cacheInfo, validation);
        await dialog.ShowDialog(this);
        _scanCoordinator.CachePromptResult = dialog.Result;
    }

    private void OnScanCompleted(TypeGraph graph)
    {
        _currentSettings = _settingsService.LoadSettings(graph.Metadata.SourceDescription);

        _currentGraph = graph;
        _focusNavigationController.SetRootGraph(_currentGraph, _currentSettings.SourceFolderPath);
        _seedSelectionState.Clear();

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

    private void OnScanStatusChanged(string status)
    {
        StatsText.Text = status;
    }

    private void SetDisplayedGraph(TypeGraph? graph, string selectedNodeId = "")
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

        // Load manual overrides before layout
        if (_currentSettings.PersistManualLayout)
        {
            _manualOverrides = _cacheService.LoadManualOverrides(_currentSettings);
        }
        else
        {
            _manualOverrides = new ManualLayoutOverrides();
        }
        _layoutEngine.ManualOverrides = _manualOverrides;

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
            SetDisplayedGraph(_currentGraph);
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
}
