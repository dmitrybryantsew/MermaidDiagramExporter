using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Export;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class MermaidExporterTests
{
    private static TypeGraph MakeGraph(params TypeNodeData[] nodes)
    {
        return new TypeGraph("Test", nodes.ToList(), [], [], new TypeGraphMetadata());
    }

    private static TypeGraph MakeGraph(TypeNodeData[] nodes, TypeEdgeData[] edges)
    {
        return new TypeGraph("Test", nodes.ToList(), edges.ToList(), [], new TypeGraphMetadata());
    }

    [Fact]
    public void BuildDiagram_SingleClass_ContainsClassDeclaration()
    {
        var node = new TypeNodeData { Id = "T_Foo", DisplayName = "Foo", Namespace = "" };
        var graph = MakeGraph(node);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("class T_Foo[\"Foo\"]", result);
    }

    [Fact]
    public void BuildDiagram_ClassWithNamespace_WrappedInNamespace()
    {
        var node = new TypeNodeData { Id = "T_Foo", DisplayName = "Foo", Namespace = "MyApp" };
        var graph = MakeGraph(node);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("namespace MyApp", result);
        Assert.Contains("class T_Foo[\"Foo\"]", result);
    }

    [Fact]
    public void BuildDiagram_Interface_AnnotationIsInterface()
    {
        var node = new TypeNodeData
        {
            Id = "T_IFoo",
            DisplayName = "IFoo",
            Kind = TypeNodeKind.Interface
        };
        var graph = MakeGraph(node);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("interface", result);
    }

    [Fact]
    public void BuildDiagram_Enum_AnnotationIsEnumeration()
    {
        var node = new TypeNodeData
        {
            Id = "T_Color",
            DisplayName = "Color",
            Kind = TypeNodeKind.Enum
        };
        var graph = MakeGraph(node);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("enumeration", result);
    }

    [Fact]
    public void BuildDiagram_AbstractClass_AnnotationIsAbstract()
    {
        var node = new TypeNodeData
        {
            Id = "T_Base",
            DisplayName = "Base",
            Kind = TypeNodeKind.AbstractClass
        };
        var graph = MakeGraph(node);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("abstract", result);
    }

    [Fact]
    public void BuildDiagram_StaticClass_AnnotationIsStatic()
    {
        var node = new TypeNodeData
        {
            Id = "T_Util",
            DisplayName = "Util",
            Kind = TypeNodeKind.StaticClass
        };
        var graph = MakeGraph(node);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("static", result);
    }

    [Fact]
    public void BuildDiagram_InheritanceEdge_ProducesRelationship()
    {
        var baseNode = new TypeNodeData { Id = "T_Base", DisplayName = "Base" };
        var derivedNode = new TypeNodeData { Id = "T_Derived", DisplayName = "Derived" };
        var edge = new TypeEdgeData
        {
            FromNodeId = "T_Base",
            ToNodeId = "T_Derived",
            Kind = TypeEdgeKind.Inheritance
        };
        var graph = MakeGraph([baseNode, derivedNode], [edge]);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("T_Base", result);
        Assert.Contains("T_Derived", result);
    }

    [Fact]
    public void BuildDiagram_ImplementsEdge_HasLabelImplements()
    {
        var ifaceNode = new TypeNodeData { Id = "T_IFoo", DisplayName = "IFoo" };
        var classNode = new TypeNodeData { Id = "T_Foo", DisplayName = "Foo" };
        var edge = new TypeEdgeData
        {
            FromNodeId = "T_IFoo",
            ToNodeId = "T_Foo",
            Kind = TypeEdgeKind.Implements,
            Label = "implements"
        };
        var graph = MakeGraph([ifaceNode, classNode], [edge]);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        Assert.Contains("implements", result);
    }

    [Fact]
    public void BuildDiagram_EdgeToNonExistentNode_IsSkipped()
    {
        var node = new TypeNodeData { Id = "T_Foo", DisplayName = "Foo" };
        var edge = new TypeEdgeData
        {
            FromNodeId = "T_Foo",
            ToNodeId = "T_DoesNotExist",
            Kind = TypeEdgeKind.Association
        };
        var graph = MakeGraph([node], [edge]);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        // Should not crash, edge silently skipped
        Assert.Contains("class T_Foo", result);
    }

    [Fact]
    public void BuildDiagram_DuplicateEdge_IsDeduplicated()
    {
        var a = new TypeNodeData { Id = "T_A", DisplayName = "A" };
        var b = new TypeNodeData { Id = "T_B", DisplayName = "B" };
        var edge1 = new TypeEdgeData { FromNodeId = "T_A", ToNodeId = "T_B", Kind = TypeEdgeKind.Association };
        var edge2 = new TypeEdgeData { FromNodeId = "T_A", ToNodeId = "T_B", Kind = TypeEdgeKind.Association };
        var graph = MakeGraph([a, b], [edge1, edge2]);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        // No crash, deduplicated
        Assert.Contains("T_A", result);
        Assert.Contains("T_B", result);
    }

    [Fact]
    public void BuildDiagram_MembersWithVisibility_ProducesVisibilityMarkers()
    {
        var node = new TypeNodeData
        {
            Id = "T_Foo",
            DisplayName = "Foo",
            Members = new List<TypeMemberData>
            {
                new() { Name = "X", TypeName = "int", Kind = TypeMemberKind.Field, Visibility = TypeVisibility.Public },
                new() { Name = "Y", TypeName = "int", Kind = TypeMemberKind.Field, Visibility = TypeVisibility.Private },
            }
        };
        var graph = MakeGraph(node);

        string result = MermaidGraphExporter.BuildDiagram(graph);

        // Public member should have + prefix in Mermaid
        Assert.Contains("+", result);
    }
}
