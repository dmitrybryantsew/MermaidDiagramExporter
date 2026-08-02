using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Determines which classes move together when the user drags a selected
/// class. Per the marquee + move-scope feature (UIContract §5 + §11).
/// </summary>
public enum MoveScope
{
    /// <summary>
    /// Only the classes currently in the selection move.
    /// </summary>
    SelectedOnly = 0,

    /// <summary>
    /// The selection plus their directly edge-connected neighbors (1-depth,
    /// undirected) move together. Lets users keep related clusters aligned.
    /// </summary>
    SelectedPlusRelated1D = 1
}

/// <summary>
/// Shared helpers for computing the set of node IDs affected by a move
/// operation, based on the current <see cref="MoveScope"/> and the graph's
/// edges. Used by both Analyze Mode (GraphEdge list) and Design Mode
/// (DesignEdge list) via the overloads below.
/// </summary>
public static class MoveScopeResolver
{
    /// <summary>
    /// Computes the full set of node IDs that should move together, given the
    /// primary dragged node, the current selection, the move scope, and the
    /// list of edges (each as a pair of endpoint IDs).
    /// </summary>
    /// <param name="draggedId">The class/node the user grabbed.</param>
    /// <param name="selectedIds">The current selection set (may be empty).</param>
    /// <param name="scope">The active move scope.</param>
    /// <param name="edges">All edges as (fromId, toId) pairs.</param>
    /// <returns>The set of IDs to move.</returns>
    public static HashSet<string> ResolveMoveSet(
        string draggedId,
        IReadOnlyCollection<string> selectedIds,
        MoveScope scope,
        IEnumerable<(string FromId, string ToId)> edges)
    {
        // Base set: if the dragged node is part of the selection, move the
        // whole selection; otherwise just move the dragged node.
        var baseSet = new HashSet<string>();
        if (selectedIds.Count > 0 && selectedIds.Contains(draggedId))
            baseSet.UnionWith(selectedIds);
        else
            baseSet.Add(draggedId);

        if (scope != MoveScope.SelectedPlusRelated1D)
            return baseSet;

        // Add 1-depth undirected neighbors of any node in the base set.
        var related = new HashSet<string>();
        foreach (var (fromId, toId) in edges)
        {
            if (baseSet.Contains(fromId) && !baseSet.Contains(toId))
                related.Add(toId);
            else if (baseSet.Contains(toId) && !baseSet.Contains(fromId))
                related.Add(fromId);
        }

        baseSet.UnionWith(related);
        return baseSet;
    }
}
