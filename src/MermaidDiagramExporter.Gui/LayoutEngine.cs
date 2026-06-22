using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Gui.Layout;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// Bridge between the layout engine and the GraphCanvas.
/// Converts a TypeGraph → LayoutResult → GraphNode/GraphEdge for rendering.
/// </summary>
public class LayoutEngine
{
    private readonly GraphLayoutCoordinator _coordinator = new();

    public (List<GraphNode> nodes, List<GraphEdge> edges) Layout(Core.TypeGraph graph)
    {
        var result = _coordinator.CreateLayout(graph);
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
                Members = members
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
}
