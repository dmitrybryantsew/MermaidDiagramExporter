using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using MermaidDiagramExporter.Gui.Layout;

namespace MermaidDiagramExporter.Gui.Persistence;

/// <summary>
/// Stores user-manually-adjusted node positions as deltas from the engine-computed positions.
/// Key = nodeId, Value = delta (offset) from the engine position.
/// Using deltas (not absolute positions) means adding new nodes won't break existing layouts.
/// </summary>
public sealed class ManualLayoutOverrides
{
    /// <summary>
    /// nodeId -> delta offset from engine-computed position.
    /// </summary>
    public Dictionary<string, Vector2> NodePositionDeltas { get; set; } = new();

    /// <summary>
    /// UTC timestamp of when these overrides were last saved.
    /// </summary>
    public DateTime LastSavedUtc { get; set; }

    /// <summary>
    /// Whether any overrides exist.
    /// </summary>
    public bool HasOverrides => NodePositionDeltas.Count > 0;

    /// <summary>
    /// Clears all overrides.
    /// </summary>
    public void Clear() => NodePositionDeltas.Clear();

    /// <summary>
    /// Records a manual override delta for a node.
    /// </summary>
    public void SetDelta(string nodeId, Vector2 delta)
    {
        if (delta.X == 0 && delta.Y == 0)
        {
            NodePositionDeltas.Remove(nodeId);
            return;
        }
        NodePositionDeltas[nodeId] = delta;
    }

    /// <summary>
    /// Gets the override delta for a node, or (0,0) if none.
    /// </summary>
    public Vector2 GetDelta(string nodeId)
    {
        return NodePositionDeltas.TryGetValue(nodeId, out var delta) ? delta : Vector2.zero;
    }
}

/// <summary>
/// JSON converter for Vector2 struct.
/// </summary>
public class Vector2JsonConverter : JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        float x = root.GetProperty("x").GetSingle();
        float y = root.GetProperty("y").GetSingle();
        return new Vector2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}
