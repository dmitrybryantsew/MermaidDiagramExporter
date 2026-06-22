using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public static class PostLayoutResultUtility
{
    public static LayoutResult CloneResult(LayoutResult result)
    {
        if (result == null)
            return new LayoutResult();

        return new LayoutResult
        {
            NodeBounds = result.NodeBounds.ToDictionary(pair => pair.Key, pair => pair.Value),
            ClusterBounds = result.ClusterBounds.ToDictionary(pair => pair.Key, pair => pair.Value),
            NodeClusterIds = result.NodeClusterIds.ToDictionary(pair => pair.Key, pair => pair.Value),
            ClusterVisuals = result.ClusterVisuals.ToDictionary(
                pair => pair.Key,
                pair => new LayoutClusterVisual
                {
                    Id = pair.Value.Id,
                    Label = pair.Value.Label,
                    TitleMetrics = LayoutCloneUtility.CloneTitleMetrics(pair.Value.TitleMetrics)
                }),
            EdgePaths = result.EdgePaths
                .Select(path => new LayoutEdgePath
                {
                    EdgeId = path.EdgeId,
                    FromNodeId = path.FromNodeId,
                    ToNodeId = path.ToNodeId,
                    Kind = path.Kind,
                    IsClippedToClusters = path.IsClippedToClusters,
                    Points = path.Points.ToArray()
                })
                .ToList(),
            ContentSize = result.ContentSize
        };
    }

    public static HashSet<string> GetDescendantClusterIds(LayoutGraph graph, string clusterId)
    {
        var descendants = new HashSet<string>();
        CollectDescendants(graph, clusterId, descendants);
        descendants.Remove(clusterId);
        return descendants;
    }

    public static HashSet<string> GetClusterSubtreeIds(LayoutGraph graph, string clusterId)
    {
        var descendants = new HashSet<string>();
        CollectDescendants(graph, clusterId, descendants);
        return descendants;
    }

    public static void ShiftClusterSubtree(
        LayoutGraph graph,
        string clusterId,
        Vector2 delta,
        IDictionary<string, Rect> nodeBounds,
        IDictionary<string, Rect> clusterBounds)
    {
        var subtreeClusterIds = GetClusterSubtreeIds(graph, clusterId);

        foreach (var subtreeClusterId in subtreeClusterIds)
        {
            var subtreeCluster = graph.Clusters.FirstOrDefault(candidate => candidate.Id == subtreeClusterId);
            if (subtreeCluster == null)
                continue;

            if (clusterBounds.TryGetValue(subtreeClusterId, out var clusterRect))
            {
                clusterRect.position += delta;
                clusterBounds[subtreeClusterId] = clusterRect;
            }

            foreach (var nodeId in subtreeCluster.NodeIds)
            {
                if (nodeBounds.TryGetValue(nodeId, out var nodeRect))
                {
                    nodeRect.position += delta;
                    nodeBounds[nodeId] = nodeRect;
                }
            }
        }
    }

    private static void CollectDescendants(LayoutGraph graph, string clusterId, ISet<string> results)
    {
        if (string.IsNullOrEmpty(clusterId) || !results.Add(clusterId))
            return;

        var cluster = graph.Clusters.FirstOrDefault(candidate => candidate.Id == clusterId);
        if (cluster == null)
            return;

        foreach (var childClusterId in cluster.ChildClusterIds)
        {
            CollectDescendants(graph, childClusterId, results);
        }
    }
}
