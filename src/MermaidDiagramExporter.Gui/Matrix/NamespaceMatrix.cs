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
