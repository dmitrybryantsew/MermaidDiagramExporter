using System;
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Force placement for the zone-first macro step: a handful (≤ ~60) of large
/// zone boxes connected by weighted coupling springs. Deliberately NOT the
/// node-level <see cref="ForceDirectedLayoutEngine"/>: that engine's repulsion
/// weakens as rectangles overlap more deeply, which is tolerable for small
/// class nodes but wrong for huge zone boxes — boxes would merge into blobs
/// and ignore their coupling springs.
///
/// Differences from the node engine:
/// - deterministic grid seeding with zero initial overlap (boxes start laid
///   out in rows, not stacked at the origin),
/// - overlapping pairs get a separation force that GROWS with penetration,
/// - springs act on the border gap and naturally reverse into repulsion when
///   boxes overlap (gap &lt; k).
///
/// Ends with a guaranteed non-overlap sweep (monotone rightward pushes) and
/// normalizes so the top-left box corner is at (marginX, marginY).
/// Deterministic: same input + same seed = same placement.
/// </summary>
public static class ZoneMacroLayout
{
    /// <summary>
    /// Repulsion cutoff in units of the ideal gap k. Pairs farther apart than
    /// k × this factor don't repel — long-range repulsion over many bodies
    /// inflates the pack and neutralizes coupling springs.
    /// </summary>
    private const double RepulsionRangeFactor = 4.0;

    public static Dictionary<string, Rect> Run(
        IReadOnlyList<(string Key, float Width, float Height)> boxes,
        IReadOnlyList<(int A, int B, float Weight)> springs,
        float spacing,
        float marginX,
        float marginY,
        int iterations,
        double repulsionConstant,
        double springConstant,
        int seed)
    {
        int n = boxes.Count;
        var result = new Dictionary<string, Rect>(n);
        if (n == 0) return result;

        var halfW = new float[n];
        var halfH = new float[n];
        for (int i = 0; i < n; i++)
        {
            halfW[i] = Math.Max(boxes[i].Width, 10f) * 0.5f;
            halfH[i] = Math.Max(boxes[i].Height, 10f) * 0.5f;
        }

        // ── Grid seed (zero initial overlap), rows sized to a square-ish area ──
        double totalArea = 0;
        for (int i = 0; i < n; i++)
            totalArea += (halfW[i] * 2 + spacing) * (halfH[i] * 2 + spacing);
        double targetRowWidth = Math.Sqrt(totalArea);

        var pos = new (double x, double y)[n]; // box centers
        {
            double x = 0, y = 0, rowHeight = 0;
            for (int i = 0; i < n; i++)
            {
                double w = halfW[i] * 2, h = halfH[i] * 2;
                if (x > 0 && x + w > targetRowWidth)
                {
                    y += rowHeight + spacing;
                    x = 0;
                    rowHeight = 0;
                }
                pos[i] = (x + halfW[i], y + halfH[i]);
                x += w + spacing;
                if (h > rowHeight) rowHeight = h;
            }
        }

        double k = Math.Max(spacing, 40f);
        double initialTemperature = k;
        var rng = new Random(seed);
        var disp = new (double x, double y)[n];
        var dispSeparation = new (double x, double y)[n];

        for (int iter = 0; iter < Math.Max(iterations, 1); iter++)
        {
            double temperature = initialTemperature * (1.0 - (double)iter / iterations) + 0.5;
            for (int i = 0; i < n; i++) { disp[i] = (0, 0); dispSeparation[i] = (0, 0); }

            // ── Pairwise forces ──
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    var v = BorderVector(i, j, pos, halfW, halfH, rng);
                    if (v.overlapping)
                    {
                        // Separation: force grows with penetration, along the
                        // axis of least penetration. Tracked separately from the
                        // force displacement: it is applied with a constant (not
                        // decaying) cap so jams still clear in late iterations —
                        // a temperature-capped separation freezes structural
                        // jams (spring hubs whose neighbors can't all be at
                        // gap k) into place, and the end cleanup would have to
                        // tear coupled pairs far apart.
                        double force = springConstant * (v.penetration + k);
                        dispSeparation[i].x -= v.dx * force; dispSeparation[i].y -= v.dy * force;
                        dispSeparation[j].x += v.dx * force; dispSeparation[j].y += v.dy * force;
                    }
                    else
                    {
                        // Short-range repulsion only: with 48 bodies, infinite-range
                        // k²/gap repulsion inflates the whole pack into a giant disc
                        // (gravity can't balance it) and drowns the springs' effect.
                        // A cutoff lets coupled pairs rest at their spring equilibrium
                        // (~k) while uncoupled boxes settle just outside the pack.
                        double gap = Math.Max(v.gap, 1.0);
                        if (gap > k * RepulsionRangeFactor) continue;
                        double force = repulsionConstant * k * k / gap;
                        disp[i].x -= v.dx * force; disp[i].y -= v.dy * force;
                        disp[j].x += v.dx * force; disp[j].y += v.dy * force;
                    }
                }
            }

            // ── Coupling springs (attract to border gap k; negative gap
            // reverses into repulsion, so crossed boxes uncross) ──
            foreach (var (a, b, weight) in springs)
            {
                var v = BorderVector(a, b, pos, halfW, halfH, rng);
                double gap = v.overlapping ? -v.penetration : Math.Max(v.gap, 1.0);
                double force = springConstant * weight * (gap - k) * 0.5;
                disp[a].x += v.dx * force; disp[a].y += v.dy * force;
                disp[b].x -= v.dx * force; disp[b].y -= v.dy * force;
            }

            // ── Weak global gravity (keeps uncoupled zones near the pack) ──
            {
                double gx = 0, gy = 0;
                for (int i = 0; i < n; i++) { gx += pos[i].x; gy += pos[i].y; }
                gx /= n; gy /= n;
                for (int i = 0; i < n; i++)
                {
                    disp[i].x += (gx - pos[i].x) * 0.02;
                    disp[i].y += (gy - pos[i].y) * 0.02;
                }
            }

            // ── Apply displacements: forces are temperature-capped (annealing),
            // separation gets a constant cap so jams clear all the way ──
            double separationCap = k * 0.5;
            for (int i = 0; i < n; i++)
            {
                double len = Math.Sqrt(disp[i].x * disp[i].x + disp[i].y * disp[i].y);
                if (len >= 1e-9)
                {
                    double capped = Math.Min(len, temperature);
                    pos[i].x += disp[i].x / len * capped;
                    pos[i].y += disp[i].y / len * capped;
                }

                double slen = Math.Sqrt(dispSeparation[i].x * dispSeparation[i].x + dispSeparation[i].y * dispSeparation[i].y);
                if (slen >= 1e-9)
                {
                    double capped = Math.Min(slen, separationCap);
                    pos[i].x += dispSeparation[i].x / slen * capped;
                    pos[i].y += dispSeparation[i].y / slen * capped;
                }
            }

        }

        // Final cleanup: min-axis separation, then the rightward sweep as the
        // termination guarantee for pathological oscillations.
        SeparatePairwise(pos, halfW, halfH, spacing);

        for (int i = 0; i < n; i++)
            result[boxes[i].Key] = new Rect(
                (float)pos[i].x - halfW[i], (float)pos[i].y - halfH[i],
                halfW[i] * 2f, halfH[i] * 2f);

        // Guarantee: no two zone boxes overlap (monotone rightward sweep —
        // converges in at most n sweeps; see ForceClusterSeparation phase 2).
        EnsureSeparated(result, spacing);

        // Normalize: top-left content corner lands at the outer margin.
        float minX = float.MaxValue, minY = float.MaxValue;
        foreach (var r in result.Values)
        {
            if (r.xMin < minX) minX = r.xMin;
            if (r.yMin < minY) minY = r.yMin;
        }
        float ox = marginX - minX, oy = marginY - minY;
        var keys = result.Keys.ToList();
        foreach (var key in keys)
        {
            var r = result[key];
            result[key] = new Rect(r.X + ox, r.Y + oy, r.Width, r.Height);
        }

        return result;
    }

    /// <summary>
    /// Direction and border distance between two boxes. When they overlap,
    /// the direction is the axis of least penetration and the penetration
    /// depth is returned (the gap is meaningless then).
    /// </summary>
    private static (double dx, double dy, double gap, double penetration, bool overlapping) BorderVector(
        int i, int j,
        (double x, double y)[] pos,
        float[] halfW, float[] halfH,
        Random rng)
    {
        double cx = pos[j].x - pos[i].x;
        double cy = pos[j].y - pos[i].y;
        if (cx == 0 && cy == 0)
        {
            cx = (rng.NextDouble() - 0.5) * 0.01;
            cy = (rng.NextDouble() - 0.5) * 0.01;
        }

        double overlapX = halfW[i] + halfW[j] - Math.Abs(cx);
        double overlapY = halfH[i] + halfH[j] - Math.Abs(cy);

        if (overlapX > 0 && overlapY > 0)
        {
            if (overlapX < overlapY)
                return (Math.Sign(cx) != 0 ? Math.Sign(cx) : 1, 0, 0, overlapX, true);
            return (0, Math.Sign(cy) != 0 ? Math.Sign(cy) : 1, 0, overlapY, true);
        }

        double gapX = Math.Max(0, -overlapX);
        double gapY = Math.Max(0, -overlapY);
        double gap = Math.Max(Math.Sqrt(gapX * gapX + gapY * gapY), 1e-3);
        double centerLen = Math.Max(Math.Sqrt(cx * cx + cy * cy), 1e-9);
        return (cx / centerLen, cy / centerLen, gap, 0, false);
    }

    /// <summary>
    /// Resolves box overlaps by pushing the later box (in y/x order) fully
    /// past the earlier one along the axis of least penetration — moves are
    /// direction-aware (not always rightward), so coupled neighborhoods stay
    /// together. Can oscillate in dense packs; callers use it for cleanup and
    /// rely on <see cref="EnsureSeparated"/> for the termination guarantee.
    /// </summary>
    private static void SeparatePairwise(
        (double x, double y)[] pos,
        float[] halfW,
        float[] halfH,
        double spacing)
    {
        int n = pos.Length;
        for (int guard = 0; guard < 100; guard++)
        {
            var order = Enumerable.Range(0, n)
                .OrderBy(i => pos[i].y - halfH[i])
                .ThenBy(i => pos[i].x - halfW[i])
                .ToArray();
            bool changed = false;

            for (int oi = 0; oi < n; oi++)
            for (int oj = oi + 1; oj < n; oj++)
            {
                int i = order[oi], j = order[oj];
                double cx = pos[j].x - pos[i].x;
                double cy = pos[j].y - pos[i].y;
                double overlapX = halfW[i] + halfW[j] + spacing - Math.Abs(cx);
                double overlapY = halfH[i] + halfH[j] + spacing - Math.Abs(cy);
                if (overlapX <= 0 || overlapY <= 0) continue;

                changed = true;
                if (overlapX < overlapY)
                    pos[j].x += overlapX * (cx > 0 ? 1 : cx < 0 ? -1 : 1);
                else
                    pos[j].y += overlapY * (cy > 0 ? 1 : cy < 0 ? -1 : 1);
            }

            if (!changed) return;
        }
    }

    /// <summary>
    /// Pushes overlapping rects apart by moving the later rect (in x-order)
    /// rightward past the earlier one. Rightward-only moves are monotone, so
    /// this converges in at most n sweeps (after sweep k, the first k+1 rects
    /// are mutually clean).
    /// </summary>
    private static void EnsureSeparated(Dictionary<string, Rect> rects, float spacing)
    {
        var keys = rects.Keys.ToList();
        for (int guard = 0; guard < keys.Count + 1; guard++)
        {
            var ordered = keys
                .OrderBy(k => rects[k].xMin)
                .ThenBy(k => rects[k].yMin)
                .ToList();
            bool changed = false;

            for (int i = 0; i < ordered.Count; i++)
            for (int j = i + 1; j < ordered.Count; j++)
            {
                var a = rects[ordered[i]];
                var b = rects[ordered[j]];
                float overlapX = Math.Min(a.xMax, b.xMax) - Math.Max(a.xMin, b.xMin) + spacing;
                float overlapY = Math.Min(a.yMax, b.yMax) - Math.Max(a.yMin, b.yMin) + spacing;
                if (overlapX <= 0 || overlapY <= 0) continue;

                rects[ordered[j]] = new Rect(b.X + overlapX, b.Y, b.Width, b.Height);
                changed = true;
            }

            if (!changed) return;
        }
    }
}
