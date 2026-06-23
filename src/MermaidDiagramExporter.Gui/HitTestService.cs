using System;
using System.Collections.Generic;
using SkiaSharp;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Owns hit-testing logic previously inline in GraphCanvas.
/// Extracted in Step 17 to separate hit-testing from input handling and rendering.
/// </summary>
public sealed class HitTestService
{
    /// <summary>
    /// Transforms screen coordinates to world coordinates using the current viewport state.
    /// </summary>
    public static SKPoint ScreenToWorld(float screenX, float screenY, float panX, float panY, float zoom)
    {
        return new SKPoint((screenX - panX) / zoom, (screenY - panY) / zoom);
    }

    /// <summary>
    /// Finds the topmost node at the given world position (reverse draw order = topmost first).
    /// </summary>
    public static GraphNode? HitTest(SKPoint worldPos, List<GraphNode> nodes)
    {
        for (int i = nodes.Count - 1; i >= 0; i--)
        {
            var node = nodes[i];
            if (worldPos.X >= node.X && worldPos.X <= node.X + node.Width &&
                worldPos.Y >= node.Y && worldPos.Y <= node.Y + node.Height)
            {
                return node;
            }
        }
        return null;
    }
}
