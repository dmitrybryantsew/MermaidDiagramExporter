using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Export;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for MermaidGraphExporter — verifies the generated .mmd output is
/// syntactically valid Mermaid (no angle-bracket namespace labels, no
/// double-colon title issues, etc.).
/// </summary>
public class MermaidGraphExporterTests
{
    private static TypeGraph BuildGraphWithGlobalNamespace()
    {
        // Simulate a project with types in the global namespace (no namespace declaration).
        // The scanner sets Namespace="" for these and labels the group "Global Namespace".
        var graph = new TypeGraph(
            title: "Test Project",
            nodes: new[]
            {
                new TypeNodeData
                {
                    Id = "T_GlobalClass",
                    DisplayName = "GlobalClass",
                    Namespace = "", // global namespace
                    Kind = TypeNodeKind.Class,
                    Members = Array.Empty<TypeMemberData>()
                },
                new TypeNodeData
                {
                    Id = "T_NamespacedClass",
                    DisplayName = "NamespacedClass",
                    Namespace = "MyApp",
                    Kind = TypeNodeKind.Class,
                    Members = Array.Empty<TypeMemberData>()
                }
            },
            edges: Array.Empty<TypeEdgeData>(),
            groups: new[]
            {
                new TypeGroupData
                {
                    Id = "Namespace:global",
                    Label = "Global Namespace",
                    Kind = TypeGroupKind.Namespace,
                    NodeIds = new[] { "T_GlobalClass" }
                },
                new TypeGroupData
                {
                    Id = "Namespace:MyApp",
                    Label = "MyApp",
                    Kind = TypeGroupKind.Namespace,
                    NodeIds = new[] { "T_NamespacedClass" }
                }
            },
            metadata: new TypeGraphMetadata
            {
                GeneratedAtUtc = DateTime.UtcNow,
                SourceKind = GraphSourceKind.Folder,
                Options = new GraphBuildOptions(),
                SourceDescription = "test"
            });

        return graph;
    }

    [Fact]
    public void BuildDiagram_GlobalNamespace_DoesNotEmitAngleBracketLabels()
    {
        // Regression: Mermaid rejects `namespace <global namespace> {` because
        // angle brackets are not valid Mermaid syntax. The exporter must
        // sanitize the label.
        var graph = BuildGraphWithGlobalNamespace();
        var mmd = MermaidGraphExporter.BuildDiagram(graph);

        Assert.DoesNotContain("<global", mmd, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<global namespace>", mmd, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(">", mmd.Replace("classDiagram", "", StringComparison.Ordinal)
            .Replace("direction LR", "", StringComparison.Ordinal)
            .Replace("class ", "", StringComparison.Ordinal)
            .Replace("namespace ", "", StringComparison.Ordinal)
            .Replace("Global", "", StringComparison.Ordinal)
            .Replace("MyApp", "", StringComparison.Ordinal));
    }

    [Fact]
    public void BuildDiagram_GlobalNamespace_DoesNotEmitNamespaceWrapper()
    {
        // Global namespace types should NOT be wrapped in a `namespace ... { }` block
        // — they're just listed directly under classDiagram. The exporter must
        // not emit `namespace <global namespace> {` (which Mermaid rejects).
        var graph = BuildGraphWithGlobalNamespace();
        var mmd = MermaidGraphExporter.BuildDiagram(graph);

        Assert.DoesNotContain("namespace <", mmd);
        Assert.DoesNotContain("namespace Global", mmd);
        // The global class should still appear directly
        Assert.Contains("GlobalClass", mmd);
    }

    [Fact]
    public void BuildDiagram_RegularNamespace_PreservesLabel()
    {
        // Sanity check: a normal namespace should still appear with its original label.
        var graph = BuildGraphWithGlobalNamespace();
        var mmd = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("namespace MyApp", mmd);
    }

    [Fact]
    public void BuildDiagram_NamespaceWithAngleBrackets_Sanitized()
    {
        // Defensive: if a namespace label somehow contains angle brackets
        // (shouldn't happen with the current scanner, but the exporter should
        // be robust), they must be sanitized.
        var graph = new TypeGraph(
            title: "Test",
            nodes: new[]
            {
                new TypeNodeData
                {
                    Id = "T_Test",
                    DisplayName = "Test",
                    Namespace = "<weird>", // pathological input
                    Kind = TypeNodeKind.Class,
                    Members = Array.Empty<TypeMemberData>()
                }
            },
            edges: Array.Empty<TypeEdgeData>(),
            groups: new[]
            {
                new TypeGroupData
                {
                    Id = "Namespace:_weird_",
                    Label = "<weird>",
                    Kind = TypeGroupKind.Namespace,
                    NodeIds = new[] { "T_Test" }
                }
            },
            metadata: new TypeGraphMetadata
            {
                GeneratedAtUtc = DateTime.UtcNow,
                SourceKind = GraphSourceKind.Folder,
                Options = new GraphBuildOptions(),
                SourceDescription = "test"
            });

        var mmd = MermaidGraphExporter.BuildDiagram(graph);

        Assert.DoesNotContain("<weird>", mmd);
        Assert.Contains("namespace _weird_", mmd);
    }

    [Fact]
    public void BuildDiagram_HasValidMermaidStructure()
    {
        // Basic structural check: the output should start with the Mermaid
        // header and contain the expected sections.
        var graph = BuildGraphWithGlobalNamespace();
        var mmd = MermaidGraphExporter.BuildDiagram(graph);

        Assert.StartsWith("---", mmd.TrimStart());
        Assert.Contains("classDiagram", mmd);
        Assert.Contains("direction LR", mmd);
    }

    [Fact]
    public void BuildDiagram_TitleWithColon_DoesNotBreakYamlFrontmatter()
    {
        // Regression: Mermaid's frontmatter uses YAML syntax. A title like
        // "Focused: Foo (depth 1)" gets parsed as a YAML mapping key, causing
        // "bad indentation of a mapping entry" errors. The exporter must
        // sanitize colons out of the title.
        var graph = new TypeGraph(
            title: "Focused: CharacterStatsViewModel (depth 1)",
            nodes: new[]
            {
                new TypeNodeData
                {
                    Id = "T_Test",
                    DisplayName = "Test",
                    Namespace = "MyApp",
                    Kind = TypeNodeKind.Class,
                    Members = Array.Empty<TypeMemberData>()
                }
            },
            edges: Array.Empty<TypeEdgeData>(),
            groups: new[]
            {
                new TypeGroupData
                {
                    Id = "Namespace:MyApp",
                    Label = "MyApp",
                    Kind = TypeGroupKind.Namespace,
                    NodeIds = new[] { "T_Test" }
                }
            },
            metadata: new TypeGraphMetadata
            {
                GeneratedAtUtc = DateTime.UtcNow,
                SourceKind = GraphSourceKind.Folder,
                Options = new GraphBuildOptions(),
                SourceDescription = "test"
            });

        var mmd = MermaidGraphExporter.BuildDiagram(graph);

        // The title line in the frontmatter must not contain raw colons
        // (which would break YAML parsing). After sanitization it should
        // contain " - " instead.
        Assert.Contains("title: Focused - CharacterStatsViewModel", mmd);
        // The raw colon-in-title pattern must not appear in the title line
        Assert.DoesNotContain("title: Focused: CharacterStatsViewModel", mmd);
    }

    [Fact]
    public void BuildDiagram_EmptyTitle_UsesFallback()
    {
        // An empty title should fall back to a safe default rather than
        // producing an empty `title:` line.
        var graph = new TypeGraph(
            title: "",
            nodes: new[]
            {
                new TypeNodeData
                {
                    Id = "T_Test",
                    DisplayName = "Test",
                    Namespace = "MyApp",
                    Kind = TypeNodeKind.Class,
                    Members = Array.Empty<TypeMemberData>()
                }
            },
            edges: Array.Empty<TypeEdgeData>(),
            groups: new[]
            {
                new TypeGroupData
                {
                    Id = "Namespace:MyApp",
                    Label = "MyApp",
                    Kind = TypeGroupKind.Namespace,
                    NodeIds = new[] { "T_Test" }
                }
            },
            metadata: new TypeGraphMetadata
            {
                GeneratedAtUtc = DateTime.UtcNow,
                SourceKind = GraphSourceKind.Folder,
                Options = new GraphBuildOptions(),
                SourceDescription = "test"
            });

        var mmd = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("title: Diagram", mmd);
    }
}
