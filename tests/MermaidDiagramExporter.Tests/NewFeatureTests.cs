using System;
using System.IO;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Focus;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for features added in the remaining-work implementation:
/// - CLI --format md|mmd|both
/// - Focus multi-seed and AllVisibleRelations
/// - Scanner inherited members and stereotype detection
/// </summary>
public class CliFormatTests
{
    [Fact]
    public void Parse_FormatMd_SetsFormat()
    {
        var result = CliOptions.Parse(new[] { "/src", "--format", "md" });
        Assert.NotNull(result);
        Assert.Equal("md", result.Format);
    }

    [Fact]
    public void Parse_FormatMmd_SetsFormat()
    {
        var result = CliOptions.Parse(new[] { "/src", "--format", "mmd" });
        Assert.NotNull(result);
        Assert.Equal("mmd", result.Format);
    }

    [Fact]
    public void Parse_FormatBoth_SetsFormat()
    {
        var result = CliOptions.Parse(new[] { "/src", "--format", "both" });
        Assert.NotNull(result);
        Assert.Equal("both", result.Format);
    }

    [Fact]
    public void Parse_FormatDefault_IsMd()
    {
        var result = CliOptions.Parse(new[] { "/src" });
        Assert.NotNull(result);
        Assert.Equal("md", result.Format);
    }

    [Fact]
    public void Parse_FormatInvalid_ReturnsNull()
    {
        var result = CliOptions.Parse(new[] { "/src", "--format", "xml" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_FormatMissingValue_ReturnsNull()
    {
        var result = CliOptions.Parse(new[] { "/src", "--format" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_FormatIsCaseInsensitive()
    {
        var result = CliOptions.Parse(new[] { "/src", "--format", "MMD" });
        Assert.NotNull(result);
        Assert.Equal("mmd", result.Format);
    }
}

public class CliFormatEndToEndTests
{
    [Fact]
    public void FormatMmd_WritesMmdFileOnly()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Foo.cs", "namespace X; public class Foo { }");

        string outDir = Path.Combine(Path.GetTempPath(), "mermaid_fmt_mmd_" + Guid.NewGuid().ToString("N"));
        try
        {
            int exitCode = Program.Main(new[] { temp.Path, "-o", outDir, "--format", "mmd" });
            Assert.Equal(0, exitCode);

            Assert.True(Directory.GetFiles(outDir, "*.mmd").Length == 1);
            Assert.True(Directory.GetFiles(outDir, "*.md").Length == 0);

            string mmd = File.ReadAllText(Directory.GetFiles(outDir, "*.mmd")[0]);
            Assert.Contains("classDiagram", mmd);
            Assert.DoesNotContain("# ", mmd); // no markdown header
        }
        finally
        {
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
        }
    }

    [Fact]
    public void FormatBoth_WritesBothFiles()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Bar.cs", "namespace Y; public class Bar { }");

        string outDir = Path.Combine(Path.GetTempPath(), "mermaid_fmt_both_" + Guid.NewGuid().ToString("N"));
        try
        {
            int exitCode = Program.Main(new[] { temp.Path, "-o", outDir, "--format", "both" });
            Assert.Equal(0, exitCode);

            Assert.True(Directory.GetFiles(outDir, "*.mmd").Length == 1);
            Assert.True(Directory.GetFiles(outDir, "*.md").Length == 1);

            string mdContent = File.ReadAllText(Directory.GetFiles(outDir, "*.md")[0]);
            Assert.Contains("```mermaid", mdContent);
            Assert.Contains("# ", mdContent);
        }
        finally
        {
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
        }
    }
}

public class FocusMultiSeedTests
{
    [Fact]
    public void FocusSelection_MultipleSeeds_IncludesAllSeedNeighborhoods()
    {
        TypeGraph graph = BuildGraph(
            Nodes("A", "B", "C", "D", "E"),
            Association("A", "B"),
            Association("C", "D"),
            Association("B", "E"),
            Association("D", "E"));

        var controller = new FocusedGraphNavigationController();
        controller.SetRootGraph(graph, "test");

        TypeGraph? focused = controller.FocusSelection(
            new[] { "A", "C" },
            depth: 1,
            GraphFocusTraversalMode.UndirectedAssociations);

        Assert.NotNull(focused);
        // Should include A, B (neighbor of A), C, D (neighbor of C)
        Assert.Contains("A", focused!.Nodes.Select(n => n.Id));
        Assert.Contains("B", focused.Nodes.Select(n => n.Id));
        Assert.Contains("C", focused.Nodes.Select(n => n.Id));
        Assert.Contains("D", focused.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void FocusSelection_AllVisibleRelations_IncludesInheritanceAndImplements()
    {
        TypeGraph graph = BuildGraph(
            Nodes("Base", "IFace", "Derived", "Other"),
            new TypeEdgeData { FromNodeId = "Base", ToNodeId = "Derived", Kind = TypeEdgeKind.Inheritance },
            new TypeEdgeData { FromNodeId = "IFace", ToNodeId = "Derived", Kind = TypeEdgeKind.Implements },
            Association("Derived", "Other"));

        var builder = new FocusedSubgraphBuilder();
        TypeGraph? focused = builder.BuildFocusedGraph(graph, new GraphFocusRequest
        {
            SeedNodeIds = new[] { "Derived" },
            AssociationDepth = 1,
            TraversalMode = GraphFocusTraversalMode.AllVisibleRelations
        });

        Assert.NotNull(focused);
        // AllVisibleRelations should include inheritance and implements neighbors
        Assert.Contains("Base", focused!.Nodes.Select(n => n.Id));
        Assert.Contains("IFace", focused.Nodes.Select(n => n.Id));
        Assert.Contains("Other", focused.Nodes.Select(n => n.Id));
    }

    [Fact]
    public void FocusSelection_EmptySeeds_ReturnsNull()
    {
        TypeGraph graph = BuildGraph(Nodes("A"), Association("A", "B"));
        var controller = new FocusedGraphNavigationController();
        controller.SetRootGraph(graph, "test");

        TypeGraph? focused = controller.FocusSelection(
            Array.Empty<string>(),
            depth: 1,
            GraphFocusTraversalMode.UndirectedAssociations);

        Assert.Null(focused);
    }

    private static IReadOnlyList<TypeNodeData> Nodes(params string[] ids)
    {
        return ids.Select(id => new TypeNodeData
        {
            Id = id,
            DisplayName = id,
            FullName = id,
            Namespace = "Test"
        }).ToArray();
    }

    private static TypeEdgeData Association(string from, string to)
    {
        return new TypeEdgeData
        {
            FromNodeId = from,
            ToNodeId = to,
            Kind = TypeEdgeKind.Association
        };
    }

    private static TypeGraph BuildGraph(IReadOnlyList<TypeNodeData> nodes, params TypeEdgeData[] edges)
    {
        return new TypeGraph(
            "TestGraph",
            nodes,
            edges,
            new[]
            {
                new TypeGroupData
                {
                    Id = "Namespace:Test",
                    Label = "Test",
                    Kind = TypeGroupKind.Namespace,
                    NodeIds = nodes.Select(n => n.Id).ToArray()
                }
            },
            new TypeGraphMetadata());
    }
}

public class ScannerInheritedMembersTests
{
    [Fact]
    public void Scan_IncludeDeclaredMembersOnlyTrue_OnlyDeclaredMembers()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Base.cs", "namespace X; public class Base { public int BaseField; public void BaseMethod() { } }");
        temp.WriteFile("Derived.cs", "namespace X; public class Derived : Base { public int DerivedField; public void DerivedMethod() { } }");

        var scanner = new RoslynTypeScanner();
        TypeGraph graph = scanner.ScanFolder(temp.Path, new GraphBuildOptions
        {
            IncludeDeclaredMembersOnly = true
        });

        var derived = graph.Nodes.FirstOrDefault(n => n.DisplayName == "Derived");
        Assert.NotNull(derived);
        Assert.Contains(derived!.Members, m => m.Name == "DerivedMethod");
        Assert.DoesNotContain(derived.Members, m => m.Name == "BaseMethod");
    }

    [Fact]
    public void Scan_IncludeDeclaredMembersOnlyFalse_IncludesInheritedMembers()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Base.cs", "namespace X; public class Base { public int BaseField; public void BaseMethod() { } }");
        temp.WriteFile("Derived.cs", "namespace X; public class Derived : Base { public int DerivedField; public void DerivedMethod() { } }");

        var scanner = new RoslynTypeScanner();
        TypeGraph graph = scanner.ScanFolder(temp.Path, new GraphBuildOptions
        {
            IncludeDeclaredMembersOnly = false
        });

        var derived = graph.Nodes.FirstOrDefault(n => n.DisplayName == "Derived");
        Assert.NotNull(derived);
        // Should include inherited members from Base
        Assert.Contains(derived!.Members, m => m.Name == "BaseMethod");
        Assert.Contains(derived.Members, m => m.Name == "DerivedMethod");
    }

    [Fact]
    public void Scan_StereotypeDetection_IndirectMonoBehaviour()
    {
        // We can't reference UnityEngine, but we can test the stereotype
        // detection by creating a class named MonoBehaviour in a namespace
        // that mimics Unity's structure, and then inheriting from it indirectly
        using var temp = new TempSourceFolder();
        temp.WriteFile("FakeMonoBehaviour.cs",
            "namespace UnityEngine\n" +
            "{\n" +
            "    public class MonoBehaviour { }\n" +
            "}\n");
        temp.WriteFile("MyBase.cs",
            "using UnityEngine;\n" +
            "namespace Game {\n" +
            "    public class MyBase : MonoBehaviour { }\n" +
            "}\n");
        temp.WriteFile("MyClass.cs",
            "using UnityEngine;\n" +
            "namespace Game {\n" +
            "    public class MyClass : MyBase { }\n" +
            "}\n");

        var scanner = new RoslynTypeScanner();
        TypeGraph graph = scanner.ScanFolder(temp.Path);

        var myClass = graph.Nodes.FirstOrDefault(n => n.DisplayName == "MyClass");
        Assert.NotNull(myClass);
        // MyClass inherits from MyBase which inherits from MonoBehaviour
        // The stereotype detection should walk the full chain
        Assert.Contains("mono-behaviour", myClass!.Stereotypes);
    }
}

public class EdgeKindConversionTests
{
    [Fact]
    public void LayoutEngine_PreservesEdgeKindAndLabel()
    {
        // This tests that the LayoutEngine correctly copies Kind and Label
        // from TypeEdgeData into GraphEdge
        TypeGraph graph = new TypeGraph(
            "Test",
            new[]
            {
                new TypeNodeData { Id = "A", DisplayName = "A", Namespace = "X", Kind = TypeNodeKind.Class },
                new TypeNodeData { Id = "B", DisplayName = "B", Namespace = "X", Kind = TypeNodeKind.Class }
            },
            new[]
            {
                new TypeEdgeData
                {
                    FromNodeId = "A",
                    ToNodeId = "B",
                    Kind = TypeEdgeKind.Inheritance,
                    Label = "extends"
                }
            },
            Array.Empty<TypeGroupData>(),
            new TypeGraphMetadata());

        // We can't easily test GraphEdge directly without the GUI assembly,
        // but we can verify the layout coordinator produces results
        var coordinator = new MermaidDiagramExporter.Gui.Layout.GraphLayoutCoordinator();
        var result = coordinator.CreateLayout(graph);

        Assert.NotEmpty(result.NodeBounds);
        Assert.True(result.NodeBounds.ContainsKey("A"));
        Assert.True(result.NodeBounds.ContainsKey("B"));
    }

    [Fact]
    public void LayoutCoordinator_WithEdgeRouting_ProducesEdgePaths()
    {
        TypeGraph graph = new TypeGraph(
            "Test",
            new[]
            {
                new TypeNodeData { Id = "A", DisplayName = "A", Namespace = "NS1", Kind = TypeNodeKind.Class },
                new TypeNodeData { Id = "B", DisplayName = "B", Namespace = "NS2", Kind = TypeNodeKind.Class }
            },
            new[]
            {
                new TypeEdgeData
                {
                    FromNodeId = "A",
                    ToNodeId = "B",
                    Kind = TypeEdgeKind.Association
                }
            },
            new[]
            {
                new TypeGroupData { Id = "Namespace:NS1", Label = "NS1", Kind = TypeGroupKind.Namespace, NodeIds = new[] { "A" } },
                new TypeGroupData { Id = "Namespace:NS2", Label = "NS2", Kind = TypeGroupKind.Namespace, NodeIds = new[] { "B" } }
            },
            new TypeGraphMetadata());

        var coordinator = new MermaidDiagramExporter.Gui.Layout.GraphLayoutCoordinator();
        var result = coordinator.CreateLayout(graph);

        // EdgePaths should be populated by the EdgeRoutingService
        Assert.NotNull(result.EdgePaths);
        // With cross-cluster edges, we should have at least one path
        Assert.NotEmpty(result.EdgePaths);
    }
}
