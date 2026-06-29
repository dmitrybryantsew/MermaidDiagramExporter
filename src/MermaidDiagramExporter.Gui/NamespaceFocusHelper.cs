using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Extracts top-level namespace groups from a TypeGraph for the namespace
/// focus dropdown, and resolves a selected namespace to seed node IDs for
/// the existing focus BFS pipeline.
/// </summary>
public static class NamespaceFocusHelper
{
    /// <summary>
    /// Returns a sorted list of distinct first-level namespace identifiers
    /// (e.g. "PFE.Data", "PFE.Systems", "PFE.Tests") after auto-detecting
    /// the topmost common prefix across all node namespaces.
    /// </summary>
    public static List<string> GetTopLevelNamespaces(Core.TypeGraph? graph)
    {
        if (graph == null || graph.Nodes.Count == 0) return new();

        var allParts = graph.Nodes
            .Select(n => (n.Namespace ?? "").Split('.'))
            .Where(parts => parts.Length > 0 && !string.IsNullOrEmpty(parts[0]))
            .ToList();

        if (allParts.Count == 0) return new();

        // Find longest common prefix
        int prefixLen = 0;
        int minLen = allParts.Min(p => p.Length);
        for (int i = 0; i < minLen; i++)
        {
            string segment = allParts[0][i];
            if (allParts.Any(p => p[i] != segment)) break;
            prefixLen++;
        }

        // Group by prefix + next segment
        var seen = new HashSet<string>();
        var result = new List<string>();
        foreach (var parts in allParts)
        {
            string key;
            if (parts.Length > prefixLen)
                key = string.Join('.', parts, 0, prefixLen + 1);
            else if (parts.Length > 0)
                key = string.Join('.', parts);
            else
                continue;

            if (seen.Add(key))
                result.Add(key);
        }

        result.Sort();
        return result;
    }

    /// <summary>
    /// Returns all node IDs whose namespace starts with the given prefix
    /// (e.g. "PFE.Data" matches "PFE.Data", "PFE.Data.Repositories", etc.).
    /// </summary>
    public static HashSet<string> GetNodeIdsInNamespace(Core.TypeGraph? graph, string namespacePrefix)
    {
        if (graph == null || string.IsNullOrEmpty(namespacePrefix))
            return new();

        var result = new HashSet<string>();
        foreach (var node in graph.Nodes)
        {
            var ns = node.Namespace ?? "";
            if (ns == namespacePrefix || ns.StartsWith(namespacePrefix + "."))
                result.Add(node.Id);
        }
        return result;
    }
}
