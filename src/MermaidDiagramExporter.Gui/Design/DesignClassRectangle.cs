using SkiaSharp;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Sub-region hit-test result for a class rectangle. Used by
/// <c>DesignHitTestService</c> to distinguish clicks on the header, body,
/// resize handle, or edge ports.
/// </summary>
public enum ClassRectangleHitTest
{
    None,
    Body,
    Header,
    Member,
    ResizeHandle,
    LeftPort,
    RightPort
}

/// <summary>
/// Plain data object holding per-class interaction state and edit-handle
/// geometry for Design Mode. NOT an Avalonia Control — the rendering pipeline
/// is immediate-mode Skia into a single bitmap (see <c>GraphCanvas.RenderNow</c>).
/// <c>DesignCanvasController</c> consults this object during pointer handling,
/// and <c>CanvasRenderer</c> (extended) draws the edit affordances as extra
/// Skia calls layered into the existing bitmap.
/// </summary>
public sealed class ClassRectangle
{
    public ClassRectangle(string classId, DesignGraph graph)
    {
        ClassId = classId;
        Graph = graph;
    }

    /// <summary>The DesignClass this rectangle represents.</summary>
    public string ClassId { get; }

    /// <summary>The owning design graph.</summary>
    public DesignGraph Graph { get; }

    /// <summary>World X position (top-left corner).</summary>
    public float X { get; set; }

    /// <summary>World Y position (top-left corner).</summary>
    public float Y { get; set; }

    /// <summary>Width in world coordinates.</summary>
    public float Width { get; set; }

    /// <summary>Height in world coordinates.</summary>
    public float Height { get; set; }

    /// <summary>True if this class is currently selected.</summary>
    public bool IsSelected { get; set; }

    /// <summary>True while this class is being dragged.</summary>
    public bool IsDragging { get; set; }

    /// <summary>True while this class is being resized.</summary>
    public bool IsResizing { get; set; }

    /// <summary>True while the class name is being inline-edited.</summary>
    public bool IsInlineEditingName { get; set; }

    /// <summary>Index of the member being inline-edited, or null.</summary>
    public int? InlineEditingMemberIndex { get; set; }

    /// <summary>
    /// Resize handle bounds (bottom-right corner, <c>ResizeHandleSize</c> square).
    /// </summary>
    public SKRect ResizeHandleBounds => new SKRect(
        X + Width - DesignGeometry.ResizeHandleSize,
        Y + Height - DesignGeometry.ResizeHandleSize,
        X + Width,
        Y + Height);

    /// <summary>
    /// Left edge port hit area (left-center edge, <c>EdgePortDiameter</c> circle).
    /// </summary>
    public SKRect LeftPortBounds => new SKRect(
        X - DesignGeometry.EdgePortDiameter / 2f,
        Y + Height / 2f - DesignGeometry.EdgePortDiameter / 2f,
        X + DesignGeometry.EdgePortDiameter / 2f,
        Y + Height / 2f + DesignGeometry.EdgePortDiameter / 2f);

    /// <summary>
    /// Right edge port hit area (right-center edge).
    /// </summary>
    public SKRect RightPortBounds => new SKRect(
        X + Width - DesignGeometry.EdgePortDiameter / 2f,
        Y + Height / 2f - DesignGeometry.EdgePortDiameter / 2f,
        X + Width + DesignGeometry.EdgePortDiameter / 2f,
        Y + Height / 2f + DesignGeometry.EdgePortDiameter / 2f);

    /// <summary>
    /// Hit-tests a world-space point against this rectangle's sub-regions.
    /// Returns <see cref="ClassRectangleHitTest.None"/> if the point is outside.
    /// </summary>
    public ClassRectangleHitTest HitTest(SKPoint worldPos)
    {
        // Outside the class bounds entirely
        if (worldPos.X < X || worldPos.X > X + Width) return ClassRectangleHitTest.None;
        if (worldPos.Y < Y || worldPos.Y > Y + Height) return ClassRectangleHitTest.None;

        // Check sub-regions in priority order (top of Z-order wins)
        if (IsPointInRect(worldPos, ResizeHandleBounds)) return ClassRectangleHitTest.ResizeHandle;
        if (IsPointInRect(worldPos, RightPortBounds)) return ClassRectangleHitTest.RightPort;
        if (IsPointInRect(worldPos, LeftPortBounds)) return ClassRectangleHitTest.LeftPort;

        // Header is the top HeaderHeight pixels
        if (worldPos.Y < Y + DesignGeometry.HeaderHeight) return ClassRectangleHitTest.Header;

        // Otherwise it's the body or a member row
        return ClassRectangleHitTest.Body;
    }

    private static bool IsPointInRect(SKPoint p, SKRect r)
        => p.X >= r.Left && p.X <= r.Right && p.Y >= r.Top && p.Y <= r.Bottom;
}
