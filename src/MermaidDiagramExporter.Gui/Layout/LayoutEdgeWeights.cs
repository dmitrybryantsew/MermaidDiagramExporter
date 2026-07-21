using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Shared edge weight table used by both the old and new layout engines.
/// Higher weight = ranker tries harder to keep this edge short.
/// </summary>
public static class LayoutEdgeWeights
{
    /// <summary>
    /// Returns the weight for an edge of the given kind.
    /// Mirrors the original LayeredLayoutEngine.GetEdgeWeight table:
    /// Inheritance=3, Implements=2.5, else 1.
    /// </summary>
    public static float GetWeight(TypeEdgeKind kind)
    {
        return kind switch
        {
            TypeEdgeKind.Inheritance => 3f,
            TypeEdgeKind.Implements => 2.5f,
            _ => 1f
        };
    }

    /// <summary>
    /// Integer weight for engines whose API takes int weights (MSAGL
    /// <c>Edge.Weight</c>). Scaled ×2 from <see cref="GetWeight"/> so the
    /// Inheritance &gt; Implements &gt; other ordering survives the float→int
    /// conversion: 6 / 5 / 2.
    /// </summary>
    public static int GetIntWeight(TypeEdgeKind kind)
    {
        return (int)System.Math.Round(GetWeight(kind) * 2f);
    }
}
