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
