using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Focus;

public sealed class FocusedSubgraphBuilder
{
    public TypeGraph? BuildFocusedGraph(TypeGraph? sourceGraph, GraphFocusRequest? request)
    {
        if (sourceGraph == null || request == null || request.SeedNodeIds == null || request.SeedNodeIds.Count == 0)
            return sourceGraph;

        GraphNeighborhoodIndex index = GraphNeighborhoodIndex.Build(sourceGraph);
        HashSet<string> focusedNodeIds = CollectFocusedNodeIds(index, request);

        List<TypeNodeData> nodes = sourceGraph.Nodes
            .Where(node => focusedNodeIds.Contains(node.Id))
            .ToList();

        List<TypeEdgeData> edges = FilterEdges(sourceGraph, focusedNodeIds, request);
        List<TypeGroupData> groups = FilterGroups(sourceGraph, focusedNodeIds);

        string title = BuildFocusedTitle(nodes, request);
        TypeGraphMetadata metadata = BuildFocusedMetadata(sourceGraph, request, nodes.Count);

        return new TypeGraph(title, nodes, edges, groups, metadata);
    }

    private static HashSet<string> CollectFocusedNodeIds(
        GraphNeighborhoodIndex index,
        GraphFocusRequest request)
    {
        int depthLimit = Math.Max(0, request.AssociationDepth);
        HashSet<string> focused = new(request.SeedNodeIds.Where(index.NodesById.ContainsKey));
        Queue<GraphTraversalStep> queue = new(focused.Select(nodeId => new GraphTraversalStep(nodeId, 0)));

        while (queue.Count > 0)
        {
            GraphTraversalStep step = queue.Dequeue();
            if (step.Depth >= depthLimit)
                continue;

            IReadOnlyList<TypeEdgeData> relationEdges = GetTraversalEdges(index, step.NodeId, request);
            foreach (TypeEdgeData edge in relationEdges)
            {
                string neighborId = edge.FromNodeId == step.NodeId ? edge.ToNodeId : edge.FromNodeId;
                if (!index.NodesById.ContainsKey(neighborId) || !focused.Add(neighborId))
                    continue;

                queue.Enqueue(new GraphTraversalStep(neighborId, step.Depth + 1));
            }
        }

        return focused;
    }

    private static IReadOnlyList<TypeEdgeData> GetTraversalEdges(
        GraphNeighborhoodIndex index,
        string nodeId,
        GraphFocusRequest request)
    {
        return request.TraversalMode switch
        {
            GraphFocusTraversalMode.OutgoingAssociationsOnly =>
                index.OutgoingEdgesByNodeId.TryGetValue(nodeId, out List<TypeEdgeData>? outgoingAssociations)
                    ? outgoingAssociations
                        .Where(edge => edge.Kind == TypeEdgeKind.Association && request.IncludeOutgoingAssociations)
                        .ToArray()
                    : Array.Empty<TypeEdgeData>(),

            GraphFocusTraversalMode.IncomingAssociationsOnly =>
                index.IncomingEdgesByNodeId.TryGetValue(nodeId, out List<TypeEdgeData>? incomingAssociations)
                    ? incomingAssociations
                        .Where(edge => edge.Kind == TypeEdgeKind.Association && request.IncludeIncomingAssociations)
                        .ToArray()
                    : Array.Empty<TypeEdgeData>(),

            GraphFocusTraversalMode.AllVisibleRelations =>
                BuildAllVisibleTraversalEdges(index, nodeId, request),

            _ =>
                index.AssociationEdgesByNodeId.TryGetValue(nodeId, out List<TypeEdgeData>? relationEdges)
                    ? relationEdges
                    : Array.Empty<TypeEdgeData>()
        };
    }

    private static IReadOnlyList<TypeEdgeData> BuildAllVisibleTraversalEdges(
        GraphNeighborhoodIndex index,
        string nodeId,
        GraphFocusRequest request)
    {
        IEnumerable<TypeEdgeData> outgoing = index.OutgoingEdgesByNodeId.TryGetValue(nodeId, out List<TypeEdgeData>? outgoingEdges)
            ? outgoingEdges
            : Array.Empty<TypeEdgeData>();
        IEnumerable<TypeEdgeData> incoming = index.IncomingEdgesByNodeId.TryGetValue(nodeId, out List<TypeEdgeData>? incomingEdges)
            ? incomingEdges
            : Array.Empty<TypeEdgeData>();

        return outgoing
            .Concat(incoming)
            .Where(edge => ShouldTraverseEdge(edge, request, nodeId))
            .Distinct()
            .ToArray();
    }

    private static bool ShouldTraverseEdge(TypeEdgeData edge, GraphFocusRequest request, string currentNodeId)
    {
        return edge.Kind switch
        {
            TypeEdgeKind.Association when edge.FromNodeId == currentNodeId => request.IncludeOutgoingAssociations,
            TypeEdgeKind.Association when edge.ToNodeId == currentNodeId => request.IncludeIncomingAssociations,
            TypeEdgeKind.Association => true,
            TypeEdgeKind.Inheritance => request.IncludeInheritanceInsideFocusedSet,
            TypeEdgeKind.Implements => request.IncludeImplementsInsideFocusedSet,
            _ => false
        };
    }

    private static List<TypeEdgeData> FilterEdges(
        TypeGraph sourceGraph,
        IReadOnlySet<string> focusedNodeIds,
        GraphFocusRequest request)
    {
        return sourceGraph.Edges
            .Where(edge => focusedNodeIds.Contains(edge.FromNodeId) && focusedNodeIds.Contains(edge.ToNodeId))
            .Where(edge => ShouldIncludeEdge(edge, request))
            .ToList();
    }

    private static bool ShouldIncludeEdge(TypeEdgeData edge, GraphFocusRequest request)
    {
        return edge.Kind switch
        {
            TypeEdgeKind.Association => true,
            TypeEdgeKind.Inheritance => request.IncludeInheritanceInsideFocusedSet,
            TypeEdgeKind.Implements => request.IncludeImplementsInsideFocusedSet,
            _ => false
        };
    }

    private static List<TypeGroupData> FilterGroups(
        TypeGraph sourceGraph,
        IReadOnlySet<string> focusedNodeIds)
    {
        return sourceGraph.Groups
            .Select(group => new TypeGroupData
            {
                Id = group.Id,
                Label = group.Label,
                Kind = group.Kind,
                ParentGroupId = group.ParentGroupId,
                NodeIds = group.NodeIds.Where(focusedNodeIds.Contains).ToArray()
            })
            .Where(group => group.NodeIds.Count > 0)
            .ToList();
    }

    private static string BuildFocusedTitle(IReadOnlyList<TypeNodeData> nodes, GraphFocusRequest request)
    {
        List<TypeNodeData> seedNodes = nodes
            .Where(node => request.SeedNodeIds.Contains(node.Id))
            .ToList();

        if (seedNodes.Count == 1)
            return "Focused: " + seedNodes[0].DisplayName + " (depth " + request.AssociationDepth + ")";

        if (seedNodes.Count > 1)
            return "Focused: " + seedNodes[0].DisplayName + " + " + (seedNodes.Count - 1) + " more (depth " + request.AssociationDepth + ")";

        return "Focused View (depth " + request.AssociationDepth + ")";
    }

    private static TypeGraphMetadata BuildFocusedMetadata(TypeGraph sourceGraph, GraphFocusRequest request, int nodeCount)
    {
        IReadOnlyList<string> seedNodeIds = request.SeedNodeIds?.ToArray() ?? Array.Empty<string>();
        string focusSummary = BuildFocusSummary(sourceGraph, request);

        return new TypeGraphMetadata
        {
            GeneratedAtUtc = sourceGraph.Metadata.GeneratedAtUtc,
            SourceKind = sourceGraph.Metadata.SourceKind,
            Options = sourceGraph.Metadata.Options,
            SourceDescription = sourceGraph.Metadata.SourceDescription
                + " | Focused depth " + request.AssociationDepth
                + " | Nodes " + nodeCount,
            IsDerivedView = true,
            ParentGraphTitle = sourceGraph.Title,
            FocusSummary = focusSummary,
            SeedNodeIds = seedNodeIds,
            FocusDepth = request.AssociationDepth
        };
    }

    private static string BuildFocusSummary(TypeGraph sourceGraph, GraphFocusRequest request)
    {
        Dictionary<string, TypeNodeData> nodesById = sourceGraph.Nodes.ToDictionary(node => node.Id);
        List<string> seedLabels = request.SeedNodeIds
            .Where(nodesById.ContainsKey)
            .Select(nodeId => nodesById[nodeId].DisplayName)
            .ToList();

        if (seedLabels.Count == 1)
            return "Focused around " + seedLabels[0] + " with " + BuildTraversalModeLabel(request.TraversalMode) + " depth " + request.AssociationDepth + ".";

        if (seedLabels.Count > 1)
            return "Focused around " + seedLabels[0] + " and " + (seedLabels.Count - 1) + " more seed nodes with " + BuildTraversalModeLabel(request.TraversalMode) + " depth " + request.AssociationDepth + ".";

        return "Focused view with " + BuildTraversalModeLabel(request.TraversalMode) + " depth " + request.AssociationDepth + ".";
    }

    private static string BuildTraversalModeLabel(GraphFocusTraversalMode traversalMode)
    {
        return traversalMode switch
        {
            GraphFocusTraversalMode.OutgoingAssociationsOnly => "outgoing association",
            GraphFocusTraversalMode.IncomingAssociationsOnly => "incoming association",
            GraphFocusTraversalMode.AllVisibleRelations => "all-visible relation",
            _ => "association"
        };
    }

    private readonly struct GraphTraversalStep
    {
        public GraphTraversalStep(string nodeId, int depth)
        {
            NodeId = nodeId;
            Depth = depth;
        }

        public string NodeId { get; }

        public int Depth { get; }
    }
}
