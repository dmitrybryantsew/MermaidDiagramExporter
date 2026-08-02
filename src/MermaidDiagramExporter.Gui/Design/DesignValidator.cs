using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Basic validation for DesignGraph. Catches structural problems that would
/// cause invalid Mermaid output or invalid C# stub output. Called before
/// export and before save.
/// </summary>
public static class DesignValidator
{
    /// <summary>
    /// Returns a list of validation errors. Empty list means the graph is valid.
    /// </summary>
    public static List<string> Validate(DesignGraph graph)
    {
        var errors = new List<string>();

        // Check for duplicate class names within the same namespace
        var duplicates = graph.Classes
            .GroupBy(c => (c.Namespace, c.Name))
            .Where(g => g.Count() > 1);
        foreach (var dup in duplicates)
            errors.Add($"Duplicate class '{dup.Key.Name}' in namespace '{dup.Key.Namespace}'");

        // Check for edges referencing non-existent classes
        var classIds = new HashSet<string>(graph.Classes.Select(c => c.Id));
        foreach (var edge in graph.Edges)
        {
            if (!classIds.Contains(edge.FromClassId))
                errors.Add($"Edge {edge.Id} references missing source class {edge.FromClassId}");
            if (!classIds.Contains(edge.ToClassId))
                errors.Add($"Edge {edge.Id} references missing target class {edge.ToClassId}");
        }

        // Check for self-edges on inheritance/implements (invalid C#)
        foreach (var edge in graph.Edges.Where(e => e.Kind is EdgeKind.Inheritance or EdgeKind.Implements))
        {
            if (edge.FromClassId == edge.ToClassId)
                errors.Add($"Class cannot inherit from or implement itself: {edge.FromClassId}");
        }

        // Check for cycles in inheritance (would be a compile error in C#)
        foreach (var cycle in FindInheritanceCycles(graph))
            errors.Add($"Inheritance cycle detected: {string.Join(" -> ", cycle)}");

        return errors;
    }

    /// <summary>
    /// Returns true if the graph has no validation errors.
    /// </summary>
    public static bool IsValid(DesignGraph graph) => Validate(graph).Count == 0;

    /// <summary>
    /// Finds all inheritance cycles in the graph. Returns a list of cycles,
    /// where each cycle is a list of class names forming the loop.
    /// </summary>
    private static List<List<string>> FindInheritanceCycles(DesignGraph graph)
    {
        var cycles = new List<List<string>>();
        var classById = graph.Classes.ToDictionary(c => c.Id, c => c.Name);

        // Build inheritance adjacency: parent -> children
        var children = new Dictionary<string, List<string>>();
        foreach (var edge in graph.Edges.Where(e => e.Kind == EdgeKind.Inheritance))
        {
            if (!children.TryGetValue(edge.FromClassId, out var list))
                children[edge.FromClassId] = list = new List<string>();
            list.Add(edge.ToClassId);
        }

        // DFS with white/gray/black coloring to detect cycles
        var color = new Dictionary<string, int>(); // 0=white, 1=gray, 2=black
        foreach (var cls in graph.Classes)
            color[cls.Id] = 0;

        var path = new List<string>();

        void Visit(string nodeId)
        {
            if (color.TryGetValue(nodeId, out var c) && c == 1)
            {
                // Found a back edge — extract cycle
                int startIdx = path.IndexOf(nodeId);
                if (startIdx >= 0)
                {
                    var cycle = path.GetRange(startIdx, path.Count - startIdx);
                    cycle.Add(nodeId); // close the cycle
                    cycles.Add(cycle.Select(id => classById.TryGetValue(id, out var n) ? n : id).ToList());
                }
                return;
            }
            if (c == 2) return; // already fully explored, no cycle through here

            color[nodeId] = 1;
            path.Add(nodeId);

            if (children.TryGetValue(nodeId, out var kids))
            {
                foreach (var kid in kids.ToList()) // copy to allow modification
                    Visit(kid);
            }

            path.RemoveAt(path.Count - 1);
            color[nodeId] = 2;
        }

        foreach (var cls in graph.Classes)
        {
            if (color[cls.Id] == 0)
                Visit(cls.Id);
        }

        return cycles;
    }
}
