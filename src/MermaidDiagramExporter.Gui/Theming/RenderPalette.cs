using SkiaSharp;

namespace MermaidDiagramExporter.Gui.Theming;

/// <summary>
/// Skia-bridge palette for canvas, minimap, and matrix renderers. Two
/// instances: <see cref="Dark"/> and <see cref="Light"/>. The active one is
/// selected by <see cref="ThemeService.Apply"/> and consumed via the static
/// <see cref="Current"/> property.
/// </summary>
public sealed class RenderPalette
{
    // Canvas
    public required SKColor CanvasBg { get; init; }
    public required SKColor ClusterFill { get; init; }
    public required SKColor ClusterStroke { get; init; }
    public required SKColor ClusterLabel { get; init; }
    public required SKColor NodeFill { get; init; }
    public required SKColor NodeStroke { get; init; }
    public required SKColor NodeTitleText { get; init; }
    public required SKColor NodeMemberText { get; init; }
    public required SKColor EdgeLabel { get; init; }

    // Affordance paints (same in both themes — they're semantic accents)
    public required SKColor Selection { get; init; }
    public required SKColor Hover { get; init; }
    public required SKColor DropTarget { get; init; }
    public required SKColor Marquee { get; init; }
    public required SKColor RubberBand { get; init; }
    public required SKColor SearchMatch { get; init; }

    // Minimap
    public required SKColor MinimapBg { get; init; }
    public required SKColor MinimapNodeFill { get; init; }
    public required SKColor MinimapNodeStroke { get; init; }
    public required SKColor MinimapEdge { get; init; }
    public required SKColor MinimapViewportBorder { get; init; }

    public static RenderPalette Dark { get; } = new()
    {
        CanvasBg = new SKColor(0x1A, 0x1E, 0x24),
        ClusterFill = new SKColor(0x25, 0x2A, 0x32),
        ClusterStroke = new SKColor(0x3A, 0x42, 0x50),
        ClusterLabel = new SKColor(0x70, 0x80, 0x90),
        NodeFill = new SKColor(0x2D, 0x33, 0x3F),
        NodeStroke = new SKColor(0x3A, 0x42, 0x50),
        NodeTitleText = new SKColor(0xE0, 0xE6, 0xEC),
        NodeMemberText = new SKColor(0x88, 0x90, 0x98),
        EdgeLabel = new SKColor(0x88, 0x90, 0x98),
        Selection = new SKColor(0xFF, 0x8C, 0x00),
        Hover = new SKColor(0x60, 0xA0, 0xE0),
        DropTarget = new SKColor(0x40, 0xB0, 0x70),
        Marquee = new SKColor(0xFF, 0xA0, 0x40, 0x28),
        RubberBand = new SKColor(0xFF, 0xA0, 0x40),
        SearchMatch = new SKColor(0xFF, 0xE0, 0x40),
        MinimapBg = new SKColor(0x15, 0x19, 0x1E),
        MinimapNodeFill = new SKColor(0x2D, 0x33, 0x3F),
        MinimapNodeStroke = new SKColor(0x4A, 0x6A, 0x8A),
        MinimapEdge = new SKColor(0x3A, 0x42, 0x50),
        MinimapViewportBorder = new SKColor(0xFF, 0xE0, 0x40),
    };

    public static RenderPalette Light { get; } = new()
    {
        CanvasBg = new SKColor(0xF7, 0xF8, 0xFA),
        ClusterFill = new SKColor(0xED, 0xEF, 0xF3),
        ClusterStroke = new SKColor(0xC8, 0xCF, 0xD8),
        ClusterLabel = new SKColor(0x6B, 0x76, 0x84),
        NodeFill = new SKColor(0xFF, 0xFF, 0xFF),
        NodeStroke = new SKColor(0xB8, 0xC0, 0xC8),
        NodeTitleText = new SKColor(0x1A, 0x1A, 0x1A),
        NodeMemberText = new SKColor(0x5A, 0x64, 0x70),
        EdgeLabel = new SKColor(0x5A, 0x64, 0x70),
        Selection = new SKColor(0xFF, 0x8C, 0x00),
        Hover = new SKColor(0x60, 0xA0, 0xE0),
        DropTarget = new SKColor(0x40, 0xB0, 0x70),
        Marquee = new SKColor(0xFF, 0xA0, 0x40, 0x28),
        RubberBand = new SKColor(0xFF, 0xA0, 0x40),
        SearchMatch = new SKColor(0xFF, 0xC0, 0x40),
        MinimapBg = new SKColor(0xEC, 0xEF, 0xF1),
        MinimapNodeFill = new SKColor(0xC8, 0xD0, 0xD8),
        MinimapNodeStroke = new SKColor(0x8A, 0x96, 0xA4),
        MinimapEdge = new SKColor(0xB0, 0xB8, 0xC0),
        MinimapViewportBorder = new SKColor(0xE6, 0x90, 0x20),
    };

    /// <summary>
    /// Currently active palette. Updated by <see cref="ThemeService.Apply"/>.
    /// </summary>
    public static RenderPalette Current { get; private set; } = Dark;

    /// <summary>
    /// Sets the active palette. Call this BEFORE raising
    /// <see cref="ThemeService.ThemeChanged"/> so renderers see the new colors.
    /// </summary>
    public static void SetCurrent(RenderPalette palette) => Current = palette;
}
