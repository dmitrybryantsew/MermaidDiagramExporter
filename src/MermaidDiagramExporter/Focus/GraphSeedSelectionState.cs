using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Focus;

public sealed class GraphSeedSelectionState
{
    private readonly HashSet<string> _seedNodeIds = new();

    public int Count => _seedNodeIds.Count;

    public bool HasSeeds => _seedNodeIds.Count > 0;

    public IReadOnlyList<string> SeedNodeIds => _seedNodeIds.OrderBy(id => id).ToArray();

    public bool Contains(string nodeId)
    {
        return !string.IsNullOrEmpty(nodeId) && _seedNodeIds.Contains(nodeId);
    }

    public void Add(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
            _seedNodeIds.Add(nodeId);
    }

    public void Remove(string nodeId)
    {
        if (!string.IsNullOrEmpty(nodeId))
            _seedNodeIds.Remove(nodeId);
    }

    public void Clear()
    {
        _seedNodeIds.Clear();
    }

    public void PruneToGraph(TypeGraph? graph)
    {
        if (graph == null)
        {
            _seedNodeIds.Clear();
            return;
        }

        HashSet<string> validNodeIds = new(graph.Nodes.Select(node => node.Id));
        _seedNodeIds.RemoveWhere(nodeId => !validNodeIds.Contains(nodeId));
    }
}
