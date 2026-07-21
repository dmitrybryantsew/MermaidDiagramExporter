using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Own force-directed layout engine (Fruchterman–Reingold style). Exists
/// because MSAGL's public force path (InitialLayout) is a deliberately cheap
/// seed layout — MDS seed + 5–10 iterations — that degenerates into a vertical
/// strip on large clustered graphs, with no public API for iteration control,
/// pinning, or incremental updates. This engine gives full control:
/// deterministic seeding, rectangle-aware forces, per-cluster gravity.
///
/// Forces per iteration:
/// - springs along edges (attract to a target border gap),
/// - repulsion between all node pairs (rectangle-border aware),
/// - gravity pulling each node toward its top-level cluster centroid,
/// - weak global gravity toward the overall centroid (keeps components near).
/// Deterministic: same graph + same seed = same layout.
/// </summary>
public sealed class ForceDirectedLayoutEngine : IGraphLayoutEngine
{
    public LayoutResult Run(LayoutGraph graph, LayoutOptions options)
    {
        if (graph == null) return new LayoutResult();

        var nodes = graph.Nodes.Where(n => n.Role == LayoutNodeRole.Real).ToList();
        if (nodes.Count == 0) return new LayoutResult();

        var fo = options.Force;
        int n = nodes.Count;
        var indexOf = new Dictionary<string, int>(n);
        for (int i = 0; i < n; i++) indexOf[nodes[i].Id] = i;

        var halfW = new float[n];
        var halfH = new float[n];
        for (int i = 0; i < n; i++)
        {
            halfW[i] = Math.Max(nodes[i].Width, 10f) * 0.5f;
            halfH[i] = Math.Max(nodes[i].Height, 10f) * 0.5f;
        }

        // ── Springs (direct edges between real nodes), weighted by coupling
        // kind — inheritance/implements pairs pull harder (LayoutEdgeWeights) ──
        var springs = new List<(int a, int b, float weight)>();
        foreach (var le in graph.Edges)
        {
            if (le.Role != LayoutEdgeRole.Direct) continue;
            if (!indexOf.TryGetValue(le.FromNodeId, out int a)) continue;
            if (!indexOf.TryGetValue(le.ToNodeId, out int b)) continue;
            if (a != b) springs.Add((a, b, LayoutEdgeWeights.GetWeight(le.Kind)));
        }

        // ── Cluster groups (top-level cluster per node, for gravity) ──
        var topClusterByNode = ResolveTopLevelClusters(nodes, graph.Clusters);
        var clusterGroups = new Dictionary<string, List<int>>();
        for (int i = 0; i < n; i++)
        {
            string tc = topClusterByNode[i];
            if (tc.Length == 0) continue;
            if (!clusterGroups.TryGetValue(tc, out var list))
                clusterGroups[tc] = list = new List<int>();
            list.Add(i);
        }

        // ── Ideal border gap for springs; repulsion scale ──
        float k = Math.Max(options.RankSpacing, 40f);

        // ── Deterministic seed: top-level clusters on a circle, nodes
        // jittered around their cluster anchor ──
        var rng = new Random(fo.Seed);
        var pos = new (double x, double y)[n];
        var anchors = new Dictionary<string, (double x, double y)>();
        int ci = 0;
        double clusterRingRadius = Math.Max(k * clusterGroups.Count * 0.3, k * 2);
        foreach (var key in clusterGroups.Keys.OrderBy(x => x, StringComparer.Ordinal))
        {
            double angle = 2.0 * Math.PI * ci / Math.Max(clusterGroups.Count, 1);
            anchors[key] = (Math.Cos(angle) * clusterRingRadius, Math.Sin(angle) * clusterRingRadius);
            ci++;
        }
        for (int i = 0; i < n; i++)
        {
            string tc = topClusterByNode[i];
            var anchor = tc.Length > 0 && anchors.TryGetValue(tc, out var a) ? a : (x: 0.0, y: 0.0);
            pos[i] = (anchor.x + (rng.NextDouble() - 0.5) * k * 2,
                      anchor.y + (rng.NextDouble() - 0.5) * k * 2);
        }

        // ── Iterate ──
        int iterations = Math.Max(fo.Iterations, 1);
        double initialTemperature = k;
        var disp = new (double x, double y)[n];
        var clusterCentroids = new Dictionary<string, (double x, double y)>();

        for (int iter = 0; iter < iterations; iter++)
        {
            double temperature = initialTemperature * (1.0 - (double)iter / iterations) + 0.5;
            for (int i = 0; i < n; i++) disp[i] = (0, 0);

            // Repulsion (all pairs, rectangle-border aware)
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var (dx, dy, dist) = BorderVector(i, j, pos, halfW, halfH, rng);
                    double force = fo.RepulsionConstant * k * k / dist;
                    double ux = dx / dist * force;
                    double uy = dy / dist * force;
                    disp[i].x -= ux; disp[i].y -= uy;
                    disp[j].x += ux; disp[j].y += uy;
                }
            }

            // Springs (attract to border gap k), weighted by coupling strength
            foreach (var (a, b, weight) in springs)
            {
                var (dx, dy, dist) = BorderVector(a, b, pos, halfW, halfH, rng);
                double force = fo.SpringConstant * weight * (dist - k) * 0.5;
                double ux = dx / dist * force;
                double uy = dy / dist * force;
                disp[a].x += ux; disp[a].y += uy;
                disp[b].x -= ux; disp[b].y -= uy;
            }

            // Cluster gravity toward top-level cluster centroid
            if (fo.ClusterGravity > 0)
            {
                clusterCentroids.Clear();
                foreach (var (key, members) in clusterGroups)
                {
                    double sx = 0, sy = 0;
                    foreach (int m in members) { sx += pos[m].x; sy += pos[m].y; }
                    clusterCentroids[key] = (sx / members.Count, sy / members.Count);
                }
                foreach (var (key, members) in clusterGroups)
                {
                    var c = clusterCentroids[key];
                    foreach (int m in members)
                    {
                        disp[m].x += (c.x - pos[m].x) * fo.ClusterGravity;
                        disp[m].y += (c.y - pos[m].y) * fo.ClusterGravity;
                    }
                }
            }

            // Weak global gravity toward overall centroid
            {
                double gx = 0, gy = 0;
                for (int i = 0; i < n; i++) { gx += pos[i].x; gy += pos[i].y; }
                gx /= n; gy /= n;
                for (int i = 0; i < n; i++)
                {
                    disp[i].x += (gx - pos[i].x) * 0.05;
                    disp[i].y += (gy - pos[i].y) * 0.05;
                }
            }

            // Apply capped displacements
            for (int i = 0; i < n; i++)
            {
                double len = Math.Sqrt(disp[i].x * disp[i].x + disp[i].y * disp[i].y);
                if (len < 1e-9) continue;
                double capped = Math.Min(len, temperature);
                pos[i].x += disp[i].x / len * capped;
                pos[i].y += disp[i].y / len * capped;
            }
        }

        ResolveOverlaps(pos, halfW, halfH, options.NodeSpacing, rng);

        // ── Read back ──
        float minX = float.MaxValue, minY = float.MaxValue;
        var nodeBounds = new Dictionary<string, Rect>(n);
        for (int i = 0; i < n; i++)
        {
            float x = (float)pos[i].x - halfW[i];
            float y = (float)pos[i].y - halfH[i];
            nodeBounds[nodes[i].Id] = new Rect(x, y, halfW[i] * 2f, halfH[i] * 2f);
            if (x < minX) minX = x;
            if (y < minY) minY = y;
        }

        // Normalize to positive origin + outer margin
        float offsetX = options.OuterMarginX - minX;
        float offsetY = options.OuterMarginY - minY;
        var shifted = new Dictionary<string, Rect>(n);
        float maxX = float.MinValue, maxY = float.MinValue;
        foreach (var (id, r) in nodeBounds)
        {
            var s = new Rect(r.X + offsetX, r.Y + offsetY, r.Width, r.Height);
            shifted[id] = s;
            if (s.xMax > maxX) maxX = s.xMax;
            if (s.yMax > maxY) maxY = s.yMax;
        }

        var clusterBounds = ClusterBoundsComputer.Compute(graph.Clusters, shifted, options);
        if (options.Force.PreventClusterOverlap)
        {
            // Guarantee: no two sibling namespace clusters overlap. Overlapping
            // pairs are rigidly translated apart (2D minimal push, bottom-up).
            ForceClusterSeparation.Separate(graph.Clusters, shifted, clusterBounds, options);
        }

        foreach (var r in clusterBounds.Values)
        {
            if (r.xMax > maxX) maxX = r.xMax;
            if (r.yMax > maxY) maxY = r.yMax;
        }

        float finalW = Math.Max(options.MinimumContentWidth, maxX + options.OuterMarginX);
        float finalH = Math.Max(options.MinimumContentHeight, maxY + options.OuterMarginY);

        return new LayoutResult
        {
            NodeBounds = shifted,
            ClusterBounds = clusterBounds,
            ContentSize = new Vector2(finalW, finalH),
        };
    }

    /// <summary>
    /// Rectangle-border aware direction + distance between two nodes.
    /// Returns the unit-scale direction (dx, dy — not normalized, scaled by
    /// dist) and the border distance (gap between rectangles, min 1).
    /// </summary>
    private static (double dx, double dy, double dist) BorderVector(
        int i, int j,
        (double x, double y)[] pos,
        float[] halfW, float[] halfH,
        Random rng)
    {
        double cx = pos[j].x - pos[i].x;
        double cy = pos[j].y - pos[i].y;
        if (cx == 0 && cy == 0)
        {
            // Coincident centers: deterministic arbitrary direction.
            cx = (rng.NextDouble() - 0.5) * 0.01;
            cy = (rng.NextDouble() - 0.5) * 0.01;
        }

        double overlapX = halfW[i] + halfW[j] - Math.Abs(cx); // >0 means overlapping in X
        double overlapY = halfH[i] + halfH[j] - Math.Abs(cy); // >0 means overlapping in Y

        if (overlapX > 0 && overlapY > 0)
        {
            // Rectangles overlap: push apart along the axis of least penetration.
            double dist = Math.Min(overlapX, overlapY) + 1.0;
            if (overlapX < overlapY)
                return (Math.Sign(cx) * dist, 0, dist);
            return (0, Math.Sign(cy) * dist, dist);
        }

        double gapX = Math.Max(0, -overlapX);
        double gapY = Math.Max(0, -overlapY);
        double gap = Math.Max(Math.Sqrt(gapX * gapX + gapY * gapY), 1.0);
        // Direction from border to border ≈ center direction scaled to the gap.
        double centerLen = Math.Max(Math.Sqrt(cx * cx + cy * cy), 1e-9);
        return (cx / centerLen * gap, cy / centerLen * gap, gap);
    }

    /// <summary>
    /// A few sweep passes pushing overlapping rectangles apart along the axis
    /// of least penetration. Force iterations leave small overlaps because
    /// repulsion is border-approximate.
    /// </summary>
    private static void ResolveOverlaps(
        (double x, double y)[] pos,
        float[] halfW, float[] halfH,
        float spacing,
        Random rng)
    {
        int n = pos.Length;
        for (int sweep = 0; sweep < 10; sweep++)
        {
            bool anyOverlap = false;
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    double cx = pos[j].x - pos[i].x;
                    double cy = pos[j].y - pos[i].y;
                    double overlapX = halfW[i] + halfW[j] + spacing - Math.Abs(cx);
                    double overlapY = halfH[i] + halfH[j] + spacing - Math.Abs(cy);
                    if (overlapX <= 0 || overlapY <= 0) continue;

                    anyOverlap = true;
                    if (overlapX < overlapY)
                    {
                        double push = overlapX * 0.5;
                        double sign = cx != 0 ? Math.Sign(cx) : (rng.NextDouble() > 0.5 ? 1 : -1);
                        pos[i].x -= sign * push;
                        pos[j].x += sign * push;
                    }
                    else
                    {
                        double push = overlapY * 0.5;
                        double sign = cy != 0 ? Math.Sign(cy) : (rng.NextDouble() > 0.5 ? 1 : -1);
                        pos[i].y -= sign * push;
                        pos[j].y += sign * push;
                    }
                }
            }
            if (!anyOverlap) break;
        }
    }

    /// <summary>
    /// Maps each node to its top-level cluster id ("" when unclustered).
    /// </summary>
    private static string[] ResolveTopLevelClusters(
        List<LayoutNode> nodes,
        IReadOnlyList<LayoutCluster> clusters)
    {
        var parentById = clusters.ToDictionary(c => c.Id, c => c.ParentClusterId ?? "");
        var result = new string[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            string id = nodes[i].ClusterId ?? "";
            for (int guard = 0; guard < 64; guard++)
            {
                if (id.Length == 0 || !parentById.TryGetValue(id, out string parent) || parent.Length == 0)
                    break;
                id = parent;
            }
            result[i] = id;
        }
        return result;
    }
}
