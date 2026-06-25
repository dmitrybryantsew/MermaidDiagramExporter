namespace MermaidDiagramExporter.Gui.Design;

/// <summary>Lightweight wrapper for member rows in the inspector list.
/// Top-level class (not a record) so Avalonia XAML can resolve the
/// DisplayText property for binding.</summary>
public sealed class MemberRow
{
    public MemberRow(string displayText, int index)
    {
        DisplayText = displayText;
        Index = index;
    }
    public string DisplayText { get; }
    public int Index { get; }
}

/// <summary>Lightweight wrapper for edge rows in the inspector list.
/// Top-level class (not a record) so Avalonia XAML can resolve the
/// DisplayText/Kind properties for binding.</summary>
public sealed class EdgeRow
{
    public EdgeRow(string id, string kind, string displayText)
    {
        Id = id;
        Kind = kind;
        DisplayText = displayText;
    }
    public string Id { get; }
    public string Kind { get; set; }
    public string DisplayText { get; }
}
