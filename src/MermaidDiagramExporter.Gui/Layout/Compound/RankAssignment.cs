using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// Phase 1 of the compound layout engine: assigns ranks to every CompoundNode
/// (real nodes + cluster borders + edge segment dummies) in one unified pass.
/// Per docs/06 Step 3.
/// </summary>
public static class RankAssignment
{
    /// <summary>
    /// Runs baseline initialization, bounded-iteration longest-path relaxation,
    /// rank normalization, and edge-segment dummy insertion.
    /// </summary>
    public static void Run(CompoundGraph compound, LayoutOptions options)
    {
        InitializeBaselineRanks(compound);
        RelaxRanksViaLongestPath(compound, options);
        NormalizeRanks(compound);
        InsertEdgeSegmentDummies(compound);
        UpdateClusterBorderSpans(compound);
    }

    private static void InitializeBaselineRanks(CompoundGraph compound)
    {
        foreach (var node in compound.Nodes) node.Rank = 0;
    }

    /// <summary>
    /// Bounded-iteration longest-path relaxation with high-weight edges processed
    /// first. Includes containment-edge special case (>= / <= instead of strict >).
    /// </summary>
    private static void RelaxRanksViaLongestPath(CompoundGraph compound, LayoutOptions options)
    {
        var nodeById = compound.Nodes.ToDictionary(n => n.Id);
        var orderedEdges = compound.Edges.OrderByDescending(e => e.Weight).ToList();

        int maxIterations = compound.Nodes.Count * 2;
        for (int i = 0; i < maxIterations; i++)
        {
            bool changed = false;
            foreach (var edge in orderedEdges)
            {
                var from = nodeById[edge.FromId];
                var to = nodeById[edge.ToId];

                // Containment edges: parent top border must be <= child top border;
                // parent bottom border must be >= child bottom border (per docs/06 Step 2d).
                if (edge.IsContainment)
                {
                    if (from.Kind == CompoundNodeKind.ClusterBorderTop && from.Rank > to.Rank)
                    {
                        from.Rank = to.Rank;
                        changed = true;
                    }
                    else if (from.Kind == CompoundNodeKind.ClusterBorderBottom && to.Rank < from.Rank)
                    {
                        to.Rank = from.Rank;
                        changed = true;
                    }
                    continue;
                }

                int proposed = from.Rank + edge.MinRankSpan;
                if (proposed > to.Rank)
                {
                    to.Rank = proposed;
                    changed = true;
                }
            }
            if (!changed) break;
        }
    }

    private static void NormalizeRanks(CompoundGraph compound)
    {
        if (compound.Nodes.Count == 0) return;
        int min = int.MaxValue;
        foreach (var n in compound.Nodes) if (n.Rank < min) min = n.Rank;
        if (min == 0) return;
        foreach (var n in compound.Nodes) n.Rank -= min;
    }

    /// <summary>
    /// For every non-containment edge spanning more than 1 rank, insert intermediate
    /// EdgeSegment dummy CompoundNodes chained together. The original edge is replaced
    /// by the dummy chain; the chain is recorded in EdgeDummyChains for later projection.
    /// Per docs/06 Step 3d.
    /// </summary>
    private static void InsertEdgeSegmentDummies(CompoundGraph compound)
    {
        var nodeById = compound.Nodes.ToDictionary(n => n.Id);
        var longEdges = compound.Edges
            .Where(e => !e.IsContainment && Math.Abs(nodeById[e.ToId].Rank - nodeById[e.FromId].Rank) > 1)
            .ToList();

        foreach (var edge in longEdges)
        {
            var from = nodeById[edge.FromId];
            var to = nodeById[edge.ToId];
            int direction = to.Rank > from.Rank ? 1 : -1;
            string previousId = edge.FromId;
            var chain = new List<string> { edge.FromId };
            string? layoutEdgeId = edge.OriginalLayoutEdgeId;

            for (int r = from.Rank + direction; r != to.Rank; r += direction)
            {
                var dummyId = $"edgeseg:{edge.FromId}->{edge.ToId}:{r}";
                compound.Nodes.Add(new CompoundNode
                {
                    Id = dummyId,
                    Kind = CompoundNodeKind.EdgeSegment,
                    OriginalEdgeId = layoutEdgeId,
                    Rank = r,
                    Width = 0,
                    Height = 0,
                    OwningClusterId = ClusterAncestry.FindLowestCommonAncestor(
                        from.OwningClusterId, to.OwningClusterId, compound)
                });
                compound.Edges.Add(new CompoundEdge
                {
                    FromId = previousId,
                    ToId = dummyId,
                    Weight = edge.Weight,
                    MinRankSpan = 1,
                    OriginalLayoutEdgeId = layoutEdgeId
                });
                previousId = dummyId;
                chain.Add(dummyId);
            }
            compound.Edges.Add(new CompoundEdge
            {
                FromId = previousId,
                ToId = edge.ToId,
                Weight = edge.Weight,
                MinRankSpan = 1,
                OriginalLayoutEdgeId = layoutEdgeId
            });
            chain.Add(edge.ToId);

            // Record the dummy chain for later projection
            if (!string.IsNullOrEmpty(layoutEdgeId))
            {
                compound.EdgeDummyChains[layoutEdgeId] = chain;
            }

            // Remove the original long edge from compound.Edges (now represented by chain)
            compound.Edges.Remove(edge);
        }
    }

    /// <summary>
    /// After ranking, compute the min/max rank span for each cluster's border chain.
    /// </summary>
    private static void UpdateClusterBorderSpans(CompoundGraph compound)
    {
        foreach (var (clusterId, chain) in compound.ClusterBorders)
        {
            int min = int.MaxValue, max = int.MinValue;
            foreach (var node in compound.Nodes)
            {
                if (node.OwningClusterId != clusterId) continue;
                if (node.Kind == CompoundNodeKind.ClusterBorderTop)
                    chain.LowBorderByRank[node.Rank] = node.Id;
                else if (node.Kind == CompoundNodeKind.ClusterBorderBottom)
                    chain.HighBorderByRank[node.Rank] = node.Id;
                if (node.Rank < min) min = node.Rank;
                if (node.Rank > max) max = node.Rank;
            }
            chain.MinRank = min == int.MaxValue ? 0 : min;
            chain.MaxRank = max == int.MinValue ? 0 : max;
        }
    }
}
