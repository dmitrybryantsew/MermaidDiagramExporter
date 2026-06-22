using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — hierarchical layered layout with:
/// - Connected component detection
/// - Cluster rank assignment via longest-path
/// - Structured row building with wrapping
/// - Crossing reduction via median/barycenter heuristic
/// </summary>
public sealed class LayeredLayoutEngine : IGraphLayoutEngine
{
    private static readonly CrossingReductionService CrossingReduction = new();

    public LayoutResult Run(LayoutGraph graph, LayoutOptions options)
    {
        var nodeBounds = new Dictionary<string, Rect>();
        var clusterBounds = new Dictionary<string, Rect>();

        var nodeById = graph.Nodes.ToDictionary(n => n.Id);
        var clusterIdByNodeId = graph.Nodes.ToDictionary(n => n.Id, n => n.ClusterId);

        var components = ComponentSplitter.SplitClusters(graph);
        float currentX = options.OuterMarginX;
        float currentY = options.OuterMarginY;
        float rowHeight = 0f;
        float maxContentWidth = options.MinimumContentWidth;

        foreach (var component in components)
        {
            var layout = BuildComponentLayout(component, graph, nodeById, clusterIdByNodeId, options);

            if (currentX > options.OuterMarginX && currentX + layout.Size.X > options.TargetRowWidth)
            {
                currentX = options.OuterMarginX;
                currentY += rowHeight + options.ComponentSpacing;
                rowHeight = 0f;
            }

            OffsetLayout(layout, new Vector2(currentX, currentY), nodeBounds, clusterBounds);
            currentX += layout.Size.X + options.ComponentSpacing;
            rowHeight = Mathf.Max(rowHeight, layout.Size.Y);
            maxContentWidth = Mathf.Max(maxContentWidth, currentX + options.OuterMarginX);
        }

        float totalHeight = currentY + rowHeight + options.OuterMarginY;

        return new LayoutResult
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            ContentSize = new Vector2(
                Mathf.Max(options.MinimumContentWidth, maxContentWidth),
                Mathf.Max(options.MinimumContentHeight, totalHeight))
        };
    }

    private static ComponentLayout BuildComponentLayout(
        IReadOnlyList<LayoutCluster> component,
        LayoutGraph graph,
        IReadOnlyDictionary<string, LayoutNode> nodeById,
        IReadOnlyDictionary<string, string> clusterIdByNodeId,
        LayoutOptions options)
    {
        var metrics = BuildClusterMetrics(component, graph, clusterIdByNodeId);
        var ranks = AssignClusterRanks(component, graph, clusterIdByNodeId, metrics);
        var clustersByRank = GroupClustersByRank(component, ranks, metrics);

        var clusterBounds = new Dictionary<string, Rect>();
        var nodeBounds = new Dictionary<string, Rect>();

        float x = 0f;
        float componentHeight = 0f;

        foreach (var rank in clustersByRank.Keys.OrderBy(v => v))
        {
            var rankClusters = clustersByRank[rank];
            float maxRankWidth = 0f;
            float y = 0f;

            foreach (var cluster in rankClusters)
            {
                var cl = BuildClusterLayout(cluster, graph, nodeById, options);
                clusterBounds[cluster.Id] = new Rect(x, y, cl.Bounds.Width, cl.Bounds.Height);

                foreach (var pair in cl.NodeBounds)
                {
                    var r = pair.Value;
                    nodeBounds[pair.Key] = new Rect(r.X + x, r.Y + y, r.Width, r.Height);
                }

                maxRankWidth = Mathf.Max(maxRankWidth, cl.Bounds.Width);
                y += cl.Bounds.Height + options.ClusterSpacing;
                componentHeight = Mathf.Max(componentHeight, y);
            }

            x += maxRankWidth + options.RankSpacing;
        }

        float componentWidth = clusterBounds.Count > 0
            ? clusterBounds.Values.Max(r => r.xMax)
            : 0f;

        return new ComponentLayout
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            Size = new Vector2(componentWidth, Mathf.Max(0f, componentHeight - options.ClusterSpacing))
        };
    }

    private static Dictionary<string, ClusterMetric> BuildClusterMetrics(
        IReadOnlyList<LayoutCluster> component,
        LayoutGraph graph,
        IReadOnlyDictionary<string, string> clusterIdByNodeId)
    {
        var componentIds = new HashSet<string>(component.Select(c => c.Id));
        var metrics = component.ToDictionary(
            c => c.Id,
            c => new ClusterMetric { Label = c.Label, NodeCount = c.NodeIds.Count });

        foreach (var edge in graph.Edges)
        {
            if (!clusterIdByNodeId.TryGetValue(edge.FromNodeId, out var fromId) ||
                !clusterIdByNodeId.TryGetValue(edge.ToNodeId, out var toId))
                continue;
            if (!componentIds.Contains(fromId) || !componentIds.Contains(toId)) continue;

            float weight = GetEdgeWeight(edge.Kind);
            metrics[fromId].OutWeight += weight;
            metrics[toId].InWeight += weight;
            metrics[fromId].ConnectedIds.Add(toId);
            metrics[toId].ConnectedIds.Add(fromId);
        }

        return metrics;
    }

    private static Dictionary<string, int> AssignClusterRanks(
        IReadOnlyList<LayoutCluster> component,
        LayoutGraph graph,
        IReadOnlyDictionary<string, string> clusterIdByNodeId,
        IReadOnlyDictionary<string, ClusterMetric> metrics)
    {
        var ranks = BuildBaselineRanks(component, metrics);
        var componentIds = new HashSet<string>(component.Select(c => c.Id));

        for (int i = 0; i < component.Count * 2; i++)
        {
            bool changed = false;
            foreach (var edge in graph.Edges.OrderByDescending(e => GetEdgeWeight(e.Kind)))
            {
                if (!clusterIdByNodeId.TryGetValue(edge.FromNodeId, out var fromId) ||
                    !clusterIdByNodeId.TryGetValue(edge.ToNodeId, out var toId))
                    continue;
                if (fromId == toId || !componentIds.Contains(fromId) || !componentIds.Contains(toId))
                    continue;

                int delta = edge.Kind == Core.TypeEdgeKind.Association ? 0 : 1;
                int proposed = ranks[fromId] + delta;
                if (proposed > ranks[toId])
                {
                    ranks[toId] = proposed;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        int minRank = ranks.Values.Min();
        if (minRank != 0)
            foreach (var id in component.Select(c => c.Id).ToList())
                ranks[id] -= minRank;

        return ranks;
    }

    private static Dictionary<string, int> BuildBaselineRanks(
        IReadOnlyList<LayoutCluster> component,
        IReadOnlyDictionary<string, ClusterMetric> metrics)
    {
        var ordered = component
            .OrderBy(c => metrics[c.Id].InWeight - metrics[c.Id].OutWeight)
            .ThenByDescending(c => metrics[c.Id].ConnectedIds.Count)
            .ThenBy(c => c.Label)
            .ToList();

        int targetRanks = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(component.Count)));
        int perRank = Mathf.Max(1, Mathf.CeilToInt(component.Count / (float)targetRanks));
        var ranks = new Dictionary<string, int>();
        for (int i = 0; i < ordered.Count; i++)
            ranks[ordered[i].Id] = i / perRank;
        return ranks;
    }

    private static Dictionary<int, List<LayoutCluster>> GroupClustersByRank(
        IReadOnlyList<LayoutCluster> component,
        IReadOnlyDictionary<string, int> ranks,
        IReadOnlyDictionary<string, ClusterMetric> metrics)
    {
        return component
            .GroupBy(c => ranks[c.Id])
            .ToDictionary(
                g => g.Key,
                g => g
                    .OrderByDescending(c => metrics[c.Id].ConnectedIds.Count)
                    .ThenByDescending(c => metrics[c.Id].OutWeight + metrics[c.Id].InWeight)
                    .ThenBy(c => c.Label)
                    .ToList());
    }

    private static ClusterLayout BuildClusterLayout(
        LayoutCluster cluster,
        LayoutGraph graph,
        IReadOnlyDictionary<string, LayoutNode> nodeById,
        LayoutOptions options)
    {
        var nodes = cluster.NodeIds
            .Select(id => nodeById.TryGetValue(id, out var n) ? n : null)
            .OfType<LayoutNode>()
            .ToList();

        var localRanks = AssignLocalNodeRanks(nodes, graph);
        var orderedNodes = nodes
            .OrderBy(n => localRanks[n.Id])
            .ThenByDescending(n => n.Height)
            .ThenBy(n => n.Id)
            .ToList();

        var coreNodes = orderedNodes.Where(n => n.Role == LayoutNodeRole.Real).ToList();

        float clusterTopPadding = options.GroupTopPadding;
        float minimumClusterWidth = options.GroupWidth;

        var coreLayout = BuildStructuredCoreLayout(coreNodes, graph, options);

        var nodeBounds = new Dictionary<string, Rect>();
        float currentX = options.GroupLeftPadding;
        float contentHeight = Mathf.Max(0f, coreLayout.Height);

        foreach (var pair in coreLayout.NodeBounds)
        {
            var r = pair.Value;
            nodeBounds[pair.Key] = new Rect(r.X + currentX, r.Y + clusterTopPadding, r.Width, r.Height);
        }

        currentX += coreLayout.Width;

        float clusterWidth = Mathf.Max(minimumClusterWidth, currentX + options.GroupLeftPadding);
        float clusterHeight = Mathf.Max(
            clusterTopPadding + options.GroupBottomPadding + 24f,
            contentHeight + clusterTopPadding + options.GroupBottomPadding);

        return new ClusterLayout
        {
            Bounds = new Rect(0, 0, clusterWidth, clusterHeight),
            NodeBounds = nodeBounds
        };
    }

    private static StructuredCoreLayout BuildStructuredCoreLayout(
        IReadOnlyList<LayoutNode> coreNodes,
        LayoutGraph graph,
        LayoutOptions options)
    {
        if (coreNodes.Count == 0) return new StructuredCoreLayout();

        var ranks = AssignLocalNodeRanks(coreNodes, graph);
        NormalizeRanks(ranks);
        ApplyWeakAssociationRankSpread(coreNodes, graph, ranks);
        NormalizeRanks(ranks);

        var structuredRows = BuildStructuredRows(coreNodes, ranks, graph, options);

        var nodeBounds = new Dictionary<string, Rect>();
        var rowWidths = new Dictionary<int, float>();
        var rowNodeIds = new Dictionary<int, List<string>>();

        float currentY = 0f;
        float maxContentWidth = 0f;
        int previousRank = int.MinValue;

        for (int rowIndex = 0; rowIndex < structuredRows.Count; rowIndex++)
        {
            var row = structuredRows[rowIndex];
            if (rowIndex > 0)
            {
                currentY += row.Rank == previousRank
                    ? options.StructuredWrappedRowGap
                    : options.StructuredRankGap;
            }

            float currentX = 0f;
            float rowHeight = 0f;
            rowNodeIds[rowIndex] = new List<string>();

            foreach (var node in row.Nodes)
            {
                nodeBounds[node.Id] = new Rect(currentX, currentY, node.Width, node.Height);
                rowNodeIds[rowIndex].Add(node.Id);
                currentX += node.Width + options.StructuredNodeColumnSpacing;
                rowHeight = Mathf.Max(rowHeight, node.Height);
            }

            float rowWidth = row.Nodes.Count > 0 ? currentX - options.StructuredNodeColumnSpacing : 0f;
            rowWidths[rowIndex] = rowWidth;
            maxContentWidth = Mathf.Max(maxContentWidth, rowWidth);
            currentY += rowHeight;
            previousRank = row.Rank;
        }

        OffsetStructuredRows(nodeBounds, rowNodeIds, rowWidths, maxContentWidth, options);

        return new StructuredCoreLayout
        {
            NodeBounds = nodeBounds,
            Width = maxContentWidth,
            Height = Mathf.Max(0f, currentY)
        };
    }

    private static List<StructuredRow> BuildStructuredRows(
        IReadOnlyList<LayoutNode> nodes,
        IReadOnlyDictionary<string, int> ranks,
        LayoutGraph graph,
        LayoutOptions options)
    {
        var structuredRows = nodes
            .GroupBy(n => ranks[n.Id])
            .OrderBy(g => g.Key)
            .Select(g => new StructuredRow
            {
                Rank = g.Key,
                Nodes = g
                    .OrderByDescending(n => GetNodeConnectivity(n.Id, ranks.Keys, graph))
                    .ThenBy(n => n.Label)
                    .ThenBy(n => n.Id)
                    .ToList()
            })
            .ToList();

        RefineStructuredRows(structuredRows, graph);

        var wrappedRows = new List<StructuredRow>();
        foreach (var row in structuredRows)
        {
            foreach (var wrapped in WrapStructuredRow(row.Nodes, options))
            {
                wrappedRows.Add(new StructuredRow { Rank = row.Rank, Nodes = wrapped });
            }
        }

        RefineStructuredRows(wrappedRows, graph);
        return wrappedRows;
    }

    private static void RefineStructuredRows(IList<StructuredRow> rows, LayoutGraph graph)
    {
        var rowLists = rows.Select(r => r.Nodes).ToList();
        CrossingReduction.RefineRows(rowLists, graph);
        for (int i = 0; i < rows.Count; i++)
            rows[i].Nodes = rowLists[i];
    }

    private static Dictionary<string, int> AssignLocalNodeRanks(IReadOnlyList<LayoutNode> nodes, LayoutGraph graph)
    {
        var nodeById = nodes.ToDictionary(n => n.Id);
        var ranks = nodes.ToDictionary(n => n.Id, n => GetInitialLocalRank(n));
        var nodeIds = new HashSet<string>(nodes.Select(n => n.Id));

        for (int i = 0; i < nodes.Count * 2; i++)
        {
            bool changed = false;
            foreach (var edge in graph.Edges.OrderByDescending(e => GetEdgeWeight(e.Kind)))
            {
                if (!nodeIds.Contains(edge.FromNodeId) || !nodeIds.Contains(edge.ToNodeId) || edge.FromNodeId == edge.ToNodeId)
                    continue;

                int delta = GetLocalRankDelta(nodeById[edge.FromNodeId], nodeById[edge.ToNodeId], edge);
                int proposed = ranks[edge.FromNodeId] + delta;
                if (proposed > ranks[edge.ToNodeId])
                {
                    ranks[edge.ToNodeId] = proposed;
                    changed = true;
                }
            }
            if (!changed) break;
        }

        return ranks;
    }

    private static void ApplyWeakAssociationRankSpread(
        IReadOnlyList<LayoutNode> nodes,
        LayoutGraph graph,
        IDictionary<string, int> ranks)
    {
        var realNodes = nodes.Where(n => n.Role == LayoutNodeRole.Real).ToList();
        if (realNodes.Count < 6 || ranks.Count == 0) return;

        int currentSpan = ranks.Values.Max() - ranks.Values.Min();
        if (currentSpan >= 2) return;

        var nodeIds = new HashSet<string>(realNodes.Select(n => n.Id));
        var adjacency = realNodes.ToDictionary(n => n.Id, _ => new HashSet<string>());

        foreach (var edge in graph.Edges)
        {
            if (edge.Role != LayoutEdgeRole.Direct || edge.FromNodeId == edge.ToNodeId) continue;
            if (!nodeIds.Contains(edge.FromNodeId) || !nodeIds.Contains(edge.ToNodeId)) continue;
            adjacency[edge.FromNodeId].Add(edge.ToNodeId);
            adjacency[edge.ToNodeId].Add(edge.FromNodeId);
        }

        var seed = realNodes
            .OrderByDescending(n => adjacency[n.Id].Count)
            .ThenBy(n => n.Label)
            .ThenBy(n => n.Id)
            .FirstOrDefault();

        if (seed == null || adjacency[seed.Id].Count == 0) return;

        var distances = new Dictionary<string, int> { [seed.Id] = 0 };
        var queue = new Queue<string>();
        queue.Enqueue(seed.Id);

        while (queue.Count > 0)
        {
            var currentId = queue.Dequeue();
            int nextDist = distances[currentId] + 1;
            foreach (var neighborId in adjacency[currentId])
            {
                if (distances.ContainsKey(neighborId)) continue;
                distances[neighborId] = nextDist;
                queue.Enqueue(neighborId);
            }
        }

        if (distances.Count == 0) return;
        int maxDist = distances.Values.Max();
        if (maxDist <= 0) return;

        int targetSpan = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(realNodes.Count)), 2, 4) - 1;
        foreach (var node in realNodes)
        {
            if (!distances.TryGetValue(node.Id, out int dist)) continue;
            int scaledRank = Mathf.RoundToInt((dist / (float)maxDist) * targetSpan);
            ranks[node.Id] = Mathf.Max(ranks[node.Id], scaledRank);
        }
    }

    private static void NormalizeRanks(IDictionary<string, int> ranks)
    {
        if (ranks.Count == 0) return;
        int min = ranks.Values.Min();
        if (min == 0) return;
        foreach (var id in ranks.Keys.ToList()) ranks[id] -= min;
    }

    private static List<List<LayoutNode>> WrapStructuredRow(IReadOnlyList<LayoutNode> row, LayoutOptions options)
    {
        var result = new List<List<LayoutNode>>();
        var current = new List<LayoutNode>();
        float currentWidth = 0f;

        foreach (var node in row)
        {
            float nodeWidth = node.Width + (current.Count > 0 ? options.StructuredNodeColumnSpacing : 0f);
            bool exceedsWidth = current.Count > 0 && currentWidth + nodeWidth > options.StructuredClusterMaxRowWidth;
            bool exceedsCount = current.Count >= options.StructuredClusterMaxNodesPerRow;

            if (exceedsWidth || exceedsCount)
            {
                result.Add(current);
                current = new List<LayoutNode>();
                currentWidth = 0f;
                nodeWidth = node.Width;
            }

            current.Add(node);
            currentWidth += nodeWidth;
        }

        if (current.Count > 0) result.Add(current);
        return result;
    }

    private static int GetNodeConnectivity(string nodeId, IEnumerable<string> ids, LayoutGraph graph)
    {
        var idSet = new HashSet<string>(ids);
        int count = 0;
        foreach (var edge in graph.Edges)
        {
            if (edge.Role != LayoutEdgeRole.Direct) continue;
            if (edge.FromNodeId == nodeId && idSet.Contains(edge.ToNodeId)) count++;
            else if (edge.ToNodeId == nodeId && idSet.Contains(edge.FromNodeId)) count++;
        }
        return count;
    }

    private static void OffsetStructuredRows(
        IDictionary<string, Rect> nodeBounds,
        IReadOnlyDictionary<int, List<string>> rowNodeIds,
        IReadOnlyDictionary<int, float> rowWidths,
        float contentWidth,
        LayoutOptions options)
    {
        foreach (var row in rowNodeIds)
        {
            if (!rowWidths.TryGetValue(row.Key, out float rowWidth)) continue;
            float slack = Mathf.Max(0f, contentWidth - rowWidth);
            float offsetX = Mathf.Min(
                options.StructuredRowMaxIndent,
                (slack * options.StructuredRowCenteringBias) + (row.Key * options.StructuredRowIndentStep));
            foreach (var nodeId in row.Value)
            {
                var r = nodeBounds[nodeId];
                nodeBounds[nodeId] = new Rect(r.X + offsetX, r.Y, r.Width, r.Height);
            }
        }
    }

    private static float GetEdgeWeight(Core.TypeEdgeKind kind) => kind switch
    {
        Core.TypeEdgeKind.Inheritance => 3f,
        Core.TypeEdgeKind.Implements => 2.5f,
        _ => 1f
    };

    private static int GetInitialLocalRank(LayoutNode node) => node.Role switch
    {
        LayoutNodeRole.ClusterInboundAnchor => 0,
        LayoutNodeRole.ClusterOutboundAnchor => 2,
        LayoutNodeRole.SelfLoopHelper => 2,
        _ => 1
    };

    private static int GetLocalRankDelta(LayoutNode from, LayoutNode to, LayoutEdge edge)
    {
        if (from.Role == LayoutNodeRole.ClusterInboundAnchor && to.Role == LayoutNodeRole.Real) return 1;
        if (from.Role == LayoutNodeRole.Real && to.Role == LayoutNodeRole.ClusterOutboundAnchor) return 1;
        return edge.Kind == Core.TypeEdgeKind.Association ? 0 : 1;
    }

    private static void OffsetLayout(
        ComponentLayout layout,
        Vector2 offset,
        IDictionary<string, Rect> nodeBounds,
        IDictionary<string, Rect> clusterBounds)
    {
        foreach (var pair in layout.ClusterBounds)
        {
            var r = pair.Value;
            clusterBounds[pair.Key] = new Rect(r.X + offset.X, r.Y + offset.Y, r.Width, r.Height);
        }

        foreach (var pair in layout.NodeBounds)
        {
            var r = pair.Value;
            nodeBounds[pair.Key] = new Rect(r.X + offset.X, r.Y + offset.Y, r.Width, r.Height);
        }
    }

    private sealed class ClusterMetric
    {
        public string Label = "";
        public int NodeCount;
        public float InWeight;
        public float OutWeight;
        public HashSet<string> ConnectedIds = new();
    }

    private sealed class ClusterLayout
    {
        public Rect Bounds;
        public Dictionary<string, Rect> NodeBounds = new();
    }

    private sealed class ComponentLayout
    {
        public Dictionary<string, Rect> NodeBounds = new();
        public Dictionary<string, Rect> ClusterBounds = new();
        public Vector2 Size;
    }

    private sealed class StructuredCoreLayout
    {
        public Dictionary<string, Rect> NodeBounds = new();
        public float Width;
        public float Height;
    }

    private sealed class StructuredRow
    {
        public int Rank;
        public List<LayoutNode> Nodes = new();
    }
}
