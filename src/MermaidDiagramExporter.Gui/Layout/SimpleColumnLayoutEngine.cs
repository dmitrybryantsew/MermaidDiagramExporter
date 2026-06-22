using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public sealed class SimpleColumnLayoutEngine : IGraphLayoutEngine
{
    public LayoutResult Run(LayoutGraph graph, LayoutOptions options)
    {
        var nodeBounds = new Dictionary<string, Rect>();
        var clusterBounds = new Dictionary<string, Rect>();

        float x = options.OuterMarginX;
        float maxHeight = 0f;

        foreach (var cluster in graph.Clusters)
        {
            var clusterNodes = cluster.NodeIds
                .Select(nodeId => graph.Nodes.FirstOrDefault(node => node.Id == nodeId))
                .Where(node => node != null)
                .ToList()!;

            float clusterTopPadding = Mathf.Max(options.GroupTopPadding, (cluster.TitleMetrics?.TotalMargin ?? 0f) + 12f);
            float clusterWidth = Mathf.Max(options.GroupWidth, (cluster.TitleMetrics?.LabelWidth ?? 0f) + options.ClusterTitleHorizontalPadding);
            float y = options.OuterMarginY + clusterTopPadding;
            float clusterHeight = clusterTopPadding;

            foreach (var node in clusterNodes)
            {
                var rect = new Rect(
                    x + options.GroupLeftPadding,
                    y,
                    node.Width,
                    node.Height);

                nodeBounds[node.Id] = rect;
                y += node.Height + options.NodeSpacing;
                clusterHeight += node.Height + options.NodeSpacing;
            }

            clusterHeight += options.GroupBottomPadding;
            clusterBounds[cluster.Id] = new Rect(
                x,
                options.OuterMarginY,
                clusterWidth,
                clusterHeight);

            maxHeight = Mathf.Max(maxHeight, clusterHeight);
            x += clusterWidth + options.GroupSpacing;
        }

        return new LayoutResult
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            ContentSize = new Vector2(
                Mathf.Max(options.MinimumContentWidth, x + 400f),
                Mathf.Max(options.MinimumContentHeight, maxHeight + 180f))
        };
    }
}
