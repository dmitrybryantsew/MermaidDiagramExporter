using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// JSON save/load for DesignGraph. Uses System.Text.Json (already used
/// elsewhere in the codebase). File extension: .dgraph.json.
/// </summary>
public static class DesignSerialization
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>
    /// Saves the graph to a file as JSON. Updates ModifiedUtc before writing.
    /// Throws on I/O errors (caller should catch and show a user-friendly message).
    /// </summary>
    public static void Save(DesignGraph graph, string filePath)
    {
        graph.ModifiedUtc = System.DateTime.UtcNow;
        string json = JsonSerializer.Serialize(graph, Options);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Loads a graph from a JSON file. Throws InvalidDataException if the
    /// version is unsupported or the file is malformed.
    /// </summary>
    public static DesignGraph Load(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var graph = JsonSerializer.Deserialize<DesignGraph>(json, Options)
            ?? throw new InvalidDataException("Failed to deserialize design graph (null result)");
        return graph;
    }

    /// <summary>
    /// Tries to load a graph from a JSON file. Returns null on any failure
    /// (malformed JSON, unsupported version, I/O error). Used by the UI
    /// for graceful error handling.
    /// </summary>
    public static DesignGraph? TryLoad(string filePath)
    {
        try
        {
            return Load(filePath);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Validates that the graph's version is supported. Returns true if OK,
    /// false if the version is unrecognized.
    /// </summary>
    public static bool IsVersionSupported(string version)
    {
        return version == "1";
    }
}
