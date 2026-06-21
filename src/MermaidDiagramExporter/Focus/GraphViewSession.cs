using System.Collections.Generic;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Focus;

public sealed class GraphViewSession
{
    private readonly Stack<GraphViewSnapshot> _backStack = new();
    private readonly Stack<GraphViewSnapshot> _forwardStack = new();
    private string _currentSelectedNodeId = string.Empty;

    public TypeGraph? RootGraph { get; private set; }

    public TypeGraph? CurrentGraph { get; private set; }

    public string SourceKey { get; private set; } = string.Empty;

    public void Initialize(TypeGraph? rootGraph, string sourceKey)
    {
        RootGraph = rootGraph;
        CurrentGraph = rootGraph;
        SourceKey = sourceKey ?? string.Empty;
        _currentSelectedNodeId = string.Empty;
        _backStack.Clear();
        _forwardStack.Clear();
    }

    public void PushFocusedGraph(TypeGraph focusedGraph, GraphFocusRequest request, string selectedNodeId)
    {
        if (CurrentGraph != null)
        {
            _backStack.Push(new GraphViewSnapshot
            {
                Graph = CurrentGraph,
                Request = request,
                Title = CurrentGraph.Title,
                SelectedNodeId = _currentSelectedNodeId
            });
        }

        _forwardStack.Clear();
        CurrentGraph = focusedGraph;
        _currentSelectedNodeId = selectedNodeId ?? string.Empty;
    }

    public bool CanGoBack() => _backStack.Count > 0;

    public bool CanGoForward() => _forwardStack.Count > 0;

    public GraphViewSnapshot? GoBack()
    {
        if (_backStack.Count == 0)
            return null;

        if (CurrentGraph != null)
        {
            _forwardStack.Push(new GraphViewSnapshot
            {
                Graph = CurrentGraph,
                Title = CurrentGraph.Title,
                SelectedNodeId = _currentSelectedNodeId
            });
        }

        GraphViewSnapshot snapshot = _backStack.Pop();
        CurrentGraph = snapshot.Graph;
        _currentSelectedNodeId = snapshot.SelectedNodeId;
        return snapshot;
    }

    public GraphViewSnapshot? GoForward()
    {
        if (_forwardStack.Count == 0)
            return null;

        if (CurrentGraph != null)
        {
            _backStack.Push(new GraphViewSnapshot
            {
                Graph = CurrentGraph,
                Title = CurrentGraph.Title,
                SelectedNodeId = _currentSelectedNodeId
            });
        }

        GraphViewSnapshot snapshot = _forwardStack.Pop();
        CurrentGraph = snapshot.Graph;
        _currentSelectedNodeId = snapshot.SelectedNodeId;
        return snapshot;
    }

    public void ResetToRoot()
    {
        CurrentGraph = RootGraph;
        _currentSelectedNodeId = string.Empty;
        _backStack.Clear();
        _forwardStack.Clear();
    }
}
