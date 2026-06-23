using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Projects a finished CompoundGraph back to LayoutResult in the exact same shape
/// the old engine produces, so downstream consumers (PostLayoutPipeline, EdgeRoutingService,
/// rendering) need zero changes. Per docs/08 Part B.
/// </summary>
public static class CompoundResultProjector
{
    public static LayoutResult Project(CompoundGraph compound, LayoutGraph originalGraph, LayoutOptions options)
    {
        var nodeBounds = new Dictionary<string, Rect>();
        foreach (var node in compound.Nodes.Where(n => n.Kind == CompoundNodeKind.Real))
        {
            if (!string.IsNullOrEmpty(node.SourceLayoutNodeId))
                nodeBounds[node.SourceLayoutNodeId!] = new Rect(node.X, node.Y, node.Width, node.Height);
        }

        var clusterBounds = new Dictionary<string, Rect>();
        foreach (var cluster in originalGraph.Clusters)
            clusterBounds[cluster.Id] = ComputeClusterBoundingBox(cluster, compound, options);

        float contentWidth = nodeBounds.Count > 0
            ? nodeBounds.Values.Max(r => r.xMax) + options.OuterMarginX
            : options.MinimumContentWidth;
        float contentHeight = nodeBounds.Count > 0
            ? nodeBounds.Values.Max(r => r.yMax) + options.OuterMarginY
            : options.MinimumContentHeight;

        // Build edge dummy paths from the compound graph's recorded dummy chains.
        // Each chain contains ordered CompoundNode ids (segment dummies + endpoints).
        // We convert to absolute positions so EdgeRoutingService can use them as waypoints.
        var edgeDummyPaths = BuildEdgeDummyPaths(compound);

        return new LayoutResult
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            ContentSize = new Vector2(
                System.Math.Max(options.MinimumContentWidth, contentWidth),
                System.Math.Max(options.MinimumContentHeight, contentHeight)),
            EdgeDummyPaths = edgeDummyPaths,
        };
    }

    /// <summary>
    /// Builds polyline paths for long edges that were broken into dummy chains during ranking.
    /// Per docs/08 Part B: "walk the EdgeDummyChains side-table and emit one point per dummy
    /// in the chain, in order, as the polyline."
    /// </summary>
    private static IReadOnlyDictionary<string, IReadOnlyList<Vector2>>? BuildEdgeDummyPaths(CompoundGraph compound)
    {
        if (compound.EdgeDummyChains.Count == 0) return null;

        var nodeById = compound.Nodes.ToDictionary(n => n.Id);
        var result = new Dictionary<string, IReadOnlyList<Vector2>>();

        foreach (var (edgeId, chain) in compound.EdgeDummyChains)
        {
            if (chain.Count < 2) continue;
            var points = new List<Vector2>(chain.Count);
            foreach (var nodeId in chain)
            {
                if (!nodeById.TryGetValue(nodeId, out var node)) continue;
                points.Add(new Vector2(node.X, node.Y));
            }
            if (points.Count >= 2)
                result[edgeId] = points;
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// Cluster bounding box = union of every contained real node's rect (transitively
    /// through nested clusters) plus border node positions, padded by GroupLeftPadding/etc.
    /// Per docs/05 §5.4.
    /// </summary>
    private static Rect ComputeClusterBoundingBox(
        LayoutCluster cluster, CompoundGraph compound, LayoutOptions options)
    {
        var descendants = ClusterAncestry.GetClusterAndDescendants(cluster.Id, compound);
        var memberNodes = compound.Nodes
            .Where(n => n.Kind == CompoundNodeKind.Real
                && n.OwningClusterId != null
                && descendants.Contains(n.OwningClusterId))
            .ToList();

        if (memberNodes.Count == 0)
            return new Rect(0, 0, options.GroupWidth, options.GroupTopPadding + options.GroupBottomPadding);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var n in memberNodes)
        {
            if (n.X < minX) minX = n.X;
            if (n.Y < minY) minY = n.Y;
            if (n.X + n.Width > maxX) maxX = n.X + n.Width;
            if (n.Y + n.Height > maxY) maxY = n.Y + n.Height;
        }

        // Pad outward
        minX -= options.GroupLeftPadding;
        minY -= options.GroupTopPadding;
        maxX += options.GroupLeftPadding;
        maxY += options.GroupBottomPadding;

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
