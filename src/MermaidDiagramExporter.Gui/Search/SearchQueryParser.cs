using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Search;

/// <summary>
/// Parses search query strings into structured criteria.
/// </summary>
public static class SearchQueryParser
{
    public static SearchCriteria Parse(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new SearchCriteria { RawQuery = query ?? "" };

        var criteria = new SearchCriteria { RawQuery = query.Trim() };

        string q = criteria.RawQuery;

        // Check for wildcard prefix: "*.Health" or "*Health"
        if (q.StartsWith("*."))
        {
            criteria.WildcardPrefix = true;
            q = q.Substring(2);
        }
        else if (q.StartsWith("*"))
        {
            criteria.WildcardPrefix = true;
            q = q.Substring(1);
        }

        // Check for namespace-qualified query: "Namespace.Class.Member"
        var parts = q.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2)
        {
            // Last part is the target name; earlier parts form namespace/type filters
            criteria.TargetName = parts[^1];
            criteria.NamespaceOrTypeHints = parts.Take(parts.Length - 1).ToList();
        }
        else
        {
            criteria.TargetName = q;
        }

        return criteria;
    }
}

public sealed class SearchCriteria
{
    public string RawQuery { get; set; } = "";
    public string TargetName { get; set; } = "";
    public IReadOnlyList<string> NamespaceOrTypeHints { get; set; } = Array.Empty<string>();
    public bool WildcardPrefix { get; set; }

    public bool IsEmpty => string.IsNullOrWhiteSpace(TargetName) && NamespaceOrTypeHints.Count == 0;
}
