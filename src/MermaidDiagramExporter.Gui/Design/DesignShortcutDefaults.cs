using System;
using System.Collections.Generic;
using Avalonia.Input;

namespace MermaidDiagramExporter.Gui.Design;

public static class DesignShortcutDefaults
{
    public static readonly Dictionary<string, string> DefaultBindings = new()
    {
        ["Select"] = "V",
        ["Class"] = "C",
        ["Interface"] = "I",
        ["Enum"] = "E",
        ["Struct"] = "S",
        ["AbstractClass"] = "A",
        ["StaticClass"] = "T",
        ["Namespace"] = "N",
        ["EdgeInheritance"] = "H",
        ["EdgeImplements"] = "M",
        ["EdgeAssociation"] = "L",
        ["EdgeDependency"] = "D",
        ["EdgeAggregation"] = "G",
        ["EdgeComposition"] = "O",
    };

    public static readonly string[] ToolNames = new[]
    {
        "Select", "Class", "Interface", "Enum", "Struct",
        "AbstractClass", "StaticClass", "Namespace",
        "EdgeInheritance", "EdgeImplements", "EdgeAssociation",
        "EdgeDependency", "EdgeAggregation", "EdgeComposition",
    };

    public static string GetEffectiveKey(string toolName, Dictionary<string, string>? customBindings)
    {
        if (customBindings != null && customBindings.TryGetValue(toolName, out var customKey) && !string.IsNullOrWhiteSpace(customKey))
            return customKey;
        return DefaultBindings.TryGetValue(toolName, out var defaultKey) ? defaultKey : "";
    }

    public static DesignTool? KeyToTool(Key key, Dictionary<string, string>? customBindings)
    {
        foreach (var toolName in ToolNames)
        {
            var binding = GetEffectiveKey(toolName, customBindings);
            if (TryMatchKey(key, binding) && Enum.TryParse<DesignTool>(toolName, out var tool))
                return tool;
        }
        return null;
    }

    private static bool TryMatchKey(Key key, string binding)
    {
        if (string.IsNullOrEmpty(binding)) return false;
        if (Enum.TryParse<Key>(binding, ignoreCase: true, out var parsedKey))
            return key == parsedKey;
        return binding.Equals(key.ToString(), StringComparison.OrdinalIgnoreCase);
    }
}
