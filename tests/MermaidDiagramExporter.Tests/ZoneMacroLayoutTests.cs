using System;
using System.Collections.Generic;
using MermaidDiagramExporter.Gui.Layout;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Controlled experiments for <see cref="ZoneMacroLayout"/>: the force model
/// must bring coupled zone boxes to their spring equilibrium gap (~ideal gap)
/// and keep uncoupled boxes farther away, without overlaps.
/// </summary>
public class ZoneMacroLayoutTests
{
    [Fact]
    public void Macro_CoupledPairsRestNearIdealGap_UncoupledFarther()
    {
        var boxes = new List<(string Key, float Width, float Height)>
        {
            ("A", 400, 300),
            ("B", 400, 300),
            ("C", 400, 300),
            ("D", 400, 300),
        };
        var springs = new List<(int A, int B, float Weight)> { (0, 1, 5f), (2, 3, 5f) };

        var result = ZoneMacroLayout.Run(
            boxes, springs,
            spacing: 120f, marginX: 40f, marginY: 40f,
            iterations: 300, repulsionConstant: 0.7, springConstant: 1.5, seed: 42);

        float Gap(string a, string b)
        {
            var ra = result[a];
            var rb = result[b];
            float gx = Math.Max(0, Math.Max(ra.xMin, rb.xMin) - Math.Min(ra.xMax, rb.xMax));
            float gy = Math.Max(0, Math.Max(ra.yMin, rb.yMin) - Math.Min(ra.yMax, rb.yMax));
            return (float)Math.Sqrt(gx * gx + gy * gy);
        }

        float coupledAB = Gap("A", "B");
        float coupledCD = Gap("C", "D");
        float uncoupled = Math.Min(Gap("A", "C"), Math.Min(Gap("A", "D"), Math.Min(Gap("B", "C"), Gap("B", "D"))));

        Assert.True(coupledAB < 400f, $"Coupled A–B gap {coupledAB:F0} should rest near the ideal gap (~150)");
        Assert.True(coupledCD < 400f, $"Coupled C–D gap {coupledCD:F0} should rest near the ideal gap (~150)");
        Assert.True(coupledAB < uncoupled, $"Coupled gap ({coupledAB:F0}) should be smaller than uncoupled ({uncoupled:F0})");
    }

    [Fact]
    public void Macro_BoxesNeverOverlap()
    {
        var boxes = new List<(string Key, float Width, float Height)>
        {
            ("A", 1500, 900),
            ("B", 800, 1200),
            ("C", 2000, 500),
            ("D", 300, 300),
            ("E", 900, 900),
        };
        var springs = new List<(int A, int B, float Weight)> { (0, 1, 10f), (1, 2, 10f), (0, 2, 10f), (3, 4, 2f) };

        var result = ZoneMacroLayout.Run(
            boxes, springs,
            spacing: 120f, marginX: 40f, marginY: 40f,
            iterations: 300, repulsionConstant: 0.7, springConstant: 1.5, seed: 42);

        var rects = new List<Rect>(result.Values);
        for (int i = 0; i < rects.Count; i++)
        for (int j = i + 1; j < rects.Count; j++)
        {
            float ow = Math.Min(rects[i].xMax, rects[j].xMax) - Math.Max(rects[i].xMin, rects[j].xMin);
            float oh = Math.Min(rects[i].yMax, rects[j].yMax) - Math.Max(rects[i].yMin, rects[j].yMin);
            Assert.True(ow <= 2f || oh <= 2f,
                $"Boxes {i} and {j} overlap by {ow:F0}x{oh:F0}");
        }
    }
}
