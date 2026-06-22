using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Stereotypes;

/// <summary>
/// Evaluates custom stereotype rules against type identifiers.
/// Thread-safe; holds compiled regexes internally.
/// </summary>
public sealed class CustomStereotypeEngine
{
    private readonly List<CompiledRule> _rules = new();

    public CustomStereotypeEngine(IEnumerable<StereotypeRule> rules)
    {
        foreach (var rule in rules ?? Enumerable.Empty<StereotypeRule>())
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                continue;
            try
            {
                var regex = new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                _rules.Add(new CompiledRule(regex, rule.Label, rule.ColorHex));
            }
            catch { /* invalid regex — skip */ }
        }
    }

    /// <summary>
    /// Returns all matching stereotypes for a given type name.
    /// </summary>
    public IReadOnlyList<MatchedStereotype> Match(string typeName)
    {
        var results = new List<MatchedStereotype>();
        foreach (var rule in _rules)
        {
            if (rule.Regex.IsMatch(typeName))
            {
                results.Add(new MatchedStereotype(rule.Label, rule.ColorHex));
            }
        }
        return results;
    }

    /// <summary>
    /// Returns all matching stereotypes by testing the type name AND each base type name in the chain.
    /// </summary>
    public IReadOnlyList<MatchedStereotype> MatchChain(params string[] typeNamesInChain)
    {
        var results = new List<MatchedStereotype>();
        foreach (var rule in _rules)
        {
            bool matched = false;
            foreach (var typeName in typeNamesInChain)
            {
                if (typeName != null && rule.Regex.IsMatch(typeName))
                {
                    matched = true;
                    break;
                }
            }
            if (matched)
            {
                results.Add(new MatchedStereotype(rule.Label, rule.ColorHex));
            }
        }
        return results;
    }

    public bool HasRules => _rules.Count > 0;

    /// <summary>
    /// Validates a regex pattern without throwing. Returns true if valid, false with error message if not.
    /// </summary>
    public static bool TryValidatePattern(string pattern, out string? errorMessage)
    {
        try
        {
            _ = new Regex(pattern);
            errorMessage = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private sealed class CompiledRule
    {
        public Regex Regex;
        public string Label;
        public string ColorHex;
        public CompiledRule(Regex regex, string label, string colorHex)
        {
            Regex = regex;
            Label = label;
            ColorHex = colorHex;
        }
    }
}

public sealed class MatchedStereotype
{
    public string Label { get; }
    public string ColorHex { get; }
    public MatchedStereotype(string label, string colorHex)
    {
        Label = label;
        ColorHex = colorHex;
    }
}
