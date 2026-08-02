using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Tests
{
    public class ClusterBoundsComputerBenchmarkTests
    {
        private readonly ITestOutputHelper _output;

        public ClusterBoundsComputerBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void BenchmarkCompute()
        {
            var clusters = new List<LayoutCluster>();
            int numClusters = 5000;

            for (int i = 0; i < numClusters; i++)
            {
                var cluster = new LayoutCluster
                {
                    Id = $"cluster_{i}",
                    ParentClusterId = i > 0 ? $"cluster_{i - 1}" : null,
                    NodeIds = Array.Empty<string>(),
                    ChildClusterIds = Array.Empty<string>()
                };
                clusters.Add(cluster);
            }

            var nodeBounds = new Dictionary<string, Rect>();
            var options = new LayoutOptions();

            var sw = Stopwatch.StartNew();
            ClusterBoundsComputer.Compute(clusters, nodeBounds, options);
            sw.Stop();

            _output.WriteLine($"Elapsed time: {sw.ElapsedMilliseconds} ms");
        }
    }
}
