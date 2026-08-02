using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Xunit;
using Xunit.Abstractions;
using MermaidDiagramExporter.Gui.Layout;
using MermaidDiagramExporter.Gui;

namespace MermaidDiagramExporter.Tests
{
    public class ForceClusterSeparationBenchmarkTests
    {
        private readonly ITestOutputHelper _output;

        public ForceClusterSeparationBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void BenchmarkDepthCalculation()
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

            var clusterById = clusters.ToDictionary(c => c.Id);

            var swSlow = Stopwatch.StartNew();
            var depthByIdSlow = clusters.ToDictionary(c => c.Id, c => DepthSlow(c, clusters));
            swSlow.Stop();

            var swFast = Stopwatch.StartNew();
            var depthByIdFast = clusters.ToDictionary(c => c.Id, c => DepthFast(c, clusterById));
            swFast.Stop();

            _output.WriteLine($"Elapsed time (slow): {swSlow.ElapsedMilliseconds} ms");
            _output.WriteLine($"Elapsed time (fast): {swFast.ElapsedMilliseconds} ms");

            // The O(N^2) behavior is extreme. If N is 5000, 64 levels cap means 64 * 5000 = 320,000 max looks,
            // but each lookup scans up to 5000 items. (320,000 * 2500 avg = 800 million operations)
            // It clearly speeds it up significantly.
        }

        private static int DepthSlow(LayoutCluster cluster, IReadOnlyList<LayoutCluster> all)
        {
            int depth = 0;
            var current = cluster;
            for (int guard = 0; guard < 64 && !string.IsNullOrEmpty(current.ParentClusterId); guard++)
            {
                var parent = all.FirstOrDefault(c => c.Id == current.ParentClusterId);
                if (parent == null) break;
                depth++;
                current = parent;
            }
            return depth;
        }

        private static int DepthFast(LayoutCluster cluster, Dictionary<string, LayoutCluster> all)
        {
            int depth = 0;
            var current = cluster;
            for (int guard = 0; guard < 64 && !string.IsNullOrEmpty(current.ParentClusterId); guard++)
            {
                if (!all.TryGetValue(current.ParentClusterId, out var parent)) break;
                depth++;
                current = parent;
            }
            return depth;
        }
    }
}
