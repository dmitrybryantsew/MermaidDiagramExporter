using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Focus;

namespace MermaidDiagramExporter.Gui;

public partial class MainWindow : Window
{
    private readonly RoslynTypeScanner _scanner = new();
    private readonly LayoutEngine _layoutEngine = new();
    private readonly FocusedGraphNavigationController _focusNavigationController = new();

    private List<GraphNode> _allNodes = new();
    private List<GraphEdge> _allEdges = new();
    private Dictionary<string, GraphNode> _nodeMap = new();
    private TypeGraph? _currentGraph;
    private bool _updatingClassList;

    public MainWindow()
    {
        InitializeComponent();
        GraphCanvasView.SelectionChanged += OnCanvasSelectionChanged;
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
        var folder = FolderTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            StatsText.Text = "Invalid folder";
            return;
        }

        try
        {
            _currentGraph = _scanner.ScanFolder(folder);
            _focusNavigationController.SetRootGraph(_currentGraph, folder);

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

        var (nodes, edges) = _layoutEngine.Layout(graph);
        _allNodes = nodes;
        _allEdges = edges;
        _nodeMap = nodes.ToDictionary(n => n.Id);

        GraphCanvasView.SetGraph(nodes, edges);
        UpdateClassList(graph);
        UpdateStats(graph);

        if (!string.IsNullOrEmpty(selectedNodeId))
        {
            var selectedNode = graph.Nodes.FirstOrDefault(n => n.Id == selectedNodeId);
            if (selectedNode != null)
                SelectedNodeText.Text = $"{selectedNode.Namespace}.{selectedNode.DisplayName}\nKind: {selectedNode.Kind}\nFile: {Path.GetFileName(selectedNode.AssetPath)}";
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

        TypeGraph? focusedGraph = _focusNavigationController.FocusSelection(
            node.Id,
            depth: 1,
            GraphFocusTraversalMode.UndirectedAssociations);
        if (focusedGraph != null)
            SetDisplayedGraph(focusedGraph, node.Id);
    }

    private void OnCanvasSelectionChanged(GraphNode? node)
    {
        if (node == null)
        {
            SelectedNodeText.Text = "";
            return;
        }

        SelectedNodeText.Text = $"{node.Namespace}.{node.DisplayName}\nKind: {node.Kind}\nFile: {Path.GetFileName(node.AssetPath)}";
    }

    private void OnSavePng(object? sender, RoutedEventArgs e)
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "export");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"diagram_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        GraphCanvasView.SaveToPng(path);
        StatsText.Text = $"Saved: {path}";
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

    // For zoom in/out buttons, we directly manipulate the internal zoom
    // by calling a public method we'll add to GraphCanvas
    private void OnZoomIn(object? sender, RoutedEventArgs e) => GraphCanvasView.ZoomBy(1.2f);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => GraphCanvasView.ZoomBy(1.0f / 1.2f);
}
