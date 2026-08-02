using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui.Persistence;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Bridge between the layout engine and the GraphCanvas.
/// Converts a TypeGraph → LayoutResult → GraphNode/GraphEdge for rendering.
/// </summary>
public class LayoutEngine
{
    private readonly GraphLayoutCoordinator _coordinator = new();

    /// <summary>
    /// Manual position overrides to apply after layout. Set before calling Layout().
    /// </summary>
    public ManualLayoutOverrides? ManualOverrides { get; set; }

    /// <summary>
    /// Layout options controlling spacing, padding, and sizing.
    /// </summary>
    public LayoutOptions? LayoutOptions { get; set; }

    public (List<GraphNode> nodes, List<GraphEdge> edges) Layout(Core.TypeGraph graph)
    {
        var options = LayoutOptions ?? new LayoutOptions();
        var result = _coordinator.CreateLayout(graph, options);

        // Apply manual overrides if present
        if (ManualOverrides != null && ManualOverrides.HasOverrides)
        {
            result = ManualLayoutApplier.ApplyOverrides(result, ManualOverrides, options);
        }

        return ConvertToGraphNodes(graph, result);
    }

    /// <summary>
    /// Converts a pre-built <see cref="LayoutResult"/> (with positions
    /// already determined by the caller) into renderable GraphNodes/GraphEdges.
    /// Used by Design Mode where positions are authoritative on
    /// <see cref="MermaidDiagramExporter.Gui.Design.DesignClass"/> and must
    /// NOT be recomputed by the layout algorithm. Per docs/design/09 BUG-1.
    /// </summary>
    public (List<GraphNode> nodes, List<GraphEdge> edges) LayoutFromLayoutResult(Core.TypeGraph graph, LayoutResult result)
        => ConvertToGraphNodes(graph, result);

    /// <summary>
    /// Re-routes edges for an existing LayoutResult without re-running the
    /// layout engine. Used in Design Mode to get proper edge paths for
    /// manually-positioned nodes.
    /// </summary>
    public IReadOnlyList<LayoutEdgePath> RouteEdges(Core.TypeGraph graph, LayoutResult result, LayoutOptions? options = null)
        => _coordinator.RouteEdges(graph, result, options ?? LayoutOptions ?? new LayoutOptions());

    /// <summary>
    /// Re-routes edges using current node positions without re-running the
    /// layout engine. Updates <see cref="GraphEdge.Points"/> in place.
    /// Used after manual node moves (drag) to refresh orthogonal/cluster-aware
    /// edge paths. Cheaper than a full re-layout.
    /// </summary>
    public void RedrawEdges(Core.TypeGraph? graph, List<GraphNode> nodes, List<GraphEdge> edges, LayoutOptions? options = null)
    {
        if (graph == null || nodes.Count == 0) return;
        var opts = options ?? LayoutOptions ?? new LayoutOptions();

        // Build a LayoutResult from current node positions
        var nodeBounds = new Dictionary<string, Layout.Rect>();
        var nodeClusterIds = new Dictionary<string, string>();
        var clusterBounds = new Dictionary<string, Layout.Rect>();

        foreach (var n in nodes)
        {
            nodeBounds[n.Id] = new Layout.Rect(n.X, n.Y, n.Width, n.Height);
            var ns = n.Namespace ?? "";
            if (!string.IsNullOrEmpty(ns))
                nodeClusterIds[n.Id] = ns;
        }

        // Compute cluster bounds from namespace groups (same logic as DrawNamespaceGroups)
        var byNamespace = nodes
            .Where(n => !string.IsNullOrEmpty(n.Namespace))
            .GroupBy(n => n.Namespace!);
        const float clusterPadding = 24f;
        const float clusterTitleHeight = 24f;
        foreach (var nsGroup in byNamespace)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            foreach (var n in nsGroup)
            {
                if (n.X < minX) minX = n.X;
                if (n.Y < minY) minY = n.Y;
                if (n.X + n.Width > maxX) maxX = n.X + n.Width;
                if (n.Y + n.Height > maxY) maxY = n.Y + n.Height;
            }
            clusterBounds[nsGroup.Key] = new Layout.Rect(
                minX - clusterPadding, minY - clusterPadding - clusterTitleHeight,
                (maxX - minX) + clusterPadding * 2f,
                (maxY - minY) + clusterPadding * 2f + clusterTitleHeight);
        }

        var result = new LayoutResult
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            NodeClusterIds = nodeClusterIds,
        };

        var edgePaths = _coordinator.RouteEdges(graph, result, opts);
        var pointsByEdgeId = edgePaths
            .Where(p => !string.IsNullOrEmpty(p.EdgeId))
            .ToDictionary(p => p.EdgeId, p => p.Points);

        foreach (var e in edges)
        {
            var key = Core.TypeEdgeData.CreateEdgeId(e.FromNode?.Id ?? "", e.ToNode?.Id ?? "", e.Kind);
            if (pointsByEdgeId.TryGetValue(key, out var pts))
                e.Points = pts;
            else
                e.Points = System.Array.Empty<Vector2>();
        }
    }

    private (List<GraphNode> nodes, List<GraphEdge> edges) ConvertToGraphNodes(Core.TypeGraph graph, LayoutResult result)
    {
        var nodeMap = new Dictionary<string, GraphNode>();

        foreach (var nd in graph.Nodes)
        {
            if (!result.NodeBounds.TryGetValue(nd.Id, out var bounds)) continue;

            var members = nd.Members
                .Take(8)
                .Select(m => new GraphMember
                {
                    Name = m.Name,
                    TypeName = m.TypeName ?? "void",
                    Kind = m.Kind.ToString()
                })
                .ToList();

            var node = new GraphNode
            {
                Id = nd.Id,
                DisplayName = nd.DisplayName,
                Namespace = nd.Namespace ?? "",
                AssetPath = nd.AssetPath ?? "",
                Kind = nd.Kind.ToString(),
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                Members = members,
                StereotypeBadges = BuildStereotypeBadges(nd)
            };
            nodeMap[nd.Id] = node;
        }

        var edgePointsByEdgeId = result.EdgePaths
            .Where(p => !string.IsNullOrEmpty(p.EdgeId))
            .ToDictionary(p => p.EdgeId, p => p.Points);

        var edges = new List<GraphEdge>();
        foreach (var e in graph.Edges)
        {
            if (nodeMap.TryGetValue(e.FromNodeId, out var from) &&
                nodeMap.TryGetValue(e.ToNodeId, out var to))
            {
                edgePointsByEdgeId.TryGetValue(Core.TypeEdgeData.CreateEdgeId(e.FromNodeId, e.ToNodeId, e.Kind), out var points);
                edges.Add(new GraphEdge
                {
                    FromNode = from,
                    ToNode = to,
                    Kind = e.Kind,
                    Label = e.Label ?? "",
                    IsStrongRelation = e.Kind == Core.TypeEdgeKind.Inheritance || e.Kind == Core.TypeEdgeKind.Implements,
                    Points = points ?? System.Array.Empty<Vector2>()
                });
            }
        }

        return (nodeMap.Values.ToList(), edges);
    }

    private static List<GraphStereotypeBadge> BuildStereotypeBadges(TypeNodeData nd)
    {
        var badges = new List<GraphStereotypeBadge>();
        foreach (var stereotype in nd.Stereotypes)
        {
            string color = stereotype switch
            {
                "mono-behaviour" => "#4CAF50",
                "scriptable-object" => "#FF9800",
                "component" => "#2196F3",
                _ => "#9E9E9E" // default gray for unknown/custom
            };
            badges.Add(new GraphStereotypeBadge { Label = stereotype, ColorHex = color });
        }
        return badges;
    }
}
