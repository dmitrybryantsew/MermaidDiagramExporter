using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Gui.Design;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for M5 export pipeline: DesignExporter.ToMermaid, ToCSharpStub,
/// ToTypeGraph. Per docs/design/07-implementation-phases.md M5 acceptance criteria.
/// </summary>
public class DesignExporterTests
{
    private static DesignGraph CreateSimpleGraph()
    {
        var graph = new DesignGraph { Title = "Test Zoo" };
        graph.Classes.Add(new DesignClass
        {
            Id = "animal",
            Name = "Animal",
            Namespace = "Zoo",
            Kind = ClassKind.Class,
            X = 10, Y = 20, Width = 200, Height = 100,
            Members =
            {
                new DesignMember { Kind = MemberKind.Field, Name = "Name", TypeName = "string", Visibility = Visibility.Public },
                new DesignMember { Kind = MemberKind.Method, Name = "Speak", TypeName = "void", Visibility = Visibility.Public }
            }
        });
        graph.Classes.Add(new DesignClass
        {
            Id = "dog",
            Name = "Dog",
            Namespace = "Zoo",
            Kind = ClassKind.Class,
            X = 300, Y = 20, Width = 200, Height = 100
        });
        graph.Edges.Add(new DesignEdge
        {
            Id = "e1",
            FromClassId = "dog",
            ToClassId = "animal",
            Kind = EdgeKind.Inheritance
        });
        return graph;
    }

    // ── ToTypeGraph ──

    [Fact]
    public void ToTypeGraph_PreservesTitle()
    {
        var graph = CreateSimpleGraph();
        var typeGraph = DesignExporter.ToTypeGraph(graph);
        Assert.Equal("Test Zoo", typeGraph.Title);
    }

    [Fact]
    public void ToTypeGraph_PreservesAllClasses()
    {
        var graph = CreateSimpleGraph();
        var typeGraph = DesignExporter.ToTypeGraph(graph);
        Assert.Equal(2, typeGraph.Nodes.Count);
        Assert.Contains(typeGraph.Nodes, n => n.Id == "animal");
        Assert.Contains(typeGraph.Nodes, n => n.Id == "dog");
    }

    [Fact]
    public void ToTypeGraph_PreservesAllEdges()
    {
        var graph = CreateSimpleGraph();
        var typeGraph = DesignExporter.ToTypeGraph(graph);
        Assert.Single(typeGraph.Edges);
        Assert.Equal("dog", typeGraph.Edges[0].FromNodeId);
        Assert.Equal("animal", typeGraph.Edges[0].ToNodeId);
        Assert.Equal(TypeEdgeKind.Inheritance, typeGraph.Edges[0].Kind);
    }

    [Fact]
    public void ToTypeGraph_PreservesNamespaceGroups()
    {
        var graph = CreateSimpleGraph();
        var typeGraph = DesignExporter.ToTypeGraph(graph);
        Assert.Single(typeGraph.Groups);
        Assert.Equal("Zoo", typeGraph.Groups[0].Label);
        Assert.Equal(TypeGroupKind.Namespace, typeGraph.Groups[0].Kind);
    }

    [Fact]
    public void ToTypeGraph_ConvertsClassKind()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "i1", Name = "IRunnable", Kind = ClassKind.Interface });
        graph.Classes.Add(new DesignClass { Id = "e1", Name = "Colors", Kind = ClassKind.Enum });
        graph.Classes.Add(new DesignClass { Id = "s1", Name = "Point", Kind = ClassKind.Struct });

        var typeGraph = DesignExporter.ToTypeGraph(graph);

        Assert.Equal(TypeNodeKind.Interface, typeGraph.Nodes.First(n => n.Id == "i1").Kind);
        Assert.Equal(TypeNodeKind.Enum, typeGraph.Nodes.First(n => n.Id == "e1").Kind);
        Assert.Equal(TypeNodeKind.Struct, typeGraph.Nodes.First(n => n.Id == "s1").Kind);
    }

    [Fact]
    public void ToTypeGraph_CollapsesAggregationToAssociation()
    {
        // Per docs/design/06: Dependency/Aggregation/Composition all collapse
        // to Association in TypeEdgeKind (lossy conversion).
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A" });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B" });
        graph.Edges.Add(new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Aggregation });
        graph.Edges.Add(new DesignEdge { Id = "e2", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Composition });
        graph.Edges.Add(new DesignEdge { Id = "e3", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Dependency });

        var typeGraph = DesignExporter.ToTypeGraph(graph);

        Assert.All(typeGraph.Edges, e => Assert.Equal(TypeEdgeKind.Association, e.Kind));
    }

    [Fact]
    public void ToTypeGraph_CollapsesConstructorAndEventToMethod()
    {
        // TypeMemberKind only has Field/Property/Method — Constructor and
        // Event collapse to Method.
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass
        {
            Id = "c1",
            Name = "Test",
            Members =
            {
                new DesignMember { Kind = MemberKind.Constructor, Name = "Test", TypeName = "" },
                new DesignMember { Kind = MemberKind.Event, Name = "Changed", TypeName = "EventHandler" }
            }
        });

        var typeGraph = DesignExporter.ToTypeGraph(graph);
        var members = typeGraph.Nodes[0].Members;

        Assert.All(members, m => Assert.Equal(TypeMemberKind.Method, m.Kind));
    }

    // ── ToMermaid ──

    [Fact]
    public void ToMermaid_ContainsClassDiagramHeader()
    {
        var graph = CreateSimpleGraph();
        var mermaid = DesignExporter.ToMermaid(graph);
        Assert.Contains("classDiagram", mermaid);
        Assert.Contains("direction LR", mermaid);
    }

    [Fact]
    public void ToMermaid_ContainsClassNames()
    {
        var graph = CreateSimpleGraph();
        var mermaid = DesignExporter.ToMermaid(graph);
        Assert.Contains("Animal", mermaid);
        Assert.Contains("Dog", mermaid);
    }

    [Fact]
    public void ToMermaid_ContainsInheritanceEdge()
    {
        var graph = CreateSimpleGraph();
        var mermaid = DesignExporter.ToMermaid(graph);
        Assert.Contains("<|--", mermaid); // Mermaid inheritance arrow
    }

    [Fact]
    public void ToMermaid_SanitizesColonsInTitle()
    {
        // Regression: title with colons breaks YAML frontmatter
        var graph = new DesignGraph { Title = "Focused: Foo (depth 1)" };
        graph.Classes.Add(new DesignClass { Id = "c1", Name = "Test" });
        var mermaid = DesignExporter.ToMermaid(graph);
        Assert.DoesNotContain("title: Focused:", mermaid);
        Assert.Contains("title: Focused - Foo", mermaid);
    }

    // ── ToCSharpStub ──

    [Fact]
    public void ToCSharpStub_ContainsNamespace()
    {
        var graph = CreateSimpleGraph();
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("namespace Zoo", stub);
    }

    [Fact]
    public void ToCSharpStub_ContainsClassDeclaration()
    {
        var graph = CreateSimpleGraph();
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("public class Animal", stub);
        Assert.Contains("public class Dog", stub);
    }

    [Fact]
    public void ToCSharpStub_ContainsInheritance()
    {
        var graph = CreateSimpleGraph();
        var stub = DesignExporter.ToCSharpStub(graph);
        // Dog inherits from Animal
        Assert.Contains("public class Dog : Animal", stub);
    }

    [Fact]
    public void ToCSharpStub_ContainsFieldsAndMethods()
    {
        var graph = CreateSimpleGraph();
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("public string Name", stub);
        Assert.Contains("public void Speak()", stub);
    }

    [Fact]
    public void ToCSharpStub_Interface_GeneratesInterfaceKeyword()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "i1", Name = "IRunnable", Kind = ClassKind.Interface });
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("public interface IRunnable", stub);
    }

    [Fact]
    public void ToCSharpStub_Enum_GeneratesEnumValues()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass
        {
            Id = "e1",
            Name = "Colors",
            Kind = ClassKind.Enum,
            Members =
            {
                new DesignMember { Kind = MemberKind.Field, Name = "Red", TypeName = "" },
                new DesignMember { Kind = MemberKind.Field, Name = "Green", TypeName = "" },
                new DesignMember { Kind = MemberKind.Field, Name = "Blue", TypeName = "" }
            }
        });
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("public enum Colors", stub);
        Assert.Contains("Red = 0", stub);
        Assert.Contains("Green = 1", stub);
        Assert.Contains("Blue = 2", stub);
    }

    [Fact]
    public void ToCSharpStub_StaticClass_GeneratesStaticKeyword()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass
        {
            Id = "s1",
            Name = "MathUtils",
            Kind = ClassKind.StaticClass,
            Members =
            {
                new DesignMember { Kind = MemberKind.Method, Name = "Add", TypeName = "int" }
            }
        });
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("public static class MathUtils", stub);
        Assert.Contains("public static int Add()", stub);
    }

    [Fact]
    public void ToCSharpStub_Constructor_GeneratesConstructorDeclaration()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass
        {
            Id = "c1",
            Name = "Animal",
            Members =
            {
                new DesignMember
                {
                    Kind = MemberKind.Constructor,
                    Name = "Animal",
                    TypeName = "",
                    Parameters = { new DesignParameter { Name = "name", TypeName = "string" } }
                }
            }
        });
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("public Animal(string name)", stub);
    }

    [Fact]
    public void ToCSharpStub_GlobalNamespace_NoNamespaceBlock()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "c1", Name = "GlobalClass", Namespace = "" });
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.DoesNotContain("namespace ", stub);
        Assert.Contains("public class GlobalClass", stub);
    }

    [Fact]
    public void ToCSharpStub_PrivateVisibility_GeneratesPrivateKeyword()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass
        {
            Id = "c1",
            Name = "Test",
            Members =
            {
                new DesignMember { Kind = MemberKind.Field, Name = "Secret", TypeName = "string", Visibility = Visibility.Private }
            }
        });
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("private string Secret", stub);
    }

    [Fact]
    public void ToCSharpStub_HasAutoGeneratedHeader()
    {
        var graph = CreateSimpleGraph();
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("// Auto-generated by MermaidDiagramExporter Design Mode", stub);
    }

    [Fact]
    public void ToCSharpStub_AlwaysIncludesSystemUsing()
    {
        var graph = CreateSimpleGraph();
        var stub = DesignExporter.ToCSharpStub(graph);
        Assert.Contains("using System;", stub);
    }
}
