using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Skia;
using SkiaSharp;
using MermaidDiagramExporter.Gui;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Core;
using Xunit;
using Xunit.Abstractions;

namespace MermaidDiagramExporter.Tests;

public class RenderingBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public RenderingBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Creates a synthetic graph with the specified number of nodes arranged in a grid.
    /// </summary>
    private static (List<GraphNode> nodes, List<GraphEdge> edges) CreateSyntheticGraph(int nodeCount)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();

        int cols = (int)Math.Ceiling(Math.Sqrt(nodeCount));
        float spacing = 200f;
        float nodeW = 160f;
        float nodeH = 80f;

        for (int i = 0; i < nodeCount; i++)
        {
            int row = i / cols;
            int col = i % cols;
            nodes.Add(new GraphNode
            {
                Id = $"Node{i}",
                DisplayName = $"Node{i}",
                Namespace = $"Namespace{row}",
                X = col * spacing,
                Y = row * spacing,
                Width = nodeW,
                Height = nodeH,
                Kind = "Class",
                Members = new List<GraphMember>
                {
                    new() { Name = "field1", TypeName = "int", Kind = "Field" },
                    new() { Name = "Method1", TypeName = "void", Kind = "Method" }
                }
            });
        }

        // Create some edges (each node connects to the next)
        for (int i = 0; i < nodeCount - 1; i++)
        {
            edges.Add(new GraphEdge
            {
                FromNode = nodes[i],
                ToNode = nodes[i + 1],
                Kind = TypeEdgeKind.Association
            });
        }

        return (nodes, edges);
    }

    [Fact]
    public void RenderingBenchmark_200Nodes_CompletesWithinThreshold()
    {
        const int nodeCount = 200;
        const int iterations = 50;
        const float maxAvgFrameTimeMs = 50f; // generous threshold

        var (nodes, edges) = CreateSyntheticGraph(nodeCount);

        // Create a headless surface
        int width = 1920;
        int height = 1080;
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);

        // Warm up
        using (var surface = SKSurface.Create(info))
        {
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x1A, 0x1E, 0x24));
        }

        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            using var surface = SKSurface.Create(info);
            var canvas = surface.Canvas;
            canvas.Clear(new SKColor(0x1A, 0x1E, 0x24));

            // Simulate the rendering pipeline (same as GraphCanvas.RenderNow)
            canvas.Save();
            canvas.Translate(40f, 40f);
            canvas.Scale(1.0f);

            // Draw edges (simplified)
            using var edgePaint = new SKPaint
            {
                Color = new SKColor(0x60, 0x60, 0x60),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.5f,
                IsAntialias = true
            };

            foreach (var edge in edges)
            {
                if (edge.FromNode == null || edge.ToNode == null) continue;
                float fromX = edge.FromNode.X + edge.FromNode.Width;
                float fromY = edge.FromNode.Y + edge.FromNode.Height / 2;
                float toX = edge.ToNode.X;
                float toY = edge.ToNode.Y + edge.ToNode.Height / 2;
                canvas.DrawLine(fromX, fromY, toX, toY, edgePaint);
            }

            // Draw nodes (simplified)
            using var fillPaint = new SKPaint { Color = new SKColor(0x2D, 0x33, 0x3F), Style = SKPaintStyle.Fill, IsAntialias = true };
            using var strokePaint = new SKPaint { Color = new SKColor(0x4A, 0x6A, 0x8A), Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f, IsAntialias = true };
            using var textPaint = new SKPaint { Color = new SKColor(0xE0, 0xE6, 0xEC), IsAntialias = true, TextSize = 12 };

            foreach (var node in nodes)
            {
                canvas.DrawRoundRect(node.X, node.Y, node.Width, node.Height, 6, 6, fillPaint);
                canvas.DrawRoundRect(node.X, node.Y, node.Width, node.Height, 6, 6, strokePaint);
                canvas.DrawText(node.DisplayName, node.X + 12, node.Y + 20, textPaint);
            }

            canvas.Restore();
            canvas.Flush();
        }

        sw.Stop();
        double avgMs = sw.Elapsed.TotalMilliseconds / iterations;
        _output.WriteLine($"Rendering benchmark: {nodeCount} nodes, {iterations} iterations");
        _output.WriteLine($"Average frame time: {avgMs:F2} ms");
        _output.WriteLine($"Total time: {sw.Elapsed.TotalMilliseconds:F0} ms");

        Assert.True(avgMs < maxAvgFrameTimeMs,
            $"Average frame time {avgMs:F2}ms exceeds threshold of {maxAvgFrameTimeMs}ms");
    }
}
