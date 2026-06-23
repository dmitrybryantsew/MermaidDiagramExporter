using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Shared edge weight table used by both the old and new layout engines.
/// Higher weight = ranker tries harder to keep this edge short.
/// </summary>
internal static class LayoutEdgeWeights
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
}
