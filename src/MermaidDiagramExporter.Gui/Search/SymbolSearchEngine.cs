using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Search;

/// <summary>
/// Executes symbol searches against a SymbolIndex. Returns ranked results.
/// </summary>
public sealed class SymbolSearchEngine
{
    private SymbolIndex? _index;

    public void RebuildIndex(TypeGraph graph)
    {
        _index = SymbolIndex.Build(graph);
    }

    /// <summary>
    /// Searches for nodes matching the query. Returns node IDs in relevance order.
    /// </summary>
    public IReadOnlyList<SearchResult> Search(SearchCriteria criteria, ProjectSettings settings)
    {
        if (_index == null || criteria.IsEmpty)
            return Array.Empty<SearchResult>();

        var comparer = settings.SearchCaseSensitive ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;
        string target = criteria.TargetName;

        // Collect matching node IDs with scores
        var scores = new Dictionary<string, int>();

        // 1. Name index match (exact or prefix)
        foreach (var kvp in _index.NameIndex)
        {
            bool nameMatches = settings.SearchCaseSensitive
                ? kvp.Key.Contains(target)
                : kvp.Key.Contains(target, StringComparison.OrdinalIgnoreCase);

            if (nameMatches)
            {
                int score = kvp.Key.Equals(target, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                    ? 100 : 50; // exact match scores higher
                foreach (var nodeId in kvp.Value)
                    AddScore(scores, nodeId, score);
            }
        }

        // 2. Namespace filter — if user typed "MyGame.Player", restrict to that namespace
        if (criteria.NamespaceOrTypeHints.Count > 0)
        {
            string nsHint = string.Join(".", criteria.NamespaceOrTypeHints);
            var nsMatchingIds = new HashSet<string>();
            foreach (var kvp in _index.NamespaceIndex)
            {
                if (kvp.Key.Contains(nsHint, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                {
                    foreach (var id in kvp.Value)
                        nsMatchingIds.Add(id);
                }
            }
            // Intersection: only keep scores for nodes in matching namespaces
            var toRemove = scores.Keys.Where(id => !nsMatchingIds.Contains(id)).ToList();
            foreach (var id in toRemove)
                scores.Remove(id);

            // Boost namespace-exact matches
            foreach (var id in nsMatchingIds)
            {
                if (_index.NodesById.TryGetValue(id, out var node) &&
                    node.Namespace.Equals(nsHint, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
                {
                    AddScore(scores, id, 30);
                }
            }
        }

        // 3. Member index search (if enabled)
        if (settings.SearchIncludeMembers)
        {
            foreach (var kvp in _index.MemberIndex)
            {
                bool memberMatches = settings.SearchCaseSensitive
                    ? kvp.Key.Contains(target)
                    : kvp.Key.Contains(target, StringComparison.OrdinalIgnoreCase);

                if (memberMatches)
                {
                    foreach (var entry in kvp.Value)
                    {
                        int score = kvp.Key.Equals(target, settings.SearchCaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase)
                            ? 80 : 40;
                        AddScore(scores, entry.NodeId, score);
                    }
                }
            }
        }

        // 4. Build results from scored node IDs
        var results = new List<SearchResult>();
        foreach (var kvp in scores.OrderByDescending(kv => kv.Value))
        {
            if (_index.NodesById.TryGetValue(kvp.Key, out var node))
            {
                var matchingMembers = GetMatchingMembers(kvp.Key, target, settings);
                results.Add(new SearchResult
                {
                    NodeId = node.Id,
                    NodeDisplayName = node.DisplayName,
                    NodeNamespace = node.Namespace,
                    NodeKind = node.Kind.ToString(),
                    FilePath = node.AssetPath,
                    Score = kvp.Value,
                    MatchedMembers = matchingMembers
                });
            }
        }

        return results;
    }

    private IReadOnlyList<MatchedMember> GetMatchingMembers(string nodeId, string target, ProjectSettings settings)
    {
        if (_index == null || !_index.NodesById.TryGetValue(nodeId, out var node))
            return Array.Empty<MatchedMember>();

        var matched = new List<MatchedMember>();
        foreach (var member in node.Members)
        {
            bool nameMatch = settings.SearchCaseSensitive
                ? member.Name.Contains(target)
                : member.Name.Contains(target, StringComparison.OrdinalIgnoreCase);
            bool typeMatch = settings.SearchCaseSensitive
                ? member.TypeName.Contains(target)
                : member.TypeName.Contains(target, StringComparison.OrdinalIgnoreCase);

            if (nameMatch || typeMatch)
            {
                matched.Add(new MatchedMember(member.Name, member.TypeName, member.Kind.ToString()));
            }
        }
        return matched;
    }

    private static void AddScore(Dictionary<string, int> scores, string nodeId, int points)
    {
        if (!scores.TryGetValue(nodeId, out int current))
            current = 0;
        scores[nodeId] = current + points;
    }
}

public sealed class SearchResult
{
    public string NodeId { get; set; } = "";
    public string NodeDisplayName { get; set; } = "";
    public string NodeNamespace { get; set; } = "";
    public string NodeKind { get; set; } = "";
    public string FilePath { get; set; } = "";
    public int Score { get; set; }
    public IReadOnlyList<MatchedMember> MatchedMembers { get; set; } = Array.Empty<MatchedMember>();
    public bool IsExactNameMatch { get; set; }
}

public sealed class MatchedMember
{
    public string Name { get; }
    public string TypeName { get; }
    public string Kind { get; }
    public MatchedMember(string name, string typeName, string kind)
    {
        Name = name;
        TypeName = typeName;
        Kind = kind;
    }
}
