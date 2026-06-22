using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

public static class LayoutCloneUtility
{
    public static LayoutGraph CloneGraph(LayoutGraph graph)
    {
        if (graph == null)
            return new LayoutGraph();

        return new LayoutGraph
        {
            Title = graph.Title,
            Nodes = graph.Nodes.Select(CloneNode).ToList(),
            Edges = graph.Edges.Select(CloneEdge).ToList(),
            Clusters = graph.Clusters.Select(CloneCluster).ToList(),
            ExtractedSubgraphs = graph.ExtractedSubgraphs.Select(CloneSubgraph).ToList(),
            Metadata = CloneMetadata(graph.Metadata)
        };
    }

    public static LayoutNode CloneNode(LayoutNode node)
    {
        return new LayoutNode
        {
            Id = node.Id,
            ClusterId = node.ClusterId,
            Label = node.Label,
            Role = node.Role,
            SourceNodeId = node.SourceNodeId,
            BadgeText = node.BadgeText,
            MemberLines = node.MemberLines.ToArray(),
            EstimatedWidth = node.EstimatedWidth,
            EstimatedHeight = node.EstimatedHeight,
            MeasuredWidth = node.MeasuredWidth,
            MeasuredHeight = node.MeasuredHeight,
            IsMeasured = node.IsMeasured,
            Width = node.Width,
            Height = node.Height
        };
    }

    public static LayoutEdge CloneEdge(LayoutEdge edge)
    {
        return new LayoutEdge
        {
            Id = edge.Id,
            OriginalEdgeId = edge.OriginalEdgeId,
            FromNodeId = edge.FromNodeId,
            ToNodeId = edge.ToNodeId,
            Kind = edge.Kind,
            Role = edge.Role
        };
    }

    public static LayoutCluster CloneCluster(LayoutCluster cluster)
    {
        return new LayoutCluster
        {
            Id = cluster.Id,
            Label = cluster.Label,
            Kind = cluster.Kind,
            ParentClusterId = cluster.ParentClusterId,
            NodeIds = cluster.NodeIds.ToArray(),
            ChildClusterIds = cluster.ChildClusterIds.ToArray(),
            HasExternalConnections = cluster.HasExternalConnections,
            RepresentativeNodeId = cluster.RepresentativeNodeId,
            IsExtractedSubgraph = cluster.IsExtractedSubgraph,
            TitleMetrics = CloneTitleMetrics(cluster.TitleMetrics)
        };
    }

    public static LayoutSubgraph CloneSubgraph(LayoutSubgraph subgraph)
    {
        return new LayoutSubgraph
        {
            ClusterId = subgraph.ClusterId,
            Direction = subgraph.Direction,
            Spacing = CloneSpacing(subgraph.Spacing),
            Graph = CloneGraph(subgraph.Graph)
        };
    }

    public static ClusterTitleMetrics CloneTitleMetrics(ClusterTitleMetrics? metrics)
    {
        if (metrics == null)
            return new ClusterTitleMetrics();

        return new ClusterTitleMetrics
        {
            LabelWidth = metrics.LabelWidth,
            LabelHeight = metrics.LabelHeight,
            TopMargin = metrics.TopMargin,
            BottomMargin = metrics.BottomMargin
        };
    }

    public static LayoutSpacingProfile CloneSpacing(LayoutSpacingProfile? spacing)
    {
        if (spacing == null)
            return new LayoutSpacingProfile();

        return new LayoutSpacingProfile
        {
            NodeSeparation = spacing.NodeSeparation,
            RankSeparation = spacing.RankSeparation,
            MarginX = spacing.MarginX,
            MarginY = spacing.MarginY
        };
    }

    public static LayoutGraphMetadata CloneMetadata(LayoutGraphMetadata? metadata)
    {
        if (metadata == null)
            return new LayoutGraphMetadata();

        return new LayoutGraphMetadata
        {
            SourceDescription = metadata.SourceDescription,
            Direction = metadata.Direction,
            UsesMeasuredNodes = metadata.UsesMeasuredNodes,
            Spacing = CloneSpacing(metadata.Spacing)
        };
    }
}
