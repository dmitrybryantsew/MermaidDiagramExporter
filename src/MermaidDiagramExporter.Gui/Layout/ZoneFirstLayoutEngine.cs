using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Zone-first hybrid layout ("macro/micro" two-level, research doc §4.1):
///
/// 1. <b>Micro</b>: each top-level namespace cluster is laid out independently
///    (Sugiyama for readable hierarchy, or force — <see cref="ZoneFirstEngineOptions.MicroEngine"/>),
///    with a per-zone <see cref="ForceClusterSeparation"/> so nested clusters
///    inside a zone never overlap.
/// 2. <b>Macro</b>: every zone becomes one supernode sized by its laid-out
///    content; cross-zone edges aggregate into weighted springs (coupling
///    strength). The small zone graph is placed by <see cref="ZoneMacroLayout"/>
///    (a force layout built for large boxes), so coupled zones land adjacent
///    and unrelated zones repel to the periphery.
/// 3. <b>Place</b>: a padded box per zone is reserved at its macro position
///    and guaranteed non-overlapping (monotone rightward sweep).
/// 4. <b>Compose</b>: zone contents are translated into their boxes; cluster
///    bounds are recomputed globally (they match the reserved boxes exactly).
///
/// Deterministic (fixed-seed macro force, sorted iteration order throughout).
/// Edge routing is left to the EdgeRoutingService like for all engines.
/// </summary>
public sealed class ZoneFirstLayoutEngine : IGraphLayoutEngine
{
    /// <summary>
    /// Cap for aggregated macro spring weights. A zone pair with 40 cross
    /// references should pull harder than one with 2, but unbounded weights
    /// (sums reach 50+) would collapse zone boxes into each other.
    /// </summary>
    private const float MaxMacroEdgeWeight = 10f;

    /// <summary>
    /// Iterations for the macro force layout. Fixed independently of
    /// <see cref="ForceEngineOptions.Iterations"/> (that knob tunes the
    /// node-level engine's regime): measured on the real 48-zone graph, 500
    /// iterations brings coupled zones ~20% closer than 300 at negligible
    /// cost (the macro graph has ≤ ~60 boxes).
    /// </summary>
    private const int MacroIterations = 500;

    private sealed class ZoneLayout
    {
        public string Key = "";
        public string? ClusterId;                     // null = singleton (unclustered node)
        public Dictionary<string, Rect> LocalNodeBounds = new();
        public float ContentMinX, ContentMinY;        // top-left of laid-out content (pre-padding)
        public float PadLeft, PadTop;                 // zone box padding (0 for singletons)
        public float BoxWidth, BoxHeight;             // reserved box = content + padding
    }

    public LayoutResult Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null) return new LayoutResult();

        var nodes = graph.Nodes.Where(n => n.Role == LayoutNodeRole.Real).ToList();
        if (nodes.Count == 0) return new LayoutResult();

        var clusterById = graph.Clusters.ToDictionary(c => c.Id);

        // ── Zone assignment: top-level cluster per node; unclustered nodes
        // become singleton zones (no cluster box) ──
        var zoneKeyByNode = new Dictionary<string, string>(nodes.Count);
        foreach (var node in nodes)
        {
            string top = ResolveTopLevelClusterId(node.ClusterId, clusterById);
            zoneKeyByNode[node.Id] = top.Length > 0 ? "c:" + top : "n:" + node.Id;
        }

        var zoneNodeIds = new Dictionary<string, List<string>>();
        foreach (var node in nodes)
        {
            string key = zoneKeyByNode[node.Id];
            if (!zoneNodeIds.TryGetValue(key, out var list))
                zoneNodeIds[key] = list = new List<string>();
            list.Add(node.Id);
        }

        // ── Micro layout per zone ──
        var zones = new List<ZoneLayout>();
        foreach (var key in zoneNodeIds.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            zones.Add(key.StartsWith("c:", StringComparison.Ordinal)
                ? LayoutClusterZone(key.Substring(2), zoneNodeIds[key], graph, clusterById, options)
                : LayoutSingletonZone(key.Substring(2), graph));
        }

        // ── Macro layout: one box per zone, aggregated weighted springs ──
        float zoneSpacing = Math.Max(options.ZoneFirst.ZoneSpacing, 20f);
        var zoneBoxes = ZoneMacroLayout.Run(
            BuildMacroBoxes(zones),
            BuildMacroSprings(zones, zoneKeyByNode, graph.Edges),
            zoneSpacing,
            options.OuterMarginX,
            options.OuterMarginY,
            MacroIterations,
            options.Force.RepulsionConstant,
            options.Force.SpringConstant,
            options.Force.Seed);

        // ── Compose: translate zone contents into their boxes ──
        var nodeBounds = new Dictionary<string, Rect>(nodes.Count);
        foreach (var zone in zones)
        {
            if (!zoneBoxes.TryGetValue(zone.Key, out var box)) continue;
            float dx = box.X + zone.PadLeft - zone.ContentMinX;
            float dy = box.Y + zone.PadTop - zone.ContentMinY;
            foreach (var (nodeId, local) in zone.LocalNodeBounds)
                nodeBounds[nodeId] = new Rect(local.X + dx, local.Y + dy, local.Width, local.Height);
        }

        // Cluster bounds recomputed globally: a zone cluster rect lands exactly
        // on its reserved box (content bbox + same padding formula), nested
        // cluster rects are rebuilt from the same member positions.
        var clusterBounds = ClusterBoundsComputer.Compute(graph.Clusters, nodeBounds, options);

        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var box in zoneBoxes.Values)
        {
            if (box.xMax > maxX) maxX = box.xMax;
            if (box.yMax > maxY) maxY = box.yMax;
        }
        float finalW = Math.Max(options.MinimumContentWidth, maxX + options.OuterMarginX);
        float finalH = Math.Max(options.MinimumContentHeight, maxY + options.OuterMarginY);

        return new LayoutResult
        {
            NodeBounds = nodeBounds,
            ClusterBounds = clusterBounds,
            ContentSize = new Vector2(finalW, finalH),
        };
    }

    // ── Micro layouts ──

    private static ZoneLayout LayoutClusterZone(
        string zoneClusterId,
        List<string> memberNodeIds,
        LayoutGraph graph,
        Dictionary<string, LayoutCluster> clusterById,
        LayoutOptions options)
    {
        // Cluster subtree of the zone (zone root + nested descendants).
        var subtree = new List<LayoutCluster>();
        var queue = new Queue<string>();
        queue.Enqueue(zoneClusterId);
        while (queue.Count > 0)
        {
            string id = queue.Dequeue();
            if (!clusterById.TryGetValue(id, out var cluster)) continue;
            subtree.Add(cluster);
            foreach (var childId in cluster.ChildClusterIds)
                queue.Enqueue(childId);
        }

        var memberSet = new HashSet<string>(memberNodeIds);
        var subgraph = new LayoutGraph
        {
            Nodes = graph.Nodes.Where(n => memberSet.Contains(n.Id)).ToList(),
            Edges = graph.Edges
                .Where(e => e.Role == LayoutEdgeRole.Direct && memberSet.Contains(e.FromNodeId) && memberSet.Contains(e.ToNodeId))
                .ToList(),
            Clusters = subtree,
        };

        var localNodeBounds = RunMicroLayout(subgraph, options);

        // Guarantee nested clusters inside the zone never overlap, then measure
        // content = nodes ∪ nested cluster rects (strict descendants only — the
        // zone root's own rect already includes the zone padding we add below).
        var localClusterBounds = ClusterBoundsComputer.Compute(subtree, localNodeBounds, options);
        ForceClusterSeparation.Separate(subtree, localNodeBounds, localClusterBounds, options);

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var r in localNodeBounds.Values) Extend(ref minX, ref minY, ref maxX, ref maxY, r);
        foreach (var cluster in subtree)
        {
            if (cluster.Id == zoneClusterId) continue;
            if (localClusterBounds.TryGetValue(cluster.Id, out var r))
                Extend(ref minX, ref minY, ref maxX, ref maxY, r);
        }

        var zoneCluster = clusterById[zoneClusterId];
        float padL = options.GroupLeftPadding;
        float padR = options.GroupLeftPadding;
        float padT = options.GroupTopPadding + zoneCluster.TitleMetrics.TotalMargin;
        float padB = options.GroupBottomPadding;

        return new ZoneLayout
        {
            Key = "c:" + zoneClusterId,
            ClusterId = zoneClusterId,
            LocalNodeBounds = localNodeBounds,
            ContentMinX = minX,
            ContentMinY = minY,
            PadLeft = padL,
            PadTop = padT,
            BoxWidth = (maxX - minX) + padL + padR,
            BoxHeight = (maxY - minY) + padT + padB,
        };
    }

    private static ZoneLayout LayoutSingletonZone(string nodeId, LayoutGraph graph)
    {
        var node = graph.Nodes.First(n => n.Id == nodeId);
        float w = Math.Max(node.Width, 10f);
        float h = Math.Max(node.Height, 10f);
        return new ZoneLayout
        {
            Key = "n:" + nodeId,
            ClusterId = null,
            LocalNodeBounds = new Dictionary<string, Rect> { [nodeId] = new Rect(0, 0, w, h) },
            ContentMinX = 0,
            ContentMinY = 0,
            PadLeft = 0,
            PadTop = 0,
            BoxWidth = w,
            BoxHeight = h,
        };
    }

    private static Dictionary<string, Rect> RunMicroLayout(LayoutGraph subgraph, LayoutOptions options)
    {
        var micro = options.Clone();
        micro.OuterMarginX = 0;
        micro.OuterMarginY = 0;
        micro.MinimumContentWidth = 0;
        micro.MinimumContentHeight = 0;

        LayoutResult result;
        if (options.ZoneFirst.MicroEngine == ZoneFirstMicroEngine.Force)
        {
            micro.Engine = LayoutEngineKind.Force;
            micro.Force.PreventClusterOverlap = false; // separation runs uniformly below
            result = new ForceDirectedLayoutEngine().Run(subgraph, micro);
        }
        else
        {
            micro.Engine = LayoutEngineKind.Msagl;
            micro.Msagl = new MsaglEngineOptions { Partition = MsaglPartitionMode.None };
            result = new MsaglLayoutEngine().Run(subgraph, micro);
        }

        return new Dictionary<string, Rect>(result.NodeBounds);
    }

    // ── Macro graph ──

    private static List<(string Key, float Width, float Height)> BuildMacroBoxes(List<ZoneLayout> zones)
    {
        return zones
            .Select(z => (z.Key, Math.Max(z.BoxWidth, 10f), Math.Max(z.BoxHeight, 10f)))
            .ToList();
    }

    /// <summary>
    /// Aggregates cross-zone edges into one spring per zone pair, weight = Σ
    /// coupling (inheritance 3 / implements 2.5 / other 1), capped at
    /// <see cref="MaxMacroEdgeWeight"/>. Indices reference the sorted
    /// <paramref name="zones"/> list.
    /// </summary>
    private static List<(int A, int B, float Weight)> BuildMacroSprings(
        List<ZoneLayout> zones,
        Dictionary<string, string> zoneKeyByNode,
        IReadOnlyList<LayoutEdge> edges)
    {
        var indexByKey = new Dictionary<string, int>(zones.Count);
        for (int i = 0; i < zones.Count; i++) indexByKey[zones[i].Key] = i;

        var weightByPair = new Dictionary<(int, int), float>();
        foreach (var edge in edges)
        {
            if (edge.Role != LayoutEdgeRole.Direct) continue;
            if (!zoneKeyByNode.TryGetValue(edge.FromNodeId, out var za)) continue;
            if (!zoneKeyByNode.TryGetValue(edge.ToNodeId, out var zb)) continue;
            if (za == zb) continue;
            int ia = indexByKey[za], ib = indexByKey[zb];
            var key = ia < ib ? (ia, ib) : (ib, ia);
            weightByPair.TryGetValue(key, out float w);
            weightByPair[key] = w + LayoutEdgeWeights.GetWeight(edge.Kind);
        }

        return weightByPair
            .OrderBy(kv => kv.Key.Item1)
            .ThenBy(kv => kv.Key.Item2)
            .Select(kv => (kv.Key.Item1, kv.Key.Item2, Math.Clamp(kv.Value, 1f, MaxMacroEdgeWeight)))
            .ToList();
    }

    // ── Helpers ──

    private static void Extend(ref float minX, ref float minY, ref float maxX, ref float maxY, Rect r)
    {
        if (r.xMin < minX) minX = r.xMin;
        if (r.yMin < minY) minY = r.yMin;
        if (r.xMax > maxX) maxX = r.xMax;
        if (r.yMax > maxY) maxY = r.yMax;
    }

    /// <summary>
    /// Top-level cluster id for a node's immediate cluster, or "" when the
    /// node is unclustered or its cluster chain dangles.
    /// </summary>
    private static string ResolveTopLevelClusterId(string? clusterId, Dictionary<string, LayoutCluster> clusterById)
    {
        string id = clusterId ?? "";
        for (int guard = 0; guard < 64; guard++)
        {
            if (id.Length == 0 || !clusterById.TryGetValue(id, out var cluster)) return "";
            if (string.IsNullOrEmpty(cluster.ParentClusterId)) return id;
            id = cluster.ParentClusterId;
        }
        return "";
    }
}
