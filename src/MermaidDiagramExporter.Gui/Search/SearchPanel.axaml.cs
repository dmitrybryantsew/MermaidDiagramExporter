using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Search;

public partial class SearchPanel : UserControl
{
    private readonly SymbolSearchEngine _engine = new();
    private ProjectSettings _settings = new();
    private string _lastQuery = "";
    private DispatcherTimer? _debounceTimer;

    public SearchPanel()
    {
        InitializeComponent();

        QueryTextBox.TextChanged += (s, e) =>
        {
            _debounceTimer?.Stop();
            _debounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _debounceTimer.Tick += (_, _) =>
            {
                _debounceTimer.Stop();
                ExecuteSearch();
            };
            _debounceTimer.Start();
        };

        QueryTextBox.KeyDown += (s, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter)
            {
                _debounceTimer?.Stop();
                ExecuteSearch();
            }
        };

        FocusResultsButton.Click += (s, e) =>
        {
            if (ResultsList.SelectedItem is SearchResultViewModel vm)
            {
                NodeSelected?.Invoke(vm.NodeId);
            }
            FocusOnResultsRequested?.Invoke(GetCurrentResultNodeIds());
        };

        ResultsList.SelectionChanged += (s, e) =>
        {
            if (ResultsList.SelectedItem is SearchResultViewModel vm)
            {
                NodeSelected?.Invoke(vm.NodeId);
            }
        };
    }

    /// <summary>
    /// Raised when the user selects a result node. MainWindow should pan/zoom to it.
    /// </summary>
    public event Action<string>? NodeSelected;

    /// <summary>
    /// Raised when "Focus" button clicked. MainWindow should filter canvas to show only these nodes.
    /// </summary>
    public event Action<IReadOnlyList<string>>? FocusOnResultsRequested;

    /// <summary>
    /// Call after a new TypeGraph is scanned to rebuild the index.
    /// </summary>
    public void SetGraph(TypeGraph graph, ProjectSettings settings)
    {
        _settings = settings;
        _engine.RebuildIndex(graph);
        StatusText.Text = $"Indexed {graph.Nodes.Count} types";
        // Re-run search if query exists
        if (!string.IsNullOrWhiteSpace(QueryTextBox.Text))
            ExecuteSearch();
    }

    /// <summary>
    /// Clears the search panel (e.g., when graph is reset).
    /// </summary>
    public void Clear()
    {
        ResultsList.ItemsSource = null;
        StatusText.Text = "";
    }

    private void ExecuteSearch()
    {
        string query = QueryTextBox.Text ?? "";
        _lastQuery = query;

        if (string.IsNullOrWhiteSpace(query))
        {
            ResultsList.ItemsSource = null;
            StatusText.Text = "";
            SearchCleared?.Invoke();
            return;
        }

        var criteria = SearchQueryParser.Parse(query);
        var results = _engine.Search(criteria, _settings);

        var viewModels = results.Select(r => new SearchResultViewModel
        {
            NodeId = r.NodeId,
            DisplayLabel = $"{r.NodeNamespace}.{r.NodeDisplayName}",
            NodeKind = r.NodeKind,
            NodeNamespace = r.NodeNamespace,
            FileName = string.IsNullOrEmpty(r.FilePath) ? "" : Path.GetFileName(r.FilePath),
            MatchedMembers = r.MatchedMembers.Select(m => new MatchedMemberViewModel
            {
                Name = m.Name,
                TypeName = m.TypeName,
                Kind = m.Kind
            }).ToList()
        }).ToList();

        ResultsList.ItemsSource = viewModels;
        StatusText.Text = $"{results.Count} result(s)";
    }

    private IReadOnlyList<string> GetCurrentResultNodeIds()
    {
        if (ResultsList.ItemsSource is IEnumerable<SearchResultViewModel> vms)
            return vms.Select(vm => vm.NodeId).ToList();
        return Array.Empty<string>();
    }

    public event Action? SearchCleared;
}
