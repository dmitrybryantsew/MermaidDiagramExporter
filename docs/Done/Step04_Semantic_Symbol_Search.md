# Step 04: Semantic Symbol Search

## Overview
Replace the current canvas-only name highlighting with a dedicated global symbol search panel. Supports namespace-qualified queries (`Namespace.Class.Member`), partial matches (`*.Health`), results with file path + line context, and a "Focus on Results" canvas mode.

## Dependencies
- **Step 01** — reads `CurrentSettings.SearchCaseSensitive`, `SearchIncludeMembers`, `AutoFocusSearchResults`
- **Step 03** — should be complete (not strictly required)

---

## Part A: Symbol Index

### 1. Create `src/MermaidDiagramExporter.Gui/Search/SymbolIndex.cs`

This is the inverted search index built during/after scan:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Search;

/// <summary>
/// Fast inverted index for symbol search. Built from TypeGraph after scan.
/// Supports namespace-qualified and partial queries.
/// </summary>
public sealed class SymbolIndex
{
    // nodeId -> node data for quick lookup
    public IReadOnlyDictionary<string, TypeNodeData> NodesById { get; private set; }
        = new Dictionary<string, TypeNodeData>();

    // "Player" -> ["T_MyGame_Player", "T_MyGame_PlayerController"]
    // "Health" -> ["T_MyGame_Player", "T_MyGame_Enemy"]
    public IReadOnlyDictionary<string, List<string>> NameIndex { get; private set; }
        = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    // "MyGame" -> ["T_MyGame_Player", ...] (all nodes in that namespace)
    public IReadOnlyDictionary<string, List<string>> NamespaceIndex { get; private set; }
        = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    // "Health" -> [("T_Player", "Property", "Health", 42), ...]
    public IReadOnlyDictionary<string, List<MemberIndexEntry>> MemberIndex { get; private set; }
        = new Dictionary<string, List<MemberIndexEntry>>(StringComparer.OrdinalIgnoreCase);

    // All unique namespace prefixes for autocomplete: "MyGame", "MyGame.AI", "MyGame.UI"
    public IReadOnlyList<string> NamespacePrefixes { get; private set; }
        = Array.Empty<string>();

    public static SymbolIndex Build(TypeGraph graph)
    {
        var nodesById = graph.Nodes.ToDictionary(n => n.Id);
        var nameIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var namespaceIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var memberIndex = new Dictionary<string, List<MemberIndexEntry>>(StringComparer.OrdinalIgnoreCase);
        var namespacePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            // Index node display name
            IndexToken(nameIndex, node.DisplayName, node.Id);
            // Also index the full name
            IndexToken(nameIndex, node.FullName, node.Id);

            // Index namespace and all its prefixes
            if (!string.IsNullOrEmpty(node.Namespace))
            {
                IndexToken(namespaceIndex, node.Namespace, node.Id);
                namespacePrefixes.Add(node.Namespace);

                // Add parent prefixes: "A.B.C" -> "A.B", "A"
                string ns = node.Namespace;
                while (true)
                {
                    int lastDot = ns.LastIndexOf('.');
                    if (lastDot < 0) break;
                    ns = ns.Substring(0, lastDot);
                    namespacePrefixes.Add(ns);
                }
            }

            // Index members
            foreach (var member in node.Members)
            {
                var entry = new MemberIndexEntry(node.Id, node.DisplayName, node.Namespace,
                    member.Kind.ToString(), member.Name, member.TypeName);

                IndexMember(memberIndex, member.Name, entry);
                IndexMember(memberIndex, member.TypeName, entry);
            }
        }

        return new SymbolIndex
        {
            NodesById = nodesById,
            NameIndex = nameIndex,
            NamespaceIndex = namespaceIndex,
            MemberIndex = memberIndex,
            NamespacePrefixes = namespacePrefixes.OrderBy(n => n).ToList()
        };
    }

    private static void IndexToken(Dictionary<string, List<string>> index, string token, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (!index.TryGetValue(token, out var list))
        {
            list = new List<string>();
            index[token] = list;
        }
        if (!list.Contains(nodeId))
            list.Add(nodeId);
    }

    private static void IndexMember(Dictionary<string, List<MemberIndexEntry>> index, string token, MemberIndexEntry entry)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (!index.TryGetValue(token, out var list))
        {
            list = new List<MemberIndexEntry>();
            index[token] = list;
        }
        list.Add(entry);
    }
}

public sealed class MemberIndexEntry
{
    public string NodeId { get; }
    public string NodeDisplayName { get; }
    public string NodeNamespace { get; }
    public string MemberKind { get; }
    public string MemberName { get; }
    public string MemberTypeName { get; }

    public MemberIndexEntry(string nodeId, string nodeDisplayName, string nodeNamespace,
        string memberKind, string memberName, string memberTypeName)
    {
        NodeId = nodeId;
        NodeDisplayName = nodeDisplayName;
        NodeNamespace = nodeNamespace;
        MemberKind = memberKind;
        MemberName = memberName;
        MemberTypeName = memberTypeName;
    }
}
```

---

## Part B: Query Parser

### 2. Create `src/MermaidDiagramExporter.Gui/Search/SearchQueryParser.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Search;

/// <summary>
/// Parses search query strings into structured criteria.
/// </summary>
public static class SearchQueryParser
{
    public static SearchCriteria Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchCriteria { RawQuery = query ?? "" };

        var criteria = new SearchCriteria { RawQuery = query.Trim() };

        string q = criteria.RawQuery;

        // Check for wildcard prefix: "*.Health" or "*Health"
        if (q.StartsWith("*."))
        {
            criteria.WildcardPrefix = true;
            q = q.Substring(2);
        }
        else if (q.StartsWith("*"))
        {
            criteria.WildcardPrefix = true;
            q = q.Substring(1);
        }

        // Check for namespace-qualified query: "Namespace.Class.Member"
        var parts = q.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            // Last part is the target name; earlier parts form namespace/type filters
            criteria.TargetName = parts[^1];
            criteria.NamespaceOrTypeHints = parts.Take(parts.Length - 1).ToList();
        }
        else
        {
            criteria.TargetName = q;
        }

        return criteria;
    }
}

public sealed class SearchCriteria
{
    public string RawQuery { get; set; } = "";
    public string TargetName { get; set; } = "";
    public IReadOnlyList<string> NamespaceOrTypeHints { get; set; } = Array.Empty<string>();
    public bool WildcardPrefix { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(TargetName) && NamespaceOrTypeHints.Count == 0;
}
```

---

## Part C: Search Engine

### 3. Create `src/MermaidDiagramExporter.Gui/Search/SymbolSearchEngine.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Search;

/// <summary>
/// Executes symbol searches against a SymbolIndex. Returns ranked results.
/// </summary>
public sealed class SymbolSearchEngine
{
    private SymbolIndex? _index;

    public void RebuildIndex(TypeGraph graph)
    {
        _index = SymbolIndex.Build(graph);
    }

    /// <summary>
    /// Searches for nodes matching the query. Returns node IDs in relevance order.
    /// </summary>
    public IReadOnlyList<SearchResult> Search(SearchCriteria criteria, ProjectSettings settings)
    {
        if (_index == null || criteria.IsEmpty)
            return Array.Empty<SearchResult>();

        var comparer = settings.SearchCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        string target = criteria.TargetName;

        // Collect matching node IDs with scores
        var scores = new Dictionary<string, int>();

        // 1. Name index match (exact or prefix)
        foreach (var kvp in _index.NameIndex)
        {
            bool nameMatches = settings.SearchCaseSensitive
                ? kvp.Key.Contains(target)
                : kvp.Key.Contains(target, StringComparison.OrdinalIgnoreCase);

            if (nameMatches)
            {
                int score = kvp.Key.Equals(target, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                    ? 100 : 50; // exact match scores higher
                foreach (var nodeId in kvp.Value)
                    AddScore(scores, nodeId, score);
            }
        }

        // 2. Namespace filter — if user typed "MyGame.Player", restrict to that namespace
        if (criteria.NamespaceOrTypeHints.Count > 0)
        {
            string nsHint = string.Join(".", criteria.NamespaceOrTypeHints);
            var nsMatchingIds = new HashSet<string>();
            foreach (var kvp in _index.NamespaceIndex)
            {
                if (kvp.Key.Contains(nsHint, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var id in kvp.Value)
                        nsMatchingIds.Add(id);
                }
            }
            // Intersection: only keep scores for nodes in matching namespaces
            var toRemove = scores.Keys.Where(id => !nsMatchingIds.Contains(id)).ToList();
            foreach (var id in toRemove)
                scores.Remove(id);

            // Boost namespace-exact matches
            foreach (var id in nsMatchingIds)
            {
                if (_index.NodesById.TryGetValue(id, out var node) &&
                    node.Namespace.Equals(nsHint, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                {
                    AddScore(scores, id, 30);
                }
            }
        }

        // 3. Member index search (if enabled)
        if (settings.SearchIncludeMembers)
        {
            foreach (var kvp in _index.MemberIndex)
            {
                bool memberMatches = settings.SearchCaseSensitive
                    ? kvp.Key.Contains(target)
                    : kvp.Key.Contains(target, StringComparison.OrdinalIgnoreCase);

                if (memberMatches)
                {
                    foreach (var entry in kvp.Value)
                    {
                        int score = kvp.Key.Equals(target, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                            ? 80 : 40;
                        AddScore(scores, entry.NodeId, score);
                    }
                }
            }
        }

        // 4. Build results from scored node IDs
        var results = new List<SearchResult>();
        foreach (var kvp in scores.OrderByDescending(kv => kv.Value))
        {
            if (_index.NodesById.TryGetValue(kvp.Key, out var node))
            {
                var matchingMembers = GetMatchingMembers(kvp.Key, target, settings);
                results.Add(new SearchResult
                {
                    NodeId = node.Id,
                    NodeDisplayName = node.DisplayName,
                    NodeNamespace = node.Namespace,
                    NodeKind = node.Kind.ToString(),
                    FilePath = node.AssetPath,
                    Score = kvp.Value,
                    MatchedMembers = matchingMembers
                });
            }
        }

        return results;
    }

    private IReadOnlyList<MatchedMember> GetMatchingMembers(string nodeId, string target, ProjectSettings settings)
    {
        if (_index == null || !_index.NodesById.TryGetValue(nodeId, out var node))
            return Array.Empty<MatchedMember>();

        var matched = new List<MatchedMember>();
        foreach (var member in node.Members)
        {
            bool nameMatch = settings.SearchCaseSensitive
                ? member.Name.Contains(target)
                : member.Name.Contains(target, StringComparison.OrdinalIgnoreCase);
            bool typeMatch = settings.SearchCaseSensitive
                ? member.TypeName.Contains(target)
                : member.TypeName.Contains(target, StringComparison.OrdinalIgnoreCase);

            if (nameMatch || typeMatch)
            {
                matched.Add(new MatchedMember(member.Name, member.TypeName, member.Kind.ToString()));
            }
        }
        return matched;
    }

    private static void AddScore(Dictionary<string, int> scores, string nodeId, int points)
    {
        if (!scores.TryGetValue(nodeId, out int current))
            current = 0;
        scores[nodeId] = current + points;
    }
}

public sealed class SearchResult
{
    public string NodeId { get; set; } = "";
    public string NodeDisplayName { get; set; } = "";
    public string NodeNamespace { get; set; } = "";
    public string NodeKind { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Score { get; set; }
    public IReadOnlyList<MatchedMember> MatchedMembers { get; set; } = Array.Empty<MatchedMember>();
    public bool IsExactNameMatch { get; set; }
}

public sealed class MatchedMember
{
    public string Name { get; }
    public string TypeName { get; }
    public string Kind { get; }
    public MatchedMember(string name, string typeName, string kind)
    {
        Name = name;
        TypeName = typeName;
        Kind = kind;
    }
}
```

---

## Part D: Search Panel UI

### 4. Create `src/MermaidDiagramExporter.Gui/Search/SearchPanel.axaml`

This is a user control that can be placed in a sidebar or flyout:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MermaidDiagramExporter.Gui.Search.SearchPanel"
             Width="320">
  <Border Background="#1E2329" BorderBrush="#3A4250" BorderThickness="1" Padding="8">
    <Grid RowDefinitions="Auto,Auto,*,Auto">
      <!-- Header -->
      <TextBlock Grid.Row="0" Text="Symbol Search" FontWeight="Bold" FontSize="14" Margin="0,0,0,8" />

      <!-- Query Box -->
      <Grid Grid.Row="1" ColumnDefinitions="*,Auto" Margin="0,0,0,8">
        <TextBox Grid.Column="0" x:Name="QueryTextBox" Watermark="Namespace.Class.Member" />
        <Button Grid.Column="1" x:Name="FocusResultsButton" Content="Focus" Margin="4,0,0,0"
                ToolTip.Tip="Filter canvas to show only results" />
      </Grid>

      <!-- Results -->
      <ScrollViewer Grid.Row="2" MaxHeight="500">
        <ListBox x:Name="ResultsList" Background="Transparent" BorderThickness="0">
          <ListBox.ItemTemplate>
            <DataTemplate>
              <Border Padding="4" Background="Transparent">
                <StackPanel Spacing="2">
                  <Grid ColumnDefinitions="*,Auto">
                    <TextBlock Grid.Column="0" Text="{Binding DisplayLabel}" FontWeight="Bold"
                               TextTrimming="CharacterEllipsis" />
                    <TextBlock Grid.Column="1" Text="{Binding NodeKind}" FontSize="10"
                               Opacity="0.5" Margin="4,0,0,0" />
                  </Grid>
                  <TextBlock Text="{Binding NodeNamespace}" FontSize="10" Opacity="0.5"
                             TextTrimming="CharacterEllipsis" />
                  <TextBlock Text="{Binding FileName}" FontSize="9" Opacity="0.4"
                             TextTrimming="CharacterEllipsis" />
                  <!-- Matched members -->
                  <ItemsControl ItemsSource="{Binding MatchedMembers}" Margin="8,2,0,0">
                    <ItemsControl.ItemTemplate>
                      <DataTemplate>
                        <TextBlock FontSize="10" Opacity="0.6">
                          <TextBlock.Text>
                            <MultiBinding StringFormat="  + {0} : {1} ({2})">
                              <Binding Path="Name" />
                              <Binding Path="TypeName" />
                              <Binding Path="Kind" />
                            </MultiBinding>
                          </TextBlock.Text>
                        </TextBlock>
                      </DataTemplate>
                    </ItemsControl.ItemTemplate>
                  </ItemsControl>
                </StackPanel>
              </Border>
            </DataTemplate>
          </ListBox.ItemTemplate>
        </ListBox>
      </ScrollViewer>

      <!-- Status -->
      <TextBlock Grid.Row="3" x:Name="StatusText" FontSize="10" Opacity="0.5" Margin="0,4,0,0" />
    </Grid>
  </Border>
</UserControl>
```

### 5. Create `src/MermaidDiagramExporter.Gui/Search/SearchPanel.axaml.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Threading;
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
    public void SetGraph(MermaidDiagramExporter.Core.TypeGraph graph, ProjectSettings settings)
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

// ViewModels for the ListBox binding
public class SearchResultViewModel
{
    public string NodeId { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public string NodeKind { get; set; } = "";
    public string NodeNamespace { get; set; } = "";
    public string FileName { get; set; } = "";
    public List<MatchedMemberViewModel> MatchedMembers { get; set; } = new();
}

public class MatchedMemberViewModel
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Kind { get; set; } = "";
}
```

---

## Part E: Integrate into MainWindow

### 6. Add SearchPanel to the main layout

In `MainWindow.axaml` (or wherever the main UI is defined), add the `SearchPanel` to the sidebar area alongside the existing class list. If the layout uses a `Grid` with columns, add:

```xml
<!-- In the sidebar column, after or replacing the simple search box -->
<local:SearchPanel x:Name="SymbolSearchPanel" />
```

You will need to add the namespace:
```xml
xmlns:search="clr-namespace:MermaidDiagramExporter.Gui.Search"
```

### 7. Wire events in `MainWindow.axaml.cs`

**Add field:**
```csharp
private readonly SymbolSearchEngine _searchEngine = new();
```

**In the constructor or initialization**, wire the panel events:
```csharp
SymbolSearchPanel.NodeSelected += OnSearchNodeSelected;
SymbolSearchPanel.FocusOnResultsRequested += OnFocusSearchResults;
SymbolSearchPanel.SearchCleared += OnSearchCleared;
```

**Add handlers:**
```csharp
private void OnSearchNodeSelected(string nodeId)
{
    // Pan/zoom canvas to the selected node
    if (_nodeMap.TryGetValue(nodeId, out var node))
    {
        GraphCanvasView.CenterOnNode(node);
        // Also select it in the inspector
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
    // Filter canvas to only show result nodes + their connecting edges
    var visibleSet = new HashSet<string>(nodeIds);
    var filteredNodes = _allNodes.Where(n => visibleSet.Contains(n.Id)).ToList();
    var filteredEdges = _allEdges.Where(e =>
        visibleSet.Contains(e.FromNode?.Id ?? "") && visibleSet.Contains(e.ToNode?.Id ?? ""))
        .ToList();
    GraphCanvasView.SetGraph(filteredNodes, filteredEdges);

    // Restore full graph on "Reset" click
    // (hook into existing Reset button or add a "Clear Search Filter" button)
}

private void OnSearchCleared()
{
    // If we had filtered the canvas, restore the full graph
    if (_currentGraph != null)
    {
        SetDisplayedGraph(_currentGraph);
    }
}
```

**Modify `SetDisplayedGraph()`** to update the search panel:
```csharp
private void SetDisplayedGraph(TypeGraph? graph, string selectedNodeId = "")
{
    // ... existing code ...
    if (graph != null)
    {
        SymbolSearchPanel.SetGraph(graph, _currentSettings);
        _searchEngine.RebuildIndex(graph); // also keep the local engine in sync
    }
    else
    {
        SymbolSearchPanel.Clear();
    }
}
```

### 8. Add `CenterOnNode` to `GraphCanvas.cs`

The canvas needs a method to pan/zoom to a specific node:

```csharp
/// <summary>
/// Pans the canvas so the given node is centered in the viewport.
/// </summary>
public void CenterOnNode(GraphNode node)
{
    float nodeCenterX = node.X + node.Width / 2;
    float nodeCenterY = node.Y + node.Height / 2;
    float viewW = (float)Bounds.Width;
    float viewH = (float)Bounds.Height;

    _panX = viewW / 2 - nodeCenterX * _zoom;
    _panY = viewH / 2 - nodeCenterY * _zoom;
    Invalidate();
}
```

---

## Testing Checklist

1. Scan a project, type a class name in the search box — results should appear with namespace and file.
2. Type `Namespace.ClassName` (e.g., `MyGame.Player`) — should filter to that namespace.
3. Type `*.Health` — should find all types with a member named `Health`.
4. Click a result — canvas should pan to center the node.
5. Click "Focus" — canvas should show only result nodes and their edges.
6. Clear the search box — full graph should restore.
7. Toggle "Include members" off in Settings — member matches should no longer appear in results.
8. Type a query quickly — search should debounce (not fire on every keystroke, but after 150ms pause).
