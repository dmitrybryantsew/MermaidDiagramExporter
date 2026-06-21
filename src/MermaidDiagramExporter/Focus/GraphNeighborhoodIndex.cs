using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Focus;

public sealed class GraphNeighborhoodIndex
{
    public IReadOnlyDictionary<string, TypeNodeData> NodesById { get; private set; } =
        new Dictionary<string, TypeNodeData>();

    public IReadOnlyDictionary<string, List<TypeEdgeData>> OutgoingEdgesByNodeId { get; private set; } =
        new Dictionary<string, List<TypeEdgeData>>();

    public IReadOnlyDictionary<string, List<TypeEdgeData>> IncomingEdgesByNodeId { get; private set; } =
        new Dictionary<string, List<TypeEdgeData>>();

    public IReadOnlyDictionary<string, List<TypeEdgeData>> AssociationEdgesByNodeId { get; private set; } =
        new Dictionary<string, List<TypeEdgeData>>();

    public IReadOnlyDictionary<string, List<string>> GroupIdsByNodeId { get; private set; } =
        new Dictionary<string, List<string>>();

    public static GraphNeighborhoodIndex Build(TypeGraph? graph)
    {
        Dictionary<string, TypeNodeData> nodesById = graph != null
            ? graph.Nodes.ToDictionary(node => node.Id)
            : new Dictionary<string, TypeNodeData>();
        Dictionary<string, List<TypeEdgeData>> outgoing = new();
        Dictionary<string, List<TypeEdgeData>> incoming = new();
        Dictionary<string, List<TypeEdgeData>> associations = new();
        Dictionary<string, List<string>> groupIds = new();

        foreach (TypeNodeData node in nodesById.Values)
        {
            outgoing[node.Id] = new List<TypeEdgeData>();
            incoming[node.Id] = new List<TypeEdgeData>();
            associations[node.Id] = new List<TypeEdgeData>();
            groupIds[node.Id] = new List<string>();
        }

        if (graph != null)
        {
            foreach (TypeEdgeData edge in graph.Edges)
            {
                if (outgoing.TryGetValue(edge.FromNodeId, out List<TypeEdgeData>? outgoingEdges))
                    outgoingEdges.Add(edge);

                if (incoming.TryGetValue(edge.ToNodeId, out List<TypeEdgeData>? incomingEdges))
                    incomingEdges.Add(edge);

                if (edge.Kind == TypeEdgeKind.Association)
                {
                    if (associations.TryGetValue(edge.FromNodeId, out List<TypeEdgeData>? fromAssociations))
                        fromAssociations.Add(edge);

                    if (associations.TryGetValue(edge.ToNodeId, out List<TypeEdgeData>? toAssociations))
                        toAssociations.Add(edge);
                }
            }

            foreach (TypeGroupData group in graph.Groups)
            {
                foreach (string nodeId in group.NodeIds)
                {
                    if (groupIds.TryGetValue(nodeId, out List<string>? ids))
                        ids.Add(group.Id);
                }
            }
        }

        return new GraphNeighborhoodIndex
        {
            NodesById = nodesById,
            OutgoingEdgesByNodeId = outgoing,
            IncomingEdgesByNodeId = incoming,
            AssociationEdgesByNodeId = associations,
            GroupIdsByNodeId = groupIds
        };
    }
}
