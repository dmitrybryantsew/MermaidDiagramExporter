using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Focus/navigation system — shows only classes connected to a selected class,
/// with a back/forward stack for navigation history.
/// </summary>
public class FocusNavigator
{
    private readonly Stack<string> _backStack = new();
    private readonly Stack<string> _forwardStack = new();
    private string? _currentFocus;

    public string? CurrentFocus => _currentFocus;
    public bool CanGoBack => _backStack.Count > 0;
    public bool CanGoForward => _forwardStack.Count > 0;

    public event Action? FocusChanged;

    public void FocusOn(string nodeId, Dictionary<string, GraphNode> nodeMap, List<GraphEdge> edges)
    {
        if (_currentFocus != null)
            _backStack.Push(_currentFocus);
        _forwardStack.Clear();
        _currentFocus = nodeId;
        FocusChanged?.Invoke();
    }

    public void GoBack()
    {
        if (_backStack.Count == 0) return;
        if (_currentFocus != null)
            _forwardStack.Push(_currentFocus);
        _currentFocus = _backStack.Pop();
        FocusChanged?.Invoke();
    }

    public void GoForward()
    {
        if (_forwardStack.Count == 0) return;
        if (_currentFocus != null)
            _backStack.Push(_currentFocus);
        _currentFocus = _forwardStack.Pop();
        FocusChanged?.Invoke();
    }

    public void Clear()
    {
        _backStack.Clear();
        _forwardStack.Clear();
        _currentFocus = null;
        FocusChanged?.Invoke();
    }

    /// <summary>
    /// Returns the set of node IDs that should be visible given the current focus.
    /// If no focus is set, returns null (meaning "show all").
    /// </summary>
    public HashSet<string>? GetVisibleNodeIds(Dictionary<string, GraphNode> nodeMap, List<GraphEdge> edges)
    {
        if (_currentFocus == null || !nodeMap.ContainsKey(_currentFocus))
            return null;

        var visible = new HashSet<string> { _currentFocus };

        foreach (var edge in edges)
        {
            if (edge.FromNode?.Id == _currentFocus && edge.ToNode != null)
                visible.Add(edge.ToNode.Id);
            else if (edge.ToNode?.Id == _currentFocus && edge.FromNode != null)
                visible.Add(edge.FromNode.Id);
        }

        return visible;
    }
}
