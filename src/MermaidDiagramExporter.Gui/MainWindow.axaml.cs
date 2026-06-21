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

namespace MermaidDiagramExporter.Gui;

public partial class MainWindow : Window
{
    private readonly RoslynTypeScanner _scanner = new();
    private readonly LayoutEngine _layoutEngine = new();
    private readonly FocusNavigator _navigator = new();

    private List<GraphNode> _allNodes = new();
    private List<GraphEdge> _allEdges = new();
    private Dictionary<string, GraphNode> _nodeMap = new();
    private TypeGraph? _currentGraph;

    public MainWindow()
    {
        InitializeComponent();
        GraphCanvasView.SelectionChanged += OnCanvasSelectionChanged;
        _navigator.FocusChanged += OnFocusChanged;
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
            var (nodes, edges) = _layoutEngine.Layout(_currentGraph);
            _allNodes = nodes;
            _allEdges = edges;
            _nodeMap = nodes.ToDictionary(n => n.Id);
            _navigator.Clear();

            GraphCanvasView.SetGraph(nodes, edges);
            UpdateClassList();
            UpdateStats();
        }
        catch (Exception ex)
        {
            StatsText.Text = $"Error: {ex.Message}";
        }
    }

    private void UpdateClassList()
    {
        var items = _allNodes
            .OrderBy(n => n.Namespace)
            .ThenBy(n => n.DisplayName)
            .Select(n => $"{n.Namespace}.{n.DisplayName}")
            .ToList();
        ClassListBox.ItemsSource = items;
    }

    private void UpdateStats()
    {
        if (_currentGraph == null) return;
        StatsText.Text = $"{_currentGraph.Nodes.Count} types, {_currentGraph.Edges.Count} relationships";
    }

    private void OnClassSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ClassListBox.SelectedItem is not string selected) return;
        var node = _allNodes.FirstOrDefault(n => $"{n.Namespace}.{n.DisplayName}" == selected);
        if (node == null) return;

        _navigator.FocusOn(node.Id, _nodeMap, _allEdges);
        ApplyFocusFilter();
        GraphCanvasView.InvalidateVisual();
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

    private void OnFocusChanged()
    {
        BackButton.IsEnabled = _navigator.CanGoBack;
        ForwardButton.IsEnabled = _navigator.CanGoForward;
        ResetButton.IsEnabled = _navigator.CurrentFocus != null;
        ApplyFocusFilter();
    }

    private void ApplyFocusFilter()
    {
        var visibleIds = _navigator.GetVisibleNodeIds(_nodeMap, _allEdges);
        if (visibleIds == null)
        {
            GraphCanvasView.SetGraph(_allNodes, _allEdges);
            return;
        }

        var filteredNodes = _allNodes.Where(n => visibleIds.Contains(n.Id)).ToList();
        var filteredEdges = _allEdges
            .Where(ed => ed.FromNode != null && ed.ToNode != null &&
                         visibleIds.Contains(ed.FromNode.Id) && visibleIds.Contains(ed.ToNode.Id))
            .ToList();
        GraphCanvasView.SetGraph(filteredNodes, filteredEdges);
    }

    private void OnBack(object? sender, RoutedEventArgs e) => _navigator.GoBack();
    private void OnForward(object? sender, RoutedEventArgs e) => _navigator.GoForward();
    private void OnReset(object? sender, RoutedEventArgs e) => _navigator.Clear();

    private void OnFit(object? sender, RoutedEventArgs e) => GraphCanvasView.FitToScreen();

    // For zoom in/out buttons, we directly manipulate the internal zoom
    // by calling a public method we'll add to GraphCanvas
    private void OnZoomIn(object? sender, RoutedEventArgs e) => GraphCanvasView.ZoomBy(1.2f);
    private void OnZoomOut(object? sender, RoutedEventArgs e) => GraphCanvasView.ZoomBy(1.0f / 1.2f);
}
