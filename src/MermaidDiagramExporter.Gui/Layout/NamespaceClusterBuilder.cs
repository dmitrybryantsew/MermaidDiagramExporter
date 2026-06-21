using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Ported from Unity plugin — builds namespace clusters from TypeGraph groups.
/// </summary>
internal static class NamespaceClusterBuilder
{
    public static List<LayoutCluster> Build(Core.TypeGraph graph)
    {
        if (graph.Groups.Count > 0)
        {
            return graph.Groups
                .Select(g => new LayoutCluster
                {
                    Id = g.Id,
                    Label = g.Label,
                    NodeIds = g.NodeIds.ToList()
                })
                .ToList();
        }

        return new List<LayoutCluster>
        {
            new()
            {
                Id = "fallback",
                Label = "Ungrouped",
                NodeIds = graph.Nodes.Select(n => n.Id).ToList()
            }
        };
    }
}
