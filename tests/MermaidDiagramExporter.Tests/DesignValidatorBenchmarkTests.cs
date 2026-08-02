using System;
using System.Collections.Generic;
using System.Diagnostics;
using MermaidDiagramExporter.Gui.Design;
using Xunit;
using Xunit.Abstractions;

namespace MermaidDiagramExporter.Tests;

public class DesignValidatorBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public DesignValidatorBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Validate_LargeGraph_Benchmark()
    {
        var graph = new DesignGraph();
        for (int i = 0; i < 10000; i++)
        {
            graph.Classes.Add(new DesignClass { Id = $"c{i}", Name = $"Class{i % 1000}", Namespace = $"Namespace{i / 1000}" });
            if (i > 0)
                graph.Edges.Add(new DesignEdge { Id = $"e{i}", FromClassId = $"c{i-1}", ToClassId = $"c{i}", Kind = EdgeKind.Association });
        }

        // Add a few duplicates
        graph.Classes.Add(new DesignClass { Id = "dup1", Name = "Class500", Namespace = "Namespace0" });
        graph.Classes.Add(new DesignClass { Id = "dup2", Name = "Class500", Namespace = "Namespace0" });
        graph.Classes.Add(new DesignClass { Id = "dup3", Name = "Class999", Namespace = "Namespace9" });

        // Warmup
        DesignValidator.Validate(graph);

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 100; i++)
        {
            DesignValidator.Validate(graph);
        }
        sw.Stop();

        _output.WriteLine($"Validation time for 100 iterations: {sw.ElapsedMilliseconds} ms");
    }
}
