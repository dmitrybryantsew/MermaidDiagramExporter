using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using SkiaSharp;
using MermaidDiagramExporter.Gui.Design;

namespace MermaidDiagramExporter.Tests;

public class HitTestBenchmarkTests
{
    [Fact]
    public void Benchmark_HitTest()
    {
        var graph = new DesignGraph();
        var rectangles = new List<ClassRectangle>();

        // Add 10,000 classes
        for (int i = 0; i < 10000; i++)
        {
            var cls = new DesignClass
            {
                Id = "c" + i,
                Members = new List<DesignMember> { new DesignMember() }
            };
            graph.Classes.Add(cls);
            // Put rectangles far away except the last one
            var rect = new ClassRectangle(cls, graph)
            {
                X = i == 9999 ? 0 : 100000,
                Y = i == 9999 ? 0 : 100000,
                Width = 100, Height = 100
            };
            rectangles.Add(rect);
        }

        // We want to hit test a body hit on the LAST rectangle (which is checked FIRST by HitTest)
        // But we want FirstOrDefault to take a long time, so its ID is "c9999", at the end of graph.Classes.
        var targetRect = rectangles[9999];
        // 24 is DesignGeometry.HeaderHeight
        var hitPos = new SKPoint(targetRect.X + 10, targetRect.Y + 24 + 10);

        // Warmup
        for (int i = 0; i < 100; i++)
        {
            DesignHitTestService.HitTest(hitPos, rectangles);
        }

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10000; i++)
        {
            DesignHitTestService.HitTest(hitPos, rectangles);
        }
        sw.Stop();

        Console.WriteLine($"HitTest time for 10000 calls on N=10000 (worst case FirstOrDefault): {sw.ElapsedMilliseconds} ms");
    }
}
