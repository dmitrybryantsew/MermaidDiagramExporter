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
using MermaidDiagramExporter.Llm;
using SkiaSharp;

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
    private DesignInspectorViewModel? _inspectorVm;
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
    private int _namespaceFocusDepth = 1;
    private bool _isPopulatingNamespaceCombo;

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

        // Set window icon from embedded AvaloniaResource
        try
        {
            var iconUri = new Uri("avares://MermaidDiagramExporter.Gui/Assets/icon.png");
            Icon = new Avalonia.Controls.WindowIcon(Avalonia.Platform.AssetLoader.Open(iconUri));
        }
        catch { /* non-critical: icon is cosmetic */ }

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
        _designCanvasController.ToolChanged += OnDesignToolChanged;
        GraphCanvasView.DesignClassDoubleClicked += OnDesignClassDoubleClicked;
        GraphCanvasView.DesignContextMenuRequested += OnDesignContextMenuRequested;

        // Inspector panel wiring (docs/design/10 GAP-1)
        _designCanvasController.SelectionChanged += OnDesignSelectionChanged;

        // Initialize mode toggle UI (default is Analyze Mode)
        UpdateModeUi();
        MatrixView.CellClicked += OnMatrixCellClicked;
    }

    // --- Mode toggle (M0 scaffold) ---

    private void OnAnalyzeModeClick(object? sender, RoutedEventArgs e)
    {
        // Reset all Design Mode interaction state (drags, selection, edge creation)
        // to prevent state leaks when dragging in Analyze Mode.
        _designCanvasController.ResetAllState();
        GraphCanvasView.SetDesignGraph(null);
        _designModeController.EnterAnalyzeMode();
        UpdateModeUi();
        if (_currentGraph != null)
            SetDisplayedGraph(_currentGraph, reloadManualOverridesFromDisk: false);
        UpdateStatusBar();
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
        UpdateStatusBar();
    }

    /// <summary>
    /// "Edit in Design Mode" button: converts the current Analyze Mode graph
    /// (scanned or focused) into an editable DesignGraph and switches to
    /// Design Mode. Preserves layout positions from the canvas.
    /// </summary>
    private void OnEditInDesignMode(object? sender, RoutedEventArgs e)
    {
        TypeGraph? source = _focusNavigationController.CurrentGraph ?? _currentGraph;
        if (source == null)
        {
            StatsText.Text = "Scan a folder first to create a design from it.";
            return;
        }

        // Pass current canvas layout nodes so positions are preserved
        var layoutNodes = GraphCanvasView.GetCurrentNodes();
        var design = DesignExporter.FromTypeGraph(source, layoutNodes);

        _designGraph = design;
        _designModeController.EnterDesignMode(design);
        _designIsDirty = true;
        UpdateModeUi();
        RenderDesignModeGraph();
        UpdateStatusBar();
        StatsText.Text = $"Design created from: {source.Title} ({design.Classes.Count} classes)";
    }

    /// <summary>
    /// Renders the current Design Mode graph by converting it to a TypeGraph,
    /// running it through the layout engine, and feeding the result into the
    /// canvas. Called on mode entry and after every mutation (via
    /// <see cref="OnDesignGraphMutated"/>).
    /// </summary>
    /// <param name="preserveViewport">If true, keeps the current pan/zoom instead of fitting to screen.</param>
    private void RenderDesignModeGraph(bool preserveViewport = false)
    {
        if (_designGraph == null) return;
        if (_designModeController.CurrentMode != AppMode.Design) return;

        var typeGraph = DesignExporter.ToTypeGraph(_designGraph);
        GraphCanvasView.SetDesignGraph(_designGraph);

        // Build LayoutResult directly from DesignClass positions — do NOT run
        // the layout algorithm, which would override DesignClass.X/Y. Per
        // docs/design/05 (position authority), docs/design/08 D6, and
        // docs/design/09 BUG-1.
        var layoutResult = BuildLayoutResultFromDesignGraph(_designGraph);

        // Route edges so they get proper orthogonal/cluster-aware paths instead
        // of straight lines. This populates EdgePaths which the renderer uses.
        var options = _layoutEngine.LayoutOptions ?? new LayoutOptions
        {
            UseCompoundLayoutEngine = _currentSettings.UseCompoundLayoutEngine,
            UseMsaglEngine = _currentSettings.UseMsaglEngine,
        };
        layoutResult.EdgePaths = _layoutEngine.RouteEdges(typeGraph, layoutResult, options);

        var (nodes, edges) = _layoutEngine.LayoutFromLayoutResult(typeGraph, layoutResult);

        GraphCanvasView.SetGraph(nodes, edges, preserveViewport);
        MinimapView.SetGraph(nodes, edges);
        GraphCanvasView.SetDesignSelection(new HashSet<string>(_designCanvasController.Selection.SelectedClassIds));
        StatsText.Text = $"Design: {_designGraph.Classes.Count} classes, {_designGraph.Edges.Count} edges";
    }

    /// <summary>
    /// Builds a <see cref="MermaidDiagramExporter.Gui.Layout.LayoutResult"/>
    /// directly from <see cref="DesignClass.X/Y/Width/Height"/>. Used in
    /// Design Mode where manual positions are authoritative. Per
    /// docs/design/09 BUG-1.
    /// </summary>
    private static MermaidDiagramExporter.Gui.Layout.LayoutResult BuildLayoutResultFromDesignGraph(DesignGraph graph)
    {
        var nodeBounds = new Dictionary<string, MermaidDiagramExporter.Gui.Layout.Rect>();
        var clusterBounds = new Dictionary<string, MermaidDiagramExporter.Gui.Layout.Rect>();
        var nodeClusterIds = new Dictionary<string, string>();
        var clusterVisuals = new Dictionary<string, MermaidDiagramExporter.Gui.Layout.LayoutClusterVisual>();

        // Place each class at its authoritative position
        foreach (var cls in graph.Classes)
        {
            nodeBounds[cls.Id] = new MermaidDiagramExporter.Gui.Layout.Rect(cls.X, cls.Y, cls.Width, cls.Height);

            // Map to namespace cluster if present
            var ns = cls.Namespace ?? "";
            if (!string.IsNullOrEmpty(ns))
            {
                var clusterId = $"Namespace:{ns}";
                nodeClusterIds[cls.Id] = clusterId;
            }
        }

        // Compute cluster bounds as the union of member class bounds
        var byNamespace = graph.Classes
            .Where(c => !string.IsNullOrEmpty(c.Namespace ?? ""))
            .GroupBy(c => c.Namespace!);
        foreach (var nsGroup in byNamespace)
        {
            var clusterId = $"Namespace:{nsGroup.Key}";
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var cls in nsGroup)
            {
                if (cls.X < minX) minX = cls.X;
                if (cls.Y < minY) minY = cls.Y;
                if (cls.X + cls.Width > maxX) maxX = cls.X + cls.Width;
                if (cls.Y + cls.Height > maxY) maxY = cls.Y + cls.Height;
            }
            // Pad the cluster bounds so the cluster visually contains its members
            const float padding = 20f;
            clusterBounds[clusterId] = new MermaidDiagramExporter.Gui.Layout.Rect(
                minX - padding, minY - padding,
                (maxX - minX) + padding * 2f, (maxY - minY) + padding * 2f);
            clusterVisuals[clusterId] = new MermaidDiagramExporter.Gui.Layout.LayoutClusterVisual
            {
                Label = nsGroup.Key
            };
        }

        // Compute content size as the bounding box of all classes
        float contentMinX = float.MaxValue, contentMinY = float.MaxValue;
        float contentMaxX = float.MinValue, contentMaxY = float.MinValue;
        foreach (var cls in graph.Classes)
        {
            if (cls.X < contentMinX) contentMinX = cls.X;
            if (cls.Y < contentMinY) contentMinY = cls.Y;
            if (cls.X + cls.Width > contentMaxX) contentMaxX = cls.X + cls.Width;
            if (cls.Y + cls.Height > contentMaxY) contentMaxY = cls.Y + cls.Height;
        }
        var contentSize = new MermaidDiagramExporter.Gui.Layout.Vector2(
            contentMaxX - contentMinX + 100f, contentMaxY - contentMinY + 100f);

        return new MermaidDiagramExporter.Gui.Layout.LayoutResult
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            NodeClusterIds = nodeClusterIds,
            ClusterVisuals = clusterVisuals,
            EdgePaths = Array.Empty<MermaidDiagramExporter.Gui.Layout.LayoutEdgePath>(),
            ContentSize = contentSize
        };
    }

    /// <summary>
    /// Re-renders the canvas after any Design Mode mutation. Subscribed to
    /// <see cref="DesignCanvasController.GraphMutated"/>.
    /// </summary>
    private void OnDesignGraphMutated(object? sender, EventArgs e)
    {
        if (_designModeController.CurrentMode != AppMode.Design) return;
        _designIsDirty = true;
        RenderDesignModeGraph(preserveViewport: true);
        // Refresh the inspector to reflect member/class changes
        OnDesignSelectionChanged(this, _designCanvasController.Selection);
        TryAutoSave();
        UpdateStatusBar();
    }

    /// <summary>
    /// Updates the status bar when the armed tool changes.
    /// </summary>
    private void OnDesignToolChanged(object? sender, EventArgs e)
    {
        UpdateStatusBar();
    }

    /// <summary>
    /// Updates the bottom status bar with current tool, selection, and summary info.
    /// Implements UIContract §14.
    /// </summary>
    private void UpdateStatusBar()
    {
        if (_designModeController.CurrentMode != AppMode.Design)
        {
            StatusBarText.Text = "Analyze Mode";
            return;
        }

        var toolName = _designCanvasController.CurrentTool.ToString();
        var sticky = _designCanvasController.IsToolSticky ? " (sticky)" : "";
        var selInfo = _designCanvasController.Selection.SelectedClassIds.Count switch
        {
            0 => "",
            1 => " | Selected: " + (_designGraph?.Classes.FirstOrDefault(c => c.Id == _designCanvasController.Selection.SelectedClassIds[0])?.Name ?? "?"),
            var n => $" | {n} classes selected"
        };
        var counts = _designGraph != null
            ? $" | {_designGraph.Classes.Count} classes, {_designGraph.Edges.Count} edges"
            : "";

        if (_designCanvasController.IsCreatingEdge)
        {
            // Port-drag edge creation (source is being dragged)
            if (_designCanvasController.EdgeSourceClassId == null)
            {
                StatusBarText.Text = "Creating edge... drag to target class. (Esc to cancel)";
                return;
            }
            // Keyboard-initiated edge creation (source pre-selected)
            var edgeKind = DesignCanvasController.ToolToEdgeKind(_designCanvasController.CurrentTool);
            var srcName = _designGraph?.Classes.FirstOrDefault(c => c.Id == _designCanvasController.EdgeSourceClassId)?.Name ?? "?";
            StatusBarText.Text = $"Edge: {edgeKind} — {srcName} is source. Click target class. (Esc to cancel)";
            return;
        }

        StatusBarText.Text = $"Tool: {toolName}{sticky}{selInfo}{counts}";
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
    /// Shows a context menu appropriate for what was right-clicked. Implements
    /// class/member/canvas actions per docs/design/07 W6.
    /// </summary>
    private void OnDesignContextMenuRequested(DesignContextTarget target)
    {
        if (_designGraph == null) return;

        var menu = new Avalonia.Controls.MenuFlyout();

        switch (target.Kind)
        {
            case DesignContextTargetKind.Class:
                if (target.ClassId != null)
                {
                    var classId = target.ClassId;

                    var renameItem = new Avalonia.Controls.MenuItem { Header = "Rename" };
                    renameItem.Click += (_, _) => OnDesignClassDoubleClicked(classId);
                    menu.Items.Add(renameItem);

                    var deleteItem = new Avalonia.Controls.MenuItem { Header = "Delete" };
                    deleteItem.Click += (_, _) =>
                    {
                        // Select the class first, then delete
                        _designCanvasController.HandlePointerPressed(
                            new SKPoint(target.WorldPosition.X, target.WorldPosition.Y),
                            _designGraph, new System.Collections.Generic.List<SKPoint>());
                        if (_designCanvasController.HandleDeleteKey(_designGraph))
                            RenderDesignModeGraph();
                    };
                    menu.Items.Add(deleteItem);

                    var addMemberItem = new Avalonia.Controls.MenuItem { Header = "Add Member →" };

                    var fieldItem = new Avalonia.Controls.MenuItem { Header = "Field" };
                    fieldItem.Click += (_, _) => _designCanvasController.AddMemberToSelectedClass(_designGraph, MemberKind.Field);
                    addMemberItem.Items.Add(fieldItem);

                    var propertyItem = new Avalonia.Controls.MenuItem { Header = "Property" };
                    propertyItem.Click += (_, _) => _designCanvasController.AddMemberToSelectedClass(_designGraph, MemberKind.Property);
                    addMemberItem.Items.Add(propertyItem);

                    var methodItem = new Avalonia.Controls.MenuItem { Header = "Method" };
                    methodItem.Click += (_, _) => _designCanvasController.AddMemberToSelectedClass(_designGraph, MemberKind.Method);
                    addMemberItem.Items.Add(methodItem);

                    menu.Items.Add(addMemberItem);
                }
                break;

            case DesignContextTargetKind.Member:
                if (target.ClassId != null && target.MemberIndex.HasValue)
                {
                    var classId = target.ClassId;
                    var memberIndex = target.MemberIndex.Value;

                    var deleteItem = new Avalonia.Controls.MenuItem { Header = "Delete Member" };
                    deleteItem.Click += (_, _) =>
                    {
                        _designCanvasController.RemoveMember(_designGraph, classId, memberIndex);
                        RenderDesignModeGraph();
                    };
                    menu.Items.Add(deleteItem);

                    var visItem = new Avalonia.Controls.MenuItem { Header = "Cycle Visibility" };
                    visItem.Click += (_, _) =>
                    {
                        _designCanvasController.CycleMemberVisibility(_designGraph, classId, memberIndex);
                        RenderDesignModeGraph();
                    };
                    menu.Items.Add(visItem);

                    var upItem = new Avalonia.Controls.MenuItem { Header = "Move Up" };
                    upItem.Click += (_, _) =>
                    {
                        _designCanvasController.MoveMember(_designGraph, classId, memberIndex, -1);
                        RenderDesignModeGraph();
                    };
                    menu.Items.Add(upItem);

                    var downItem = new Avalonia.Controls.MenuItem { Header = "Move Down" };
                    downItem.Click += (_, _) =>
                    {
                        _designCanvasController.MoveMember(_designGraph, classId, memberIndex, 1);
                        RenderDesignModeGraph();
                    };
                    menu.Items.Add(downItem);
                }
                break;

            case DesignContextTargetKind.EmptyCanvas:
            default:
                var addItem = new Avalonia.Controls.MenuItem { Header = "Add Class Here" };
                addItem.Click += (_, _) =>
                {
                    // Route through ExecuteCommand so undo + auto-save-dirty work.
                    // Per docs/design/09 BUG-2.
                    if (_designGraph == null) return;
                    var newClass = new DesignClass
                    {
                        Name = "NewClass",
                        X = target.WorldPosition.X - 100f,
                        Y = target.WorldPosition.Y - 30f,
                        Width = 200f,
                        Height = 60f
                    };
                    var cmd = new DesignCommands.AddClass(newClass);
                    _designCanvasController.ExecuteCommand(cmd, _designGraph);
                    // ExecuteCommand fires GraphMutated → OnDesignGraphMutated → dirty flag + auto-save
                };
                menu.Items.Add(addItem);
                break;
        }

        if (menu.Items.Count > 0)
        {
            // Use a Popup with PlacementMode.Pointer so the menu anchors to the
            // cursor, not to the MainWindow. Per docs/design/09 BUG-3.
            var popup = new Avalonia.Controls.Primitives.Popup
            {
                Placement = Avalonia.Controls.PlacementMode.Pointer,
                Child = BuildMenuContentPanel(menu),
                HorizontalOffset = 0,
                VerticalOffset = 0
            };
            popup.IsOpen = true;
        }
    }

    /// <summary>
    /// Wraps a MenuFlyout's items in a StackPanel so they can be hosted
    /// inside a Popup. MenuFlyout itself requires a control anchor and
    /// doesn't support pointer-anchored placement.
    /// </summary>
    private static Avalonia.Controls.Panel BuildMenuContentPanel(Avalonia.Controls.MenuFlyout menu)
    {
        var panel = new Avalonia.Controls.StackPanel { Background = Avalonia.Media.Brush.Parse("#2A2E34") };
        foreach (var item in menu.Items)
        {
            if (item is Avalonia.Controls.MenuItem mi)
                panel.Children.Add(mi);
        }
        return panel;
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
        _designCanvasController.ResetTool();
        StatsText.Text = "New design created";
        UpdateStatusBar();
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
        _designCanvasController.ResetTool();
        StatsText.Text = $"Opened: {Path.GetFileName(path)}";
        UpdateStatusBar();
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
        {
            RenderDesignModeGraph(preserveViewport: true);
            StatsText.Text = "Undone";
        }
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
        {
            RenderDesignModeGraph(preserveViewport: true);
            StatsText.Text = "Redone";
        }
        else
            StatsText.Text = "Nothing to redo";
    }

    /// <summary>
    /// Resets Design Mode layout by running the layout engine on the design
    /// graph and writing the computed positions back to each DesignClass.
    /// This lets the user apply MSAGL or compound layout to their design
    /// diagram, just like the "Reset Layout" button in Analyze mode.
    /// </summary>
    private void OnDesignResetLayout(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null || _designGraph.Classes.Count == 0) return;

        var typeGraph = DesignExporter.ToTypeGraph(_designGraph);

        // Run the layout engine (MSAGL / compound / simple depending on settings)
        var options = _layoutEngine.LayoutOptions ?? new LayoutOptions
        {
            UseCompoundLayoutEngine = _currentSettings.UseCompoundLayoutEngine,
            UseMsaglEngine = _currentSettings.UseMsaglEngine,
        };
        _layoutEngine.LayoutOptions = options;

        var layoutResult = _layoutEngine.Layout(typeGraph);

        // Write computed positions back to the DesignClasses
        var (nodes, _) = layoutResult;
        foreach (var node in nodes)
        {
            var cls = _designGraph.Classes.FirstOrDefault(c => c.Id == node.Id);
            if (cls != null)
            {
                cls.X = node.X;
                cls.Y = node.Y;
                cls.Width = node.Width;
                cls.Height = node.Height;
            }
        }

        _designIsDirty = true;
        RenderDesignModeGraph();
        StatsText.Text = "Layout reset";
    }

    /// <summary>
    /// Arms the Class tool. The user then clicks on the canvas to place the class.
    /// (UIContract §4: tool-first creation).
    /// </summary>
    private void OnDesignAddClass(object? sender, RoutedEventArgs e)
    {
        _designCanvasController.ArmTool(DesignTool.Class, sticky: false);
        UpdateStatusBar();
    }

    /// <summary>
    /// Handles edge type selection from the dropdown. Arms the corresponding edge tool.
    /// If exactly one class is selected, sets it as the source (Method C, UIContract §6).
    /// Also updates <see cref="DesignCanvasController.DefaultEdgeKind"/> so port-drag
    /// and the Connect button use the selected type.
    /// </summary>
    private void OnDesignEdgeTypeSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_designGraph == null) return;
        if (DesignEdgeCombo.SelectedIndex < 0) return;

        DesignTool edgeTool = DesignEdgeCombo.SelectedIndex switch
        {
            0 => DesignTool.EdgeAssociation,
            1 => DesignTool.EdgeInheritance,
            2 => DesignTool.EdgeImplements,
            3 => DesignTool.EdgeDependency,
            4 => DesignTool.EdgeAggregation,
            5 => DesignTool.EdgeComposition,
            _ => DesignTool.EdgeAssociation
        };

        // Update the default edge kind so port-drag and Connect button use it
        _designCanvasController.DefaultEdgeKind = DesignCanvasController.ToolToEdgeKind(edgeTool);

        var selectedIds = _designCanvasController.Selection.SelectedClassIds;
        if (selectedIds.Count == 1)
        {
            _designCanvasController.BeginEdgeCreationWithSource(_designGraph, selectedIds[0], edgeTool, sticky: false);
        }
        else
        {
            _designCanvasController.ArmTool(edgeTool, sticky: false);
        }
        UpdateStatusBar();
    }

    /// <summary>
    /// Connect button handler: creates an edge between the two currently
    /// selected classes using the current DefaultEdgeKind. Requires exactly
    /// two classes selected (UIContract §6 — select two, press Connect).
    /// </summary>
    private void OnDesignConnect(object? sender, RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        var selectedIds = _designCanvasController.Selection.SelectedClassIds;
        if (selectedIds.Count != 2)
        {
            StatsText.Text = "Select exactly 2 classes to connect.";
            return;
        }
        if (_designCanvasController.ConnectSelected(_designGraph))
        {
            StatsText.Text = $"Connected: {_designGraph.Classes.FirstOrDefault(c => c.Id == selectedIds[0])?.Name} → {_designGraph.Classes.FirstOrDefault(c => c.Id == selectedIds[1])?.Name}";
        }
        else
        {
            StatsText.Text = "Could not create edge (self-loop or duplicate?).";
        }
        UpdateStatusBar();
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
    /// Opens the LLM Generate dialog. On successful generation + Apply,
    /// merges the LLM-produced classes and edges into the active DesignGraph.
    /// </summary>
    private async void OnDesignLlmGenerate(object? sender, RoutedEventArgs e)
    {
        var llmSettings = _currentSettings.Llm ?? new Llm.LlmSettings();
        if (!llmSettings.IsConfigured)
        {
            StatsText.Text = "LLM not configured — open Settings to set provider/model/key";
            return;
        }

        var dialog = new LlmGenerateDialog(llmSettings);
        await dialog.ShowDialog(this);

        // If the user clicked "Apply to Design", merge the generated content
        if (dialog.AppliedResult == null || !dialog.AppliedResult.GeneratedOk) return;

        var gen = dialog.AppliedResult;

        // Ensure we have a design graph
        if (_designGraph == null)
        {
            _designGraph = new DesignGraph { Title = "LLM-Generated Design" };
            _designModeController.EnterDesignMode(_designGraph);
        }

        // Map generated class names to existing IDs (for edge resolution)
        var classNameToId = _designGraph!.Classes.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var newClassIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // genName -> newId

        // Add new classes (skip duplicates by name)
        int addedClasses = 0;
        foreach (var clsDto in gen.Classes)
        {
            if (classNameToId.ContainsKey(clsDto.Name)) continue;

            var kind = clsDto.Kind switch
            {
                "Interface" => ClassKind.Interface,
                "Enum" => ClassKind.Enum,
                "Struct" => ClassKind.Struct,
                "StaticClass" => ClassKind.StaticClass,
                "AbstractClass" => ClassKind.AbstractClass,
                _ => ClassKind.Class
            };

            var designClass = new DesignClass
            {
                Name = clsDto.Name,
                Namespace = clsDto.Namespace,
                Kind = kind,
                Stereotype = string.IsNullOrWhiteSpace(clsDto.Stereotype) ? null : clsDto.Stereotype,
                Members = clsDto.Members.Select(m => new DesignMember
                {
                    Name = m.Name,
                    TypeName = m.TypeName,
                    Kind = m.Kind switch
                    {
                        "Property" => MemberKind.Property,
                        "Method" => MemberKind.Method,
                        "Constructor" => MemberKind.Constructor,
                        "Event" => MemberKind.Event,
                        _ => MemberKind.Field
                    },
                    Visibility = m.Visibility switch
                    {
                        "Private" => Visibility.Private,
                        "Protected" => Visibility.Protected,
                        "Internal" => Visibility.Internal,
                        _ => Visibility.Public
                    },
                    Parameters = m.Parameters.Select(p => new DesignParameter
                    {
                        Name = p.Name,
                        TypeName = p.TypeName
                    }).ToList()
                }).ToList()
            };

            _designGraph.Classes.Add(designClass);
            classNameToId[designClass.Name] = designClass.Id;
            newClassIds[clsDto.Name] = designClass.Id;
            addedClasses++;
        }

        // Add new edges (resolve class names to IDs)
        int addedEdges = 0;
        foreach (var edgeDto in gen.Edges)
        {
            if (!classNameToId.TryGetValue(edgeDto.FromClassName, out var fromId)) continue;
            if (!classNameToId.TryGetValue(edgeDto.ToClassName, out var toId)) continue;

            // Skip duplicate edges
            bool exists = _designGraph.Edges.Any(e =>
                e.FromClassId == fromId && e.ToClassId == toId);
            if (exists) continue;

            var edgeKind = edgeDto.Kind switch
            {
                "Inheritance" => EdgeKind.Inheritance,
                "Implements" => EdgeKind.Implements,
                "Dependency" => EdgeKind.Dependency,
                "Aggregation" => EdgeKind.Aggregation,
                "Composition" => EdgeKind.Composition,
                _ => EdgeKind.Association
            };

            _designGraph.Edges.Add(new DesignEdge
            {
                FromClassId = fromId,
                ToClassId = toId,
                Kind = edgeKind,
                Label = edgeDto.Label
            });
            addedEdges++;
        }

        // Add new namespaces
        foreach (var ns in gen.Namespaces)
        {
            if (_designGraph.Namespaces.Any(n => n.Name == ns)) continue;
            _designGraph.Namespaces.Add(new DesignNamespace { Name = ns });
        }

        // Auto-arrange new classes that have X=Y=0
        AutoArrangeNewClasses(_designGraph);

        _designIsDirty = true;
        _designModeController.EnterDesignMode(_designGraph);
        RenderDesignModeGraph(preserveViewport: true);

        StatsText.Text = $"LLM: added {addedClasses} classes, {addedEdges} edges";
    }

    /// <summary>
    /// Places classes with X=Y=0 into a grid below/beside existing positioned classes.
    /// </summary>
    private static void AutoArrangeNewClasses(DesignGraph graph)
    {
        const float cellW = 260f;
        const float cellH = 160f;
        const int cols = 4;

        var unpositioned = graph.Classes.Where(c => c.X == 0 && c.Y == 0).ToList();
        if (unpositioned.Count == 0) return;

        float startX = 0, startY = 0;
        var positioned = graph.Classes.Where(c => c.X != 0 || c.Y != 0).ToList();
        if (positioned.Count > 0)
        {
            startX = positioned.Max(c => c.X + c.Width) + 40f;
            startY = positioned.Min(c => c.Y);
        }

        int i = 0;
        foreach (var cls in unpositioned)
        {
            int col = i % cols;
            int row = i / cols;
            cls.X = startX + col * cellW;
            cls.Y = startY + row * cellH;
            i++;
        }
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

        // Switch inspector panel between Analyze and Design states
        AnalyzeInspectorState.IsVisible = !isDesign;
        InspectorNothingState.IsVisible = isDesign && _designCanvasController.Selection.SelectedClassIds.Count == 0;
        InspectorSingleClassState.IsVisible = isDesign && _designCanvasController.Selection.SelectedClassIds.Count == 1;
        InspectorMultiSelectState.IsVisible = isDesign && _designCanvasController.Selection.SelectedClassIds.Count > 1;

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
            GraphCanvasView.SetEdgeStyles(_currentSettings.EdgeStyles);

            // Phase 4: Update UI on the main thread (touches Avalonia controls)
            SetDisplayedGraph(_currentGraph);
            PopulateNamespaceFocusDropdown(_currentGraph);
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
            UseCompoundLayoutEngine = _currentSettings.UseCompoundLayoutEngine,
            UseMsaglEngine = _currentSettings.UseMsaglEngine,
            SeparateAppAndTests = _currentSettings.SeparateAppAndTests,
            PartitionByFirstLevelNamespace = _currentSettings.PartitionByFirstLevelNamespace,
        };
        AutoRedrawCheck.IsChecked = _currentSettings.AutoRedrawEdges;

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
            AnalyzeInspectorTitle.Text = "No node selected";
            AnalyzeInspectorKind.Text = "";
            AnalyzeInspectorFile.Text = "";
            AnalyzeInspectorMembers.ItemsSource = Array.Empty<string>();
            AnalyzeInspectorOutgoing.ItemsSource = Array.Empty<string>();
            AnalyzeInspectorIncoming.ItemsSource = Array.Empty<string>();
            _currentSelectedNodeId = string.Empty;
            UpdateSeedButtons();
            return;
        }

        _currentSelectedNodeId = node.Id;

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

    private void OnAutoRedrawChanged(object? sender, RoutedEventArgs e)
    {
        _currentSettings.AutoRedrawEdges = AutoRedrawCheck.IsChecked == true;
    }

    // --- Namespace Focus ---

    private void PopulateNamespaceFocusDropdown(Core.TypeGraph? graph)
    {
        _isPopulatingNamespaceCombo = true;
        var namespaces = NamespaceFocusHelper.GetTopLevelNamespaces(graph);
        var items = new List<string> { "(all namespaces)" };
        items.AddRange(namespaces);
        NamespaceFocusCombo.ItemsSource = items;
        NamespaceFocusCombo.SelectedIndex = 0;
        _isPopulatingNamespaceCombo = false;
    }

    private void OnNamespaceFocusChanged(object? sender, SelectionChangedEventArgs e)
    {
        ApplyNamespaceFocus();
    }

    private void OnNamespaceFocusToggleChanged(object? sender, RoutedEventArgs e)
    {
        ApplyNamespaceFocus();
    }

    private void OnNamespaceFocusD1(object? sender, RoutedEventArgs e) { _namespaceFocusDepth = 1; ApplyNamespaceFocus(); }
    private void OnNamespaceFocusD2(object? sender, RoutedEventArgs e) { _namespaceFocusDepth = 2; ApplyNamespaceFocus(); }
    private void OnNamespaceFocusD3(object? sender, RoutedEventArgs e) { _namespaceFocusDepth = 3; ApplyNamespaceFocus(); }

    private void ApplyNamespaceFocus()
    {
        if (_isPopulatingNamespaceCombo) return;
        if (_currentGraph == null) return;
        if (NamespaceFocusCombo.SelectedIndex <= 0)
        {
            // "(all namespaces)" selected — reset to root
            var rootGraph = _focusNavigationController.ResetToRoot();
            if (rootGraph != null) SetDisplayedGraph(rootGraph);
            return;
        }

        string? ns = NamespaceFocusCombo.SelectedItem as string;
        if (string.IsNullOrEmpty(ns) || ns == "(all namespaces)") return;

        bool showConnected = ShowConnectedNamespacesCheck.IsChecked == true;

        if (!showConnected)
        {
            // Just the selected namespace — filter nodes directly
            var nsNodeIds = NamespaceFocusHelper.GetNodeIdsInNamespace(_currentGraph, ns);
            var filteredNodes = _allNodes.Where(n => nsNodeIds.Contains(n.Id)).ToList();
            var nodeIds = new HashSet<string>(filteredNodes.Select(n => n.Id));
            var filteredEdges = _allEdges.Where(e =>
                nodeIds.Contains(e.FromNode?.Id ?? "") && nodeIds.Contains(e.ToNode?.Id ?? "")).ToList();
            GraphCanvasView.SetGraph(filteredNodes, filteredEdges);
            StatsText.Text = $"Namespace: {ns} ({filteredNodes.Count} classes)";
            return;
        }

        // Show connected — use the focus BFS from all nodes in the namespace
        var seedIds = NamespaceFocusHelper.GetNodeIdsInNamespace(
            _focusNavigationController.RootGraph ?? _currentGraph, ns);
        if (seedIds.Count == 0) return;

        // Reset to root first so the BFS explores the full graph, not a
        // potentially already-focused subgraph
        _focusNavigationController.ResetToRoot();
        if (!_focusNavigationController.CanFocusSelection(seedIds.ToList())) return;

        var focused = _focusNavigationController.FocusSelection(
            seedIds.ToList(), _namespaceFocusDepth, GraphFocusTraversalMode.AllVisibleRelations);
        if (focused != null)
        {
            SetDisplayedGraph(focused);
            StatsText.Text = $"Namespace: {ns} + connected D{_namespaceFocusDepth} ({focused.Nodes.Count} classes)";
        }
    }

    // --- Inspector ---

    private void UpdateInspector(TypeNodeData node)
    {
        AnalyzeInspectorTitle.Text = $"{node.Namespace}.{node.DisplayName}";
        AnalyzeInspectorKind.Text = $"Kind: {node.Kind}";
        AnalyzeInspectorFile.Text = $"File: {Path.GetFileName(node.AssetPath)}";

        var memberItems = node.Members
            .Select(m => $"{m.Visibility} {m.Kind} {m.TypeName} {m.Name}{(m.Kind == TypeMemberKind.Method ? "()" : "")}")
            .ToList();
        AnalyzeInspectorMembers.ItemsSource = memberItems;

        TypeGraph? graph = _focusNavigationController.CurrentGraph;
        if (graph == null)
        {
            AnalyzeInspectorOutgoing.ItemsSource = Array.Empty<string>();
            AnalyzeInspectorIncoming.ItemsSource = Array.Empty<string>();
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
        AnalyzeInspectorOutgoing.ItemsSource = outgoing;

        var incoming = graph.Edges
            .Where(e => e.ToNodeId == node.Id)
            .Select(e =>
            {
                var source = graph.Nodes.FirstOrDefault(n => n.Id == e.FromNodeId);
                string sourceName = source != null ? $"{source.Namespace}.{source.DisplayName}" : e.FromNodeId;
                return $"{sourceName} -> {e.Kind}" + (string.IsNullOrEmpty(e.Label) ? "" : $" [{e.Label}]");
            })
            .ToList();
        AnalyzeInspectorIncoming.ItemsSource = incoming;
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
        if (_currentSettings.PersistManualLayout && !string.IsNullOrWhiteSpace(_currentSettings.SourceFolderPath))
        {
            _cacheService.SaveManualOverrides(GraphCanvasView.ManualOverrides, _currentSettings);
        }

        // Auto-redraw edges if enabled (re-routes with current node positions)
        if (_currentSettings.AutoRedrawEdges && _currentGraph != null && _allNodes.Count > 0)
        {
            RedrawEdgesNow();
        }
    }

    /// <summary>
    /// Re-routes all edges using current node positions. Called on Ctrl+R
    /// (manual) or after drag (if AutoRedrawEdges is on).
    /// </summary>
    private void RedrawEdgesNow()
    {
        if (_currentGraph == null || _allNodes.Count == 0) return;

        var options = _layoutEngine.LayoutOptions ?? new LayoutOptions
        {
            UseCompoundLayoutEngine = _currentSettings.UseCompoundLayoutEngine,
            UseMsaglEngine = _currentSettings.UseMsaglEngine,
        };

        _layoutEngine.RedrawEdges(_currentGraph, _allNodes, _allEdges, options);
        GraphCanvasView.SetGraph(_allNodes, _allEdges, preserveViewport: true);
    }

    private void OnResetLayout(object? sender, RoutedEventArgs e)
    {
        _manualOverrides.Clear();
        if (!string.IsNullOrWhiteSpace(_currentSettings.SourceFolderPath))
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

    /// <summary>
    /// Returns the current Mermaid diagram text, mode-aware.
    /// In Design Mode: exports from the DesignGraph.
    /// In Analyze Mode: exports from the focus/navigation graph or current scanned graph.
    /// </summary>
    private string? GetCurrentMermaid()
    {
        if (_designModeController.CurrentMode == AppMode.Design && _designGraph != null)
            return DesignExporter.ToMermaid(_designGraph);

        TypeGraph? graph = _focusNavigationController.CurrentGraph ?? _currentGraph;
        if (graph == null) return null;
        return MermaidGraphExporter.BuildDiagram(graph);
    }

    /// <summary>
    /// Returns the current graph title for file naming, mode-aware.
    /// </summary>
    private string GetCurrentGraphTitle()
    {
        if (_designModeController.CurrentMode == AppMode.Design && _designGraph != null)
            return _designGraph.Title;
        TypeGraph? graph = _focusNavigationController.CurrentGraph ?? _currentGraph;
        return graph?.Title ?? "diagram";
    }

    private void OnSaveMmd(object? sender, RoutedEventArgs e)
    {
        string? mermaid = GetCurrentMermaid();
        if (mermaid == null) return;

        var dir = Path.Combine(AppContext.BaseDirectory, "export");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"{MakeSafeFileName(GetCurrentGraphTitle())}.mmd");
        File.WriteAllText(path, mermaid);
        StatsText.Text = $"Saved: {path}";
    }

    private void OnSaveMd(object? sender, RoutedEventArgs e)
    {
        string? mermaid = GetCurrentMermaid();
        if (mermaid == null) return;

        var dir = Path.Combine(AppContext.BaseDirectory, "export");
        Directory.CreateDirectory(dir);
        var title = GetCurrentGraphTitle();
        var path = Path.Combine(dir, $"{MakeSafeFileName(title)}.md");
        File.WriteAllText(path, "# " + title + "\n\n```mermaid\n" + mermaid + "\n```\n");
        StatsText.Text = $"Saved: {path}";
    }

    private async void OnCopyMermaid(object? sender, RoutedEventArgs e)
    {
        string? mermaid = GetCurrentMermaid();
        if (mermaid == null) return;

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(mermaid);
            StatsText.Text = "Mermaid copied to clipboard";
        }
    }

    private void OnOpenLiveEditor(object? sender, RoutedEventArgs e)
    {
        string? mermaid = GetCurrentMermaid();
        if (mermaid == null) return;

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
            GraphCanvasView.SetEdgeStyles(_currentSettings.EdgeStyles);
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

        var ctrl = (e.KeyModifiers & KeyModifiers.Control) != 0;
        var shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

        // Ctrl+R — Redraw edges (works in both Analyze and Design modes)
        if (ctrl && e.Key == Key.R)
        {
            if (_designModeController.CurrentMode == AppMode.Design && _designGraph != null)
            {
                RenderDesignModeGraph(preserveViewport: true);
            }
            else
            {
                RedrawEdgesNow();
            }
            e.Handled = true;
            return;
        }

        // Only handle Design Mode shortcuts when in Design Mode
        if (_designModeController.CurrentMode != AppMode.Design)
            return;

        // Ignore shortcuts during inline text editing (so typing isn't intercepted)
        if (e.Source is TextBox)
            return;

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

        // Escape — cancel action, clear selection, or reset tool
        if (e.Key == Key.Escape)
        {
            if (_designCanvasController.IsCreatingEdge)
            {
                _designCanvasController.CancelEdgeCreation();
                GraphCanvasView.ForceRedraw();
                UpdateStatusBar();
                e.Handled = true;
                return;
            }
            // If a non-Select tool is armed, reset to Select
            if (_designCanvasController.CurrentTool != DesignTool.Select)
            {
                _designCanvasController.ResetTool();
                UpdateStatusBar();
                e.Handled = true;
                return;
            }
            // Otherwise deselect
            if (_designGraph != null)
            {
                // Clear selection by clicking empty canvas
                _designCanvasController.HandlePointerPressed(
                    new SKPoint(-1000, -1000), _designGraph,
                    new System.Collections.Generic.List<SKPoint>());
                RenderDesignModeGraph();
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

        // Ctrl+Shift+S — Save As
        if (ctrl && shift && e.Key == Key.S)
        {
            OnDesignSaveAs(this, new RoutedEventArgs());
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

        // F — Fit to screen (when nothing selected; reserved when "F" is used as a shortcut)
        if (e.Key == Key.F && !ctrl && !shift)
        {
            GraphCanvasView.FitToScreen();
            e.Handled = true;
            return;
        }

        // ── Tool shortcuts (UIContract §7) ──
        if (!ctrl)
        {
            var bindings = _currentSettings?.DesignShortcutBindings;
            DesignTool? tool = DesignShortcutDefaults.KeyToTool(e.Key, bindings);

            if (tool.HasValue)
            {
                ArmToolWithKeyboard(tool.Value, shift);
                e.Handled = true;
                return;
            }
        }

        // Arrow keys — nudge selected classes
        if (_designGraph != null && _designCanvasController.Selection.SelectedClassIds.Count > 0)
        {
            float nudgeAmount = shift ? 10f : 1f;
            float dx = 0, dy = 0;
            switch (e.Key)
            {
                case Key.Left: dx = -nudgeAmount; break;
                case Key.Right: dx = nudgeAmount; break;
                case Key.Up: dy = -nudgeAmount; break;
                case Key.Down: dy = nudgeAmount; break;
            }
            if (dx != 0 || dy != 0)
            {
                foreach (var id in _designCanvasController.Selection.SelectedClassIds)
                {
                    var cls = _designGraph.Classes.FirstOrDefault(c => c.Id == id);
                    if (cls != null)
                    {
                        cls.X += dx;
                        cls.Y += dy;
                    }
                }
                RenderDesignModeGraph();
                e.Handled = true;
                return;
            }
        }
    }

    /// <summary>
    /// Arms a tool from a keyboard shortcut. If Shift is held, arms in sticky mode.
    /// For edge tools, if a class is selected, sets it as the source (Method C, UIContract §6).
    /// </summary>
    private void ArmToolWithKeyboard(DesignTool tool, bool shift)
    {
        if (DesignCanvasController.IsEdgeTool(tool) && _designGraph != null)
        {
            var selectedIds = _designCanvasController.Selection.SelectedClassIds;
            if (selectedIds.Count == 1)
            {
                _designCanvasController.BeginEdgeCreationWithSource(_designGraph, selectedIds[0], tool, shift);
                UpdateStatusBar();
                return;
            }
        }
        _designCanvasController.ArmTool(tool, shift);
        UpdateStatusBar();
    }

    // ── Inspector panel (docs/design/10) ──

    /// <summary>
    /// Rebuilds the inspector view model whenever the Design Mode selection
    /// changes. Switches between the four states (Nothing/SingleClass/
    /// MultiSelect) and populates the fields/lists.
    /// </summary>
    private void OnDesignSelectionChanged(object? sender, DesignSelection selection)
    {
        if (_designGraph == null) return;

        // Sync selection to GraphCanvas for rendering (UIContract §5, Bug 2 fix)
        var selectedIds = new HashSet<string>(selection.SelectedClassIds);
        GraphCanvasView.SetDesignSelection(selectedIds);

        // (Re)create the view model so it picks up the current state
        _inspectorVm = new DesignInspectorViewModel(_designCanvasController, _designGraph);

        // Switch the visible state
        InspectorNothingState.IsVisible = selection.SelectedClassIds.Count == 0;
        InspectorSingleClassState.IsVisible = selection.SelectedClassIds.Count == 1;
        InspectorMultiSelectState.IsVisible = selection.SelectedClassIds.Count > 1;

        if (selection.SelectedClassIds.Count == 0)
        {
            // Nothing state — show summary
            InspectorClassCountText.Text = $"Classes: {_designGraph.Classes.Count}";
            InspectorEdgeCountText.Text = $"Edges: {_designGraph.Edges.Count}";
            if (_inspectorVm.UnnamedCount > 0)
            {
                InspectorUnnamedText.Text = $"⚠ {_inspectorVm.UnnamedCount} unnamed class(es)";
                InspectorUnnamedText.IsVisible = true;
            }
            else
            {
                InspectorUnnamedText.IsVisible = false;
            }
        }
        else if (selection.SelectedClassIds.Count == 1)
        {
            // Single class state — populate fields
            var cls = _inspectorVm.SelectedClass;
            if (cls != null)
            {
                InspectorClassNameLabel.Text = cls.Name;
                InspectorNameBox.Text = cls.Name;
                InspectorNamespaceBox.Text = cls.Namespace ?? "";
                InspectorKindCombo.SelectedIndex = (int)cls.Kind;

                // Wire commit handlers (one-shot — detach first to avoid duplicate handlers)
                InspectorNameBox.LostFocus -= OnInspectorNameLostFocus;
                InspectorNamespaceBox.LostFocus -= OnInspectorNamespaceLostFocus;
                InspectorKindCombo.SelectionChanged -= OnInspectorKindChanged;
                InspectorNameBox.LostFocus += OnInspectorNameLostFocus;
                InspectorNamespaceBox.LostFocus += OnInspectorNamespaceLostFocus;
                InspectorKindCombo.SelectionChanged += OnInspectorKindChanged;
            }

            // Populate member list — editable rows with class ID for commit-back
            InspectorMembersList.ItemsSource = _inspectorVm.SelectedClass?.Members
                .Select((m, i) => new MemberRow(_inspectorVm.SelectedClass.Id, m, i))
                .ToList();

            // Populate relations lists
            InspectorOutgoingList.ItemsSource = _inspectorVm.OutgoingEdges
                .Select(e => new EdgeRow(e.Id, e.Kind.ToString(),
                    $"{_inspectorVm.GetOtherClassName(e)}  ({e.Kind})"))
                .ToList();
            InspectorIncomingList.ItemsSource = _inspectorVm.IncomingEdges
                .Select(e => new EdgeRow(e.Id, e.Kind.ToString(),
                    $"{_inspectorVm.GetOtherClassName(e)}  ({e.Kind})"))
                .ToList();
        }
        else
        {
            // Multi-select state
            InspectorMultiSelectCountText.Text = $"{selection.SelectedClassIds.Count} classes selected";
            InspectorMultiSelectList.ItemsSource = _inspectorVm.SelectedClasses;
        }

        UpdateStatusBar();
    }


    private void OnInspectorNameLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        var newName = InspectorNameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(newName) || newName == _inspectorVm.SelectedClass.Name) return;
        var cmd = new DesignCommands.RenameClass(
            _inspectorVm.SelectedClass.Id,
            _inspectorVm.SelectedClass.Name,
            newName);
        _designCanvasController.ExecuteCommand(cmd, _designGraph);
        RenderDesignModeGraph();
    }

    /// <summary>Inspector kind dropdown — commits via ChangeClassKind command (undoable).</summary>
    private void OnInspectorKindChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        var newKind = (ClassKind)InspectorKindCombo.SelectedIndex;
        if (newKind == _inspectorVm.SelectedClass.Kind) return;
        _designCanvasController.ChangeClassKind(_designGraph, _inspectorVm.SelectedClass.Id, newKind);
        RenderDesignModeGraph();
    }

    /// <summary>Inspector namespace field — commits via ChangeNamespace command (undoable).</summary>
    private void OnInspectorNamespaceLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        var newNamespace = InspectorNamespaceBox.Text ?? "";
        if (newNamespace == (_inspectorVm.SelectedClass.Namespace ?? "")) return;
        _designCanvasController.ChangeNamespace(_designGraph, _inspectorVm.SelectedClass.Id, newNamespace);
        RenderDesignModeGraph();
    }

    /// <summary>Inspector member delete button.</summary>
    private void OnInspectorDeleteMember(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        if (sender is not Avalonia.Controls.Button btn) return;
        if (btn.Tag is not MemberRow row) return;
        _designCanvasController.RemoveMember(_designGraph, _inspectorVm.SelectedClass.Id, row.Index);
        RenderDesignModeGraph();
    }

    /// <summary>Cycles member visibility on badge click.</summary>
    private void OnInspectorCycleVisibility(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        if (sender is not Avalonia.Controls.Button btn) return;
        if (btn.Tag is not MemberRow row) return;
        _designCanvasController.CycleMemberVisibility(_designGraph, row.ClassId, row.Index);
        RenderDesignModeGraph();
    }

    /// <summary>Commits member name change on focus loss.</summary>
    private void OnInspectorMemberNameChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        if (sender is not Avalonia.Controls.TextBox tb) return;
        if (tb.Tag is not MemberRow row) return;
        var newName = tb.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(newName)) return;
        _designCanvasController.RenameMember(_designGraph, row.ClassId, row.Index, newName);
        RenderDesignModeGraph();
    }

    /// <summary>Commits member type change on focus loss.</summary>
    private void OnInspectorMemberTypeChanged(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        if (sender is not Avalonia.Controls.TextBox tb) return;
        if (tb.Tag is not MemberRow row) return;
        var newType = tb.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(newType)) return;
        _designCanvasController.ChangeMemberType(_designGraph, row.ClassId, row.Index, newType);
        RenderDesignModeGraph();
    }

    /// <summary>Adds a field to the selected class via inspector button.</summary>
    private void OnInspectorAddField(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        _designCanvasController.AddMemberToSelectedClass(_designGraph, MemberKind.Field);
        RenderDesignModeGraph();
    }

    /// <summary>Adds a property to the selected class via inspector button.</summary>
    private void OnInspectorAddProperty(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        _designCanvasController.AddMemberToSelectedClass(_designGraph, MemberKind.Property);
        RenderDesignModeGraph();
    }

    /// <summary>Adds a method to the selected class via inspector button.</summary>
    private void OnInspectorAddMethod(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        _designCanvasController.AddMemberToSelectedClass(_designGraph, MemberKind.Method);
        RenderDesignModeGraph();
    }

    /// <summary>Adds a constructor to the selected class via inspector button.</summary>
    private void OnInspectorAddConstructor(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null) return;
        _designCanvasController.AddMemberToSelectedClass(_designGraph, MemberKind.Constructor);
        RenderDesignModeGraph();
    }

    /// <summary>Inspector edge kind dropdown — commits via ChangeEdgeType command (undoable).</summary>
    private void OnInspectorEdgeKindChanged(object? sender, Avalonia.Controls.SelectionChangedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        if (sender is not Avalonia.Controls.ComboBox combo) return;
        EdgeRow? edgeRow = combo.Tag as EdgeRow ?? combo.DataContext as EdgeRow;
        if (edgeRow == null) return;
        if (!Enum.TryParse<EdgeKind>(edgeRow.Kind, out var oldKind)) return;
        if (!Enum.TryParse<EdgeKind>(combo.SelectedValue?.ToString() ?? "", out var newKind)) return;
        if (oldKind == newKind) return;
        _designCanvasController.ChangeEdgeType(_designGraph, edgeRow.Id, newKind);
        RenderDesignModeGraph();
    }

    /// <summary>Inspector "→" button — jumps to the class on the other end of the edge.</summary>
    private void OnInspectorJumpToClass(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm?.SelectedClass == null) return;
        if (sender is not Avalonia.Controls.Button btn) return;
        var edgeRow = (btn.Tag as EdgeRow) ?? (btn.DataContext as EdgeRow);
        if (edgeRow == null) return;
        // Find the edge, get the other class ID
        var edge = _designGraph.Edges.FirstOrDefault(e => e.Id == edgeRow.Id);
        if (edge == null) return;
        var otherId = edge.FromClassId == _inspectorVm.SelectedClass.Id
            ? edge.ToClassId : edge.FromClassId;
        _designCanvasController.SelectById(otherId);
    }

    /// <summary>Multi-select delete all button.</summary>
    private void OnInspectorMultiDelete(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_designGraph == null || _inspectorVm == null) return;
        foreach (var cls in _inspectorVm.SelectedClasses.ToList())
            _designCanvasController.HandleDeleteKey(_designGraph);
        RenderDesignModeGraph();
    }
}
