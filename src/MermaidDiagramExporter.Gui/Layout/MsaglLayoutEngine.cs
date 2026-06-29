using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Msagl.Core.Geometry;
using Microsoft.Msagl.Core.Geometry.Curves;
using Microsoft.Msagl.Core.Layout;
using Microsoft.Msagl.Core.Routing;
using Microsoft.Msagl.Layout.Layered;
using Microsoft.Msagl.Miscellaneous;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Prototype layout engine backed by Microsoft Automatic Graph Layout (MSAGL).
/// Uses MSAGL's SugiyamaLayoutSettings (same algorithm family as Mermaid's dagre)
/// with native cluster containment and rectilinear edge routing.
///
/// Maps our LayoutGraph → MSAGL GeometryGraph, runs layout, reads coordinates
/// back into our LayoutResult. Y is flipped from MSAGL's Y-up to our Y-down.
/// Edge routing is deferred to EdgeRoutingService (which runs after the engine
/// in GraphLayoutCoordinator) — it consumes the node/cluster bounds we produce.
/// </summary>
public sealed class MsaglLayoutEngine : IGraphLayoutEngine
{
    public LayoutResult Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null) return new LayoutResult();

        var realNodes = graph.Nodes.Where(n => n.Role == LayoutNodeRole.Real).ToList();
        if (realNodes.Count == 0) return new LayoutResult();

        // When a partitioning option is on, build a modified cluster list with
        // synthetic top-level clusters and re-parent namespace clusters under them.
        // MSAGL lays out each region independently and packs them side-by-side.
        var originalClusterIds = new HashSet<string>(graph.Clusters.Select(c => c.Id));
        var clusters = PartitionClustersIfEnabled(graph, options);

        // ── Build MSAGL GeometryGraph ──
        var geomGraph = new GeometryGraph();
        geomGraph.RootCluster.UserData = "root";

        // Map our node Id → MSAGL Node
        var msaglNodeById = new Dictionary<string, Node>();
        foreach (var ln in realNodes)
        {
            float w = Math.Max(ln.Width, 10f);
            float h = Math.Max(ln.Height, 10f);
            var curve = CurveFactory.CreateRectangle(w, h, new Point(0, 0));
            var node = new Node(curve, ln.Id);
            msaglNodeById[ln.Id] = node;
            geomGraph.Nodes.Add(node);
        }

        // Map our cluster Id → MSAGL Cluster, wire containment
        var msaglClusterById = new Dictionary<string, Cluster>();
        foreach (var lc in clusters)
        {
            var cluster = new Cluster { UserData = lc.Id };
            msaglClusterById[lc.Id] = cluster;

            // Add child nodes (direct children only — cluster nesting handled below)
            foreach (var nodeId in lc.NodeIds)
            {
                if (msaglNodeById.TryGetValue(nodeId, out var n))
                    cluster.AddChild(n);
            }
        }

        // Wire cluster hierarchy (parent → child clusters) and attach to root
        var childClusterIdsByParent = clusters
            .Where(c => !string.IsNullOrEmpty(c.ParentClusterId))
            .GroupBy(c => c.ParentClusterId!)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());

        foreach (var lc in clusters)
        {
            var cluster = msaglClusterById[lc.Id];
            if (childClusterIdsByParent.TryGetValue(lc.Id, out var childIds))
            {
                foreach (var childId in childIds)
                {
                    if (msaglClusterById.TryGetValue(childId, out var childCluster))
                        cluster.AddChild(childCluster);
                }
            }
        }

        // Top-level clusters (no parent) attach to RootCluster
        foreach (var lc in clusters)
        {
            if (string.IsNullOrEmpty(lc.ParentClusterId))
                geomGraph.RootCluster.AddChild(msaglClusterById[lc.Id]);
        }

        // Edges — only between real nodes that exist in the map
        foreach (var le in graph.Edges)
        {
            if (le.Role != LayoutEdgeRole.Direct) continue;
            if (!msaglNodeById.TryGetValue(le.FromNodeId, out var src)) continue;
            if (!msaglNodeById.TryGetValue(le.ToNodeId, out var tgt)) continue;
            geomGraph.Edges.Add(new Edge(src, tgt) { UserData = le.Id });
        }

        // ── Layout settings ──
        var settings = new SugiyamaLayoutSettings
        {
            NodeSeparation = Math.Max(options.NodeSpacing, 20),
            LayerSeparation = Math.Max(options.RankSpacing, 20),
            ClusterMargin = Math.Max(options.GroupSpacing, 10),
            EdgeRoutingSettings = new EdgeRoutingSettings
            {
                EdgeRoutingMode = EdgeRoutingMode.SugiyamaSplines,
                Padding = 8,
            },
        };

        // MSAGL's Sugiyama layout defaults to top-to-bottom. To match the
        // user's LayoutDirection preference, apply a post-layout rotation:
        // 90° CCW transforms TB → LR (root goes from top to left, children
        // flow rightward). MSAGL applies the inverse before layout (to adjust
        // label sizes) and the forward transform after layout to all geometry.
        if (options.Direction == LayoutDirection.LeftToRight)
        {
            settings.Transformation = new PlaneTransformation(0, -1, 0, 1, 0, 0);
        }

        LayoutHelpers.CalculateLayout(geomGraph, settings, null);

        // ── Read back, flipping Y from MSAGL's Y-up to our Y-down ──
        var bbox = geomGraph.BoundingBox;
        float originX = (float)bbox.Left;
        float originY = (float)bbox.Top;
        float contentW = (float)(bbox.Right - bbox.Left);
        float contentH = (float)(bbox.Top - bbox.Bottom);

        var nodeBounds = new Dictionary<string, Rect>();
        foreach (var ln in realNodes)
        {
            var n = msaglNodeById[ln.Id];
            float cx = (float)n.Center.X;
            float cy = (float)n.Center.Y;
            float w = (float)n.BoundingBox.Right - (float)n.BoundingBox.Left;
            float h = (float)n.BoundingBox.Top - (float)n.BoundingBox.Bottom;
            float x = cx - originX - w * 0.5f;
            float y = originY - cy - h * 0.5f;
            nodeBounds[ln.Id] = new Rect(x, y, w, h);
        }

        var clusterBounds = new Dictionary<string, Rect>();
        foreach (var lc in graph.Clusters)
        {
            if (!msaglClusterById.TryGetValue(lc.Id, out var cluster)) continue;
            var cb = cluster.BoundingBox;
            float cl = (float)cb.Left;
            float cr = (float)cb.Right;
            float cbot = (float)cb.Bottom;
            float ctop = (float)cb.Top;
            float x = cl - originX;
            float y = originY - ctop;
            float w = cr - cl;
            float h = ctop - cbot;
            if (w <= 0 || h <= 0) continue;
            clusterBounds[lc.Id] = new Rect(x, y, w, h);
        }

        // Apply outer margin by offsetting everything
        float offsetX = options.OuterMarginX;
        float offsetY = options.OuterMarginY;
        if (offsetX != 0f || offsetY != 0f)
        {
            var shiftedNodes = new Dictionary<string, Rect>();
            foreach (var kv in nodeBounds)
            {
                var r = kv.Value;
                shiftedNodes[kv.Key] = new Rect(r.X + offsetX, r.Y + offsetY, r.Width, r.Height);
            }
            nodeBounds = shiftedNodes;

            var shiftedClusters = new Dictionary<string, Rect>();
            foreach (var kv in clusterBounds)
            {
                var r = kv.Value;
                shiftedClusters[kv.Key] = new Rect(r.X + offsetX, r.Y + offsetY, r.Width, r.Height);
            }
            clusterBounds = shiftedClusters;
        }

        float totalW = contentW + offsetX * 2f;
        float totalH = contentH + offsetY * 2f;
        float finalW = Math.Max(options.MinimumContentWidth, totalW);
        float finalH = Math.Max(options.MinimumContentHeight, totalH);

        return new LayoutResult
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            ContentSize = new Vector2(finalW, finalH),
        };
    }

    // ── Cluster partitioning ──

    private const string AppClusterId = "__App";
    private const string TestClusterId = "__Tests";
    private const string PartitionPrefix = "__NS:";

    /// <summary>
    /// When a partitioning option is enabled, creates synthetic top-level
    /// clusters and re-parents existing namespace clusters under them.
    /// Two modes:
    /// - SeparateAppAndTests: two buckets (Application / Tests) by test-namespace pattern.
    /// - PartitionByFirstLevelNamespace: N buckets by first-level sub-namespace
    ///   after auto-detecting the topmost common prefix (e.g. PFE.Data, PFE.Systems).
    /// </summary>
    private List<LayoutCluster> PartitionClustersIfEnabled(LayoutGraph graph, LayoutOptions options)
    {
        var clusters = new List<LayoutCluster>(graph.Clusters);

        if (clusters.Count == 0) return clusters;

        // Don't partition if there's only a fallback "Ungrouped" cluster
        if (clusters.Count == 1 && clusters[0].Id == "fallback")
            return clusters;

        // PartitionByFirstLevelNamespace takes precedence (mutually exclusive)
        if (options.PartitionByFirstLevelNamespace)
            return PartitionByFirstLevel(clusters);
        if (options.SeparateAppAndTests)
            return PartitionByAppAndTests(clusters);

        return clusters;
    }

    private List<LayoutCluster> PartitionByAppAndTests(List<LayoutCluster> clusters)
    {
        var appCluster = new LayoutCluster
        {
            Id = AppClusterId,
            Label = "Application",
            Kind = MermaidDiagramExporter.Core.TypeGroupKind.Assembly,
        };
        var testCluster = new LayoutCluster
        {
            Id = TestClusterId,
            Label = "Tests",
            Kind = MermaidDiagramExporter.Core.TypeGroupKind.Assembly,
        };

        foreach (var lc in clusters)
        {
            if (lc.Id == AppClusterId || lc.Id == TestClusterId) continue;
            lc.ParentClusterId = IsTestNamespace(lc.Label) ? TestClusterId : AppClusterId;
        }

        clusters.Add(appCluster);
        clusters.Add(testCluster);
        return clusters;
    }

    private List<LayoutCluster> PartitionByFirstLevel(List<LayoutCluster> clusters)
    {
        // Phase 1: collect all namespace labels to find the topmost common prefix.
        var allParts = clusters
            .Where(c => !c.Id.StartsWith(PartitionPrefix))
            .Select(c => c.Label.Split('.'))
            .Where(parts => parts.Length > 0)
            .ToList();

        if (allParts.Count == 0) return clusters;

        // Find longest common prefix across all namespace segment arrays.
        int prefixLen = 0;
        int minLen = allParts.Min(p => p.Length);
        for (int i = 0; i < minLen; i++)
        {
            string segment = allParts[0][i];
            if (allParts.Any(p => p[i] != segment)) break;
            prefixLen++;
        }

        // Phase 2: group clusters by the segment right after the common prefix.
        // If a namespace has no segment beyond the prefix, use the last prefix
        // segment as its group key.
        var syntheticById = new Dictionary<string, LayoutCluster>();
        var clusterToParent = new Dictionary<string, string>();

        foreach (var lc in clusters)
        {
            if (lc.Id.StartsWith(PartitionPrefix)) continue;

            var parts = lc.Label.Split('.');
            string groupKey;
            if (parts.Length > prefixLen)
                groupKey = string.Join('.', parts, 0, prefixLen + 1);
            else if (parts.Length > 0)
                groupKey = lc.Label;
            else
                continue;

            if (!syntheticById.TryGetValue(groupKey, out var synthetic))
            {
                synthetic = new LayoutCluster
                {
                    Id = PartitionPrefix + groupKey,
                    Label = groupKey,
                    Kind = MermaidDiagramExporter.Core.TypeGroupKind.Assembly,
                };
                syntheticById[groupKey] = synthetic;
            }

            lc.ParentClusterId = synthetic.Id;
        }

        clusters.AddRange(syntheticById.Values);
        return clusters;
    }

    /// <summary>
    /// Detects whether a namespace label indicates a test namespace.
    /// Matches: *.Tests, *.Test, *.Tests.*, *.Test.*, Tests, Test
    /// </summary>
    private static bool IsTestNamespace(string ns)
    {
        if (string.IsNullOrWhiteSpace(ns)) return false;
        var parts = ns.Split('.');
        foreach (var part in parts)
        {
            if (string.Equals(part, "Tests", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "Test", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "TestSuite", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "Specs", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(part, "Specification", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
