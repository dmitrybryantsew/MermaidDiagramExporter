using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — splits clusters into connected components via BFS.
/// </summary>
internal static class ComponentSplitter
{
    public static List<List<LayoutCluster>> SplitClusters(LayoutGraph graph)
    {
        var clustersById = graph.Clusters.ToDictionary(c => c.Id);
        var adjacency = graph.Clusters.ToDictionary(c => c.Id, _ => new HashSet<string>());
        var clusterByNodeId = graph.Nodes.ToDictionary(n => n.Id, n => n.ClusterId);

        foreach (var edge in graph.Edges)
        {
            if (!clusterByNodeId.TryGetValue(edge.FromNodeId, out var fromId) ||
                !clusterByNodeId.TryGetValue(edge.ToNodeId, out var toId))
                continue;
            if (fromId == toId) continue;
            adjacency[fromId].Add(toId);
            adjacency[toId].Add(fromId);
        }

        var visited = new HashSet<string>();
        var components = new List<List<LayoutCluster>>();

        foreach (var cluster in graph.Clusters)
        {
            if (!visited.Add(cluster.Id)) continue;

            var queue = new Queue<string>();
            var component = new List<LayoutCluster>();
            queue.Enqueue(cluster.Id);

            while (queue.Count > 0)
            {
                var currentId = queue.Dequeue();
                component.Add(clustersById[currentId]);
                foreach (var neighborId in adjacency[currentId])
                {
                    if (visited.Add(neighborId))
                        queue.Enqueue(neighborId);
                }
            }

            components.Add(component);
        }

        return components
            .OrderByDescending(c => c.Sum(cl => cl.NodeIds.Count))
            .ThenBy(c => c.First().Label)
            .ToList();
    }
}
