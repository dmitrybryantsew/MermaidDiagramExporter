# Step 07: Namespace Dependency Matrix

## Overview
An alternative view to the graph: a grid/matrix showing which namespaces reference which other namespaces. Useful for identifying circular dependencies at a glance. Accessed via a toggle button ("Matrix View" / "Graph View").

## Dependencies
- None of the prior steps are strictly required, but Step 01+02 provide the project context

---

## Part A: Matrix Data Model

### 1. Create `src/MermaidDiagramExporter.Gui/Matrix/NamespaceMatrix.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Matrix;

/// <summary>
/// Represents namespace-to-namespace dependency counts.
/// </summary>
public sealed class NamespaceMatrix
{
    /// <summary>
    /// Ordered list of namespace labels (row/column headers).
    /// </summary>
    public IReadOnlyList<string> Namespaces { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Cell data: (fromNamespaceIndex, toNamespaceIndex) -> count.
    /// </summary>
    public IReadOnlyDictionary<(int From, int To), int> Cells { get; set; }
        = new Dictionary<(int, int), int>();

    /// <summary>
    /// Whether there is a dependency from row namespace to column namespace.
    /// </summary>
    public bool HasDependency(int fromIndex, int toIndex) =>
        Cells.ContainsKey((fromIndex, toIndex)) && Cells[(fromIndex, toIndex)] > 0;

    /// <summary>
    /// Total number of dependency edges for a cell.
    /// </summary>
    public int GetCount(int fromIndex, int toIndex) =>
        Cells.TryGetValue((fromIndex, toIndex), out int count) ? count : 0;

    /// <summary>
    /// Detects circular dependency chains of length 2 (A->B and B->A).
    /// Returns list of (indexA, indexB) pairs.
    /// </summary>
    public IReadOnlyList<(int, int)> FindTwoWayDependencies()
    {
        var results = new List<(int, int)>();
        int n = Namespaces.Count;
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                if (HasDependency(i, j) && HasDependency(j, i))
                    results.Add((i, j));
            }
        }
        return results;
    }
}
```

---

## Part B: Matrix Builder

### 2. Create `src/MermaidDiagramExporter.Gui/Matrix/NamespaceMatrixBuilder.cs`

```csharp
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Matrix;

/// <summary>
/// Builds a NamespaceMatrix from a TypeGraph by analyzing cross-namespace edges.
/// </summary>
public static class NamespaceMatrixBuilder
{
    public static NamespaceMatrix Build(TypeGraph graph)
    {
        // Collect all unique namespaces in order
        var namespaces = graph.Nodes
            .Select(n => n.Namespace)
            .Distinct()
            .OrderBy(ns => ns)
            .ToList();

        var nsToIndex = namespaces.Select((ns, i) => (ns, i)).ToDictionary(t => t.ns, t => t.i);
        var cells = new Dictionary<(int, int), int>();

        // Count edges between namespaces
        foreach (var edge in graph.Edges)
        {
            var fromNode = graph.Nodes.FirstOrDefault(n => n.Id == edge.FromNodeId);
            var toNode = graph.Nodes.FirstOrDefault(n => n.Id == edge.ToNodeId);
            if (fromNode == null || toNode == null) continue;

            if (!nsToIndex.TryGetValue(fromNode.Namespace, out int fromIdx)) continue;
            if (!nsToIndex.TryGetValue(toNode.Namespace, out int toIdx)) continue;
            if (fromIdx == toIdx) continue; // skip intra-namespace edges

            var key = (fromIdx, toIdx);
            if (!cells.TryGetValue(key, out int count))
                count = 0;
            cells[key] = count + 1;
        }

        return new NamespaceMatrix
        {
            Namespaces = namespaces,
            Cells = cells
        };
    }
}
```

---

## Part C: Matrix View UI

### 3. Create `src/MermaidDiagramExporter.Gui/Matrix/NamespaceMatrixView.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MermaidDiagramExporter.Gui.Matrix.NamespaceMatrixView">
  <Border Background="#1A1E24" Padding="12">
    <Grid RowDefinitions="Auto,*">
      <!-- Header -->
      <Grid Grid.Row="0" ColumnDefinitions="Auto,*" Margin="0,0,0,8">
        <TextBlock Grid.Column="0" Text="Namespace Dependency Matrix" FontWeight="Bold" FontSize="14" />
        <TextBlock Grid.Column="1" x:Name="MatrixSummaryText" HorizontalAlignment="Right"
                   Opacity="0.6" FontSize="11" />
      </Grid>

      <!-- Scrollable grid -->
      <ScrollViewer Grid.Row="1" HorizontalScrollBarVisibility="Auto" VerticalScrollBarVisibility="Auto">
        <Grid x:Name="MatrixGrid" Background="Transparent">
          <!-- Populated in code-behind -->
        </Grid>
      </ScrollViewer>
    </Grid>
  </Border>
</UserControl>
```

### 4. Create `src/MermaidDiagramExporter.Gui/Matrix/NamespaceMatrixView.axaml.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace MermaidDiagramExporter.Gui.Matrix;

public partial class NamespaceMatrixView : UserControl
{
    private NamespaceMatrix? _matrix;

    public NamespaceMatrixView()
    {
        InitializeComponent();
    }

    public void SetMatrix(NamespaceMatrix matrix)
    {
        _matrix = matrix;
        BuildGrid();
        UpdateSummary();
    }

    private void BuildGrid()
    {
        if (_matrix == null) return;

        // Clear existing
        MatrixGrid.Children.Clear();
        MatrixGrid.RowDefinitions.Clear();
        MatrixGrid.ColumnDefinitions.Clear();

        int n = _matrix.Namespaces.Count;
        if (n == 0) return;

        // Column definitions: label column + n data columns
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        for (int i = 0; i < n; i++)
            MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

        // Row definitions: header row + n data rows
        MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (int i = 0; i < n; i++)
            MatrixGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(36) });

        // Header row (column labels)
        for (int col = 0; col < n; col++)
        {
            var header = CreateHeaderTextBlock(_matrix.Namespaces[col], true);
            Grid.SetRow(header, 0);
            Grid.SetColumn(header, col + 1);
            MatrixGrid.Children.Add(header);
        }

        // Diagonal label "TO ->" hint
        var toHint = new TextBlock
        {
            Text = "->",
            FontSize = 9,
            Opacity = 0.4,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        Grid.SetRow(toHint, 0);
        Grid.SetColumn(toHint, 0);
        MatrixGrid.Children.Add(toHint);

        // Two-way dependency highlights
        var twoWay = _matrix.FindTwoWayDependencies().ToHashSet();

        // Data rows
        for (int row = 0; row < n; row++)
        {
            // Row label
            var rowLabel = CreateHeaderTextBlock(_matrix.Namespaces[row], false);
            Grid.SetRow(rowLabel, row + 1);
            Grid.SetColumn(rowLabel, 0);
            MatrixGrid.Children.Add(rowLabel);

            for (int col = 0; col < n; col++)
            {
                var cell = new Border
                {
                    BorderThickness = new Thickness(0.5),
                    BorderBrush = new SolidColorBrush(Color.Parse("#2A2F36")),
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch
                };

                if (row == col)
                {
                    // Diagonal — internal dependencies
                    cell.Background = new SolidColorBrush(Color.Parse("#1A1E24"));
                    var txt = new TextBlock
                    {
                        Text = "-",
                        FontSize = 10,
                        Opacity = 0.3,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    cell.Child = txt;
                }
                else
                {
                    int count = _matrix.GetCount(row, col);
                    if (count > 0)
                    {
                        // Color intensity based on count
                        byte intensity = (byte)Math.Min(255, 60 + count * 25);
                        bool isCircular = twoWay.Contains((Math.Min(row, col), Math.Max(row, col)));
                        string colorHex = isCircular ? $"#FF{intensity:X2}40" : $"#40{intensity:X2}80";
                        cell.Background = new SolidColorBrush(Color.Parse(colorHex));
                        cell.BorderBrush = isCircular
                            ? new SolidColorBrush(Color.Parse("#FFE040"))
                            : new SolidColorBrush(Color.Parse("#2A2F36"));
                        cell.BorderThickness = isCircular ? new Thickness(1.5) : new Thickness(0.5);

                        cell.Child = new TextBlock
                        {
                            Text = count.ToString(),
                            FontSize = 10,
                            Foreground = Brushes.White,
                            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                        };

                        // Tooltip showing details
                        ToolTip.SetTip(cell, $"{_matrix.Namespaces[row]} -> {_matrix.Namespaces[col]}\nDependencies: {count}");
                    }
                    else
                    {
                        cell.Background = new SolidColorBrush(Color.Parse("#1E2329"));
                    }

                    // Click handler
                    int capturedRow = row;
                    int capturedCol = col;
                    cell.PointerPressed += (s, e) =>
                    {
                        CellClicked?.Invoke(_matrix.Namespaces[capturedRow], _matrix.Namespaces[capturedCol]);
                    };
                }

                Grid.SetRow(cell, row + 1);
                Grid.SetColumn(cell, col + 1);
                MatrixGrid.Children.Add(cell);
            }
        }
    }

    private static TextBlock CreateHeaderTextBlock(string text, bool rotate)
    {
        // Truncate long namespace names
        string display = text;
        if (display.Length > 18)
            display = display.Substring(0, 15) + "...";

        var tb = new TextBlock
        {
            Text = display,
            FontSize = 9,
            Opacity = 0.7,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            ToolTip = text // full text on hover
        };

        if (rotate)
        {
            tb.LayoutTransform = new RotateTransform { Angle = -45 };
            tb.Margin = new Thickness(0, 8, 0, 0);
        }

        return tb;
    }

    private void UpdateSummary()
    {
        if (_matrix == null) return;
        int n = _matrix.Namespaces.Count;
        int circular = _matrix.FindTwoWayDependencies().Count;
        int totalDeps = _matrix.Cells.Values.Sum();
        MatrixSummaryText.Text = $"{n} namespaces | {totalDeps} cross-namespace dependencies | {circular} circular pairs";
    }

    /// <summary>
    /// Raised when a cell is clicked. Provides the from and to namespace names.
    /// </summary>
    public event Action<string, string>? CellClicked;
}
```

---

## Part D: Toggle Between Graph and Matrix View

### 5. Add View Toggle in `MainWindow.axaml`

Add a toggle button in the toolbar:

```xml
<ToggleButton x:Name="MatrixViewToggle" Content="Matrix View" />
```

Place the `NamespaceMatrixView` in the same container as the canvas, with `IsVisible="False"` by default:

```xml
<Panel>
  <local:GraphCanvas x:Name="GraphCanvasView" />
  <local:Matrix.NamespaceMatrixView x:Name="MatrixView"
                                    IsVisible="False" />
  <local:MinimapControl x:Name="MinimapView" ... />
</Panel>
```

### 6. Wire Toggle in `MainWindow.axaml.cs`

```csharp
// In constructor or initialization:
MatrixViewToggle.Click += OnToggleMatrixView;
MatrixView.CellClicked += OnMatrixCellClicked;

private void OnToggleMatrixView(object? sender, RoutedEventArgs e)
{
    bool showMatrix = MatrixViewToggle.IsChecked == true;
    GraphCanvasView.IsVisible = !showMatrix;
    MinimapView.IsVisible = !showMatrix && _currentSettings.ShowMinimap;
    MatrixView.IsVisible = showMatrix;

    if (showMatrix && _currentGraph != null)
    {
        var matrix = NamespaceMatrixBuilder.Build(_currentGraph);
        MatrixView.SetMatrix(matrix);
    }
}

private void OnMatrixCellClicked(string fromNamespace, string toNamespace)
{
    // Switch back to graph view and focus on nodes in these namespaces
    MatrixViewToggle.IsChecked = false;
    GraphCanvasView.IsVisible = true;
    MinimapView.IsVisible = _currentSettings.ShowMinimap;
    MatrixView.IsVisible = false;

    // Highlight nodes in both namespaces
    var targetNs = new HashSet<string> { fromNamespace, toNamespace };
    var targetNodeIds = _allNodes
        .Where(n => targetNs.Contains(n.Namespace))
        .Select(n => n.Id)
        .ToList();

    // Focus the canvas on these nodes (reuse search focus logic)
    if (targetNodeIds.Count > 0)
    {
        var filteredNodes = _allNodes.Where(n => targetNs.Contains(n.Namespace)).ToList();
        var filteredEdges = _allEdges.Where(e =>
            targetNs.Contains(e.FromNode?.Namespace ?? "") &&
            targetNs.Contains(e.ToNode?.Namespace ?? "")).ToList();
        GraphCanvasView.SetGraph(filteredNodes, filteredEdges);
    }
}
```

**Also update `SetDisplayedGraph()`** to rebuild the matrix when the graph changes:
```csharp
private void SetDisplayedGraph(TypeGraph? graph, string selectedNodeId = "")
{
    // ... existing code ...

    // If matrix view is active, refresh it
    if (MatrixView.IsVisible && graph != null)
    {
        var matrix = NamespaceMatrixBuilder.Build(graph);
        MatrixView.SetMatrix(matrix);
    }
}
```

---

## Testing Checklist

1. Scan a project with multiple namespaces. Click "Matrix View" — a grid should appear.
2. Cells with cross-namespace dependencies should show a count number and be colored (blue gradient).
3. Namespaces with circular dependencies (A->B and B->A) should highlight cells in orange/red with a yellow border.
4. The summary line should show "N namespaces | X dependencies | Y circular pairs".
5. Click a cell — should switch back to graph view filtered to show only those two namespaces.
6. Switch back to Matrix View after focusing — should still show the full matrix (not filtered).
7. Verify the matrix handles edge cases: 1 namespace (mostly empty), 0 namespaces (empty state), many namespaces (scrollable).
