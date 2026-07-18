using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Focus;

public sealed class FocusedGraphNavigationController
{
    private readonly GraphViewSession _session = new();
    private readonly FocusedSubgraphBuilder _focusedSubgraphBuilder = new();

    public TypeGraph? RootGraph => _session.RootGraph;

    public TypeGraph? CurrentGraph => _session.CurrentGraph;

    public bool IsFocusedView =>
        _session.RootGraph != null
        && _session.CurrentGraph != null
        && !ReferenceEquals(_session.RootGraph, _session.CurrentGraph);

    public void SetRootGraph(TypeGraph graph, string sourceKey)
    {
        _session.Initialize(graph, sourceKey);
    }

    public bool CanFocusSelection(string selectedNodeId)
    {
        return CurrentGraph != null && !string.IsNullOrEmpty(selectedNodeId);
    }

    public bool CanFocusSelection(IReadOnlyCollection<string> selectedNodeIds)
    {
        return CurrentGraph != null && selectedNodeIds.Count > 0;
    }

    public bool CanGoBack() => _session.CanGoBack();

    public bool CanGoForward() => _session.CanGoForward();

    public TypeGraph? FocusSelection(string selectedNodeId, int depth, GraphFocusTraversalMode traversalMode)
    {
        if (!CanFocusSelection(selectedNodeId))
            return null;

        return FocusSelection(new[] { selectedNodeId }, depth, traversalMode);
    }

    public TypeGraph? FocusSelection(IReadOnlyList<string> selectedNodeIds, int depth, GraphFocusTraversalMode traversalMode)
    {
        if (!CanFocusSelection(selectedNodeIds))
            return null;

        GraphFocusRequest request = new()
        {
            SeedNodeIds = selectedNodeIds.ToArray(),
            AssociationDepth = depth,
            TraversalMode = traversalMode
        };

        TypeGraph? focusedGraph = _focusedSubgraphBuilder.BuildFocusedGraph(CurrentGraph, request);
        if (focusedGraph == null)
            return null;

        string primarySelectedNodeId = selectedNodeIds.Count > 0 ? selectedNodeIds[0] : string.Empty;
        _session.PushFocusedGraph(focusedGraph, request, primarySelectedNodeId);
        return focusedGraph;
    }

    public GraphViewSnapshot? GoBack() => _session.GoBack();

    public GraphViewSnapshot? GoForward() => _session.GoForward();

    public TypeGraph? ResetToRoot()
    {
        _session.ResetToRoot();
        return _session.CurrentGraph;
    }

    /// <summary>
    /// Builds a focused subgraph from the given seeds without pushing onto
    /// the back/forward navigation stack. Used by "Get Code" actions that need
    /// the connected-classes set for a depth without mutating history.
    /// Operates on <see cref="CurrentGraph"/> by default; pass
    /// <paramref name="sourceGraph"/> to run against a different graph (e.g.
    /// <see cref="RootGraph"/> for full-project bundles).
    /// </summary>
    public TypeGraph? BuildFocusedSubgraph(
        IReadOnlyList<string> seedNodeIds,
        int depth,
        GraphFocusTraversalMode traversalMode,
        TypeGraph? sourceGraph = null)
    {
        TypeGraph? graph = sourceGraph ?? CurrentGraph;
        if (graph == null || seedNodeIds == null || seedNodeIds.Count == 0)
            return null;

        GraphFocusRequest request = new()
        {
            SeedNodeIds = seedNodeIds.ToArray(),
            AssociationDepth = depth,
            TraversalMode = traversalMode
        };

        return _focusedSubgraphBuilder.BuildFocusedGraph(graph, request);
    }
}
