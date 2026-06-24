namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Shared geometry constants for Design Mode. Used by both
/// <c>CanvasRenderer</c> (for drawing interactive member rows) and
/// <c>DesignHitTestService</c> (for hit-testing which row the cursor is over).
/// These constants are the single source of truth for member-row geometry in
/// Design Mode. They must agree between drawing and hit-testing, or
/// hit-testing will be off by one row.
/// </summary>
internal static class DesignGeometry
{
    /// <summary>
    /// Height of each member row in Design Mode. Distinct from
    /// <c>CanvasRenderer.NodeMemberHeight</c> (which is private to Analyze
    /// Mode's static rendering) because Design Mode rows include interactive
    /// affordances (visibility prefix, delete button, alternating background)
    /// that may need different vertical spacing than Analyze Mode's flat text.
    /// </summary>
    public const float MemberRowHeight = 18f;

    /// <summary>
    /// Height of the class header (class name + stereotype badge).
    /// </summary>
    public const float HeaderHeight = 24f;

    /// <summary>
    /// Minimum width of a class rectangle.
    /// </summary>
    public const float MinClassWidth = 180f;

    /// <summary>
    /// Minimum height of a class rectangle (header + one member row).
    /// </summary>
    public const float MinClassHeight = 42f;

    /// <summary>
    /// Maximum width of a class rectangle (caps absurdly wide classes).
    /// </summary>
    public const float MaxClassWidth = 600f;

    /// <summary>
    /// Maximum height of a class rectangle.
    /// </summary>
    public const float MaxClassHeight = 1000f;

    /// <summary>
    /// Size of the resize handle (bottom-right corner).
    /// </summary>
    public const float ResizeHandleSize = 12f;

    /// <summary>
    /// Diameter of edge ports (left/right edges of class).
    /// </summary>
    public const float EdgePortDiameter = 10f;
}
