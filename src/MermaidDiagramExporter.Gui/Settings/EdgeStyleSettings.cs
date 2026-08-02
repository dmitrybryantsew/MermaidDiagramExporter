using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Settings;

/// <summary>
/// Per-kind edge visual style (color + arrowhead shape). Serialized in ProjectSettings.
/// Defaults follow UML conventions: inheritance/implements = hollow triangle,
/// association = solid arrow.
/// </summary>
public sealed class EdgeStyleSettings
{
    public EdgeKindStyle Inheritance { get; set; } = new()
    {
        ColorHex = "#5090D0",
        Arrowhead = EdgeArrowheadStyle.HollowTriangle,
    };

    public EdgeKindStyle Implements { get; set; } = new()
    {
        ColorHex = "#40B070",
        Arrowhead = EdgeArrowheadStyle.HollowTriangle,
    };

    public EdgeKindStyle Association { get; set; } = new()
    {
        ColorHex = "#606060",
        Arrowhead = EdgeArrowheadStyle.SolidArrow,
    };

    public EdgeKindStyle ForKind(MermaidDiagramExporter.Core.TypeEdgeKind kind) => kind switch
    {
        MermaidDiagramExporter.Core.TypeEdgeKind.Inheritance => Inheritance,
        MermaidDiagramExporter.Core.TypeEdgeKind.Implements => Implements,
        _ => Association,
    };
}

public sealed class EdgeKindStyle
{
    public string ColorHex { get; set; } = "#606060";
    public EdgeArrowheadStyle Arrowhead { get; set; } = EdgeArrowheadStyle.SolidArrow;
}

/// <summary>
/// Arrowhead rendering styles following UML conventions.
/// </summary>
public enum EdgeArrowheadStyle
{
    /// <summary>Filled V-shaped arrow (default for associations).</summary>
    SolidArrow,
    /// <summary>Hollow triangle (UML generalization/realization).</summary>
    HollowTriangle,
    /// <summary>Hollow diamond (UML aggregation).</summary>
    HollowDiamond,
    /// <summary>Filled diamond (UML composition).</summary>
    FilledDiamond,
    /// <summary>No arrowhead.</summary>
    None,
}
