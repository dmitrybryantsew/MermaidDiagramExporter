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

        var edges = new List<GraphEdge>();
        foreach (var e in graph.Edges)
        {
            if (nodeMap.TryGetValue(e.FromNodeId, out var from) &&
                nodeMap.TryGetValue(e.ToNodeId, out var to))
            {
                edges.Add(new GraphEdge
                {
                    FromNode = from,
                    ToNode = to,
                    Kind = e.Kind,
                    Label = e.Label ?? "",
                    IsStrongRelation = e.Kind == Core.TypeEdgeKind.Inheritance || e.Kind == Core.TypeEdgeKind.Implements
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
