using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Standalone replacement for Unity's GUIStyle-based measurement.
/// Uses heuristic character-width estimation to approximate text dimensions.
/// </summary>
public sealed class LayoutMeasurementService
{
    private readonly Dictionary<string, Vector2> _nodeSizeCache = new();
    private readonly Dictionary<string, ClusterTitleMetrics> _clusterTitleCache = new();

    private const float TitleCharWidth = 7.2f;   // approx for 12pt bold
    private const float MemberCharWidth = 6.0f;  // approx for 10pt
    private const float BadgeCharWidth = 5.5f;   // approx for 10pt mini

    public Vector2 MeasureNode(LayoutNode node, LayoutOptions options)
    {
        if (node == null)
            return Vector2.zero;

        string cacheKey = node.Id + "|" + node.Label + "|" + node.BadgeText + "|" + string.Join("\n", node.MemberLines);
        if (_nodeSizeCache.TryGetValue(cacheKey, out var cachedSize))
            return cachedSize;

        float badgeWidth = string.IsNullOrEmpty(node.BadgeText)
            ? 0f
            : node.BadgeText.Length * BadgeCharWidth + 12f; // padding

        float paddingHorizontal = 20f;
        float paddingVertical = 16f;
        float memberSectionChrome = node.MemberLines.Count > 0 ? 15f : 0f;
        float badgeAndGapWidth = badgeWidth > 0f ? badgeWidth + 8f : 0f;

        float preferredContentWidth = node.Label.Length * TitleCharWidth + badgeAndGapWidth;
        if (node.MemberLines.Count > 0)
        {
            preferredContentWidth = Mathf.Max(
                preferredContentWidth,
                node.MemberLines.Max(line => line.Length * MemberCharWidth));
        }

        float width = Mathf.Clamp(
            preferredContentWidth + paddingHorizontal,
            options.NodeWidth,
            options.MaxMeasuredNodeWidth);

        float titleWidth = Mathf.Max(96f, width - paddingHorizontal - badgeAndGapWidth);
        // Estimate wrapped title height: chars per line ~ titleWidth / charWidth
        float charsPerLine = Mathf.Max(1f, titleWidth / TitleCharWidth);
        int titleLines = Mathf.Max(1, Mathf.CeilToInt(node.Label.Length / charsPerLine));
        float titleHeight = Mathf.Max(18f, titleLines * 16f);
        float headerHeight = Mathf.Max(titleHeight, badgeWidth > 0f ? 14f : 0f);

        float memberHeight = 0f;
        if (node.MemberLines.Count > 0)
        {
            float memberWidth = Mathf.Max(96f, width - paddingHorizontal);
            float memberCharsPerLine = Mathf.Max(1f, memberWidth / MemberCharWidth);
            memberHeight = node.MemberLines
                .Take(6)
                .Sum(line =>
                {
                    int lines = Mathf.Max(1, Mathf.CeilToInt(line.Length / memberCharsPerLine));
                    return lines * 14f + 2f;
                });
        }

        float height = paddingVertical + headerHeight + memberSectionChrome + memberHeight;
        var measuredSize = new Vector2(width, Mathf.Max(node.EstimatedHeight, height));
        _nodeSizeCache[cacheKey] = measuredSize;
        return measuredSize;
    }

    public ClusterTitleMetrics MeasureClusterTitle(LayoutCluster cluster, LayoutOptions options)
    {
        if (cluster == null)
            return new ClusterTitleMetrics();

        string cacheKey = cluster.Id + "|" + cluster.Label;
        if (_clusterTitleCache.TryGetValue(cacheKey, out var cachedMetrics))
            return CloneClusterTitleMetrics(cachedMetrics);

        float labelWidth = (cluster.Label ?? "").Length * 6.2f; // 11pt bold approx
        var metrics = new ClusterTitleMetrics
        {
            LabelWidth = labelWidth,
            LabelHeight = Mathf.Max(14f, 16f),
            TopMargin = options.ClusterTitleTopMargin,
            BottomMargin = options.ClusterTitleBottomMargin
        };

        _clusterTitleCache[cacheKey] = CloneClusterTitleMetrics(metrics);
        return metrics;
    }

    private static ClusterTitleMetrics CloneClusterTitleMetrics(ClusterTitleMetrics metrics)
    {
        return new ClusterTitleMetrics
        {
            LabelWidth = metrics.LabelWidth,
            LabelHeight = metrics.LabelHeight,
            TopMargin = metrics.TopMargin,
            BottomMargin = metrics.BottomMargin
        };
    }
}
