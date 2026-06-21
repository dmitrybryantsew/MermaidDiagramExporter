using MermaidDiagramExporter.Core;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class TypeGraphTests
{
    [Fact]
    public void Constructor_StoresAllArguments()
    {
        var nodes = new List<TypeNodeData> { new() { Id = "T_A", DisplayName = "A" } };
        var edges = new List<TypeEdgeData> { new() { FromNodeId = "T_A", ToNodeId = "T_B" } };
        var groups = new List<TypeGroupData> { new() { Id = "g1", Label = "ns" } };
        var metadata = new TypeGraphMetadata();

        var graph = new TypeGraph("title", nodes, edges, groups, metadata);

        Assert.Equal("title", graph.Title);
        Assert.Same(nodes, graph.Nodes);
        Assert.Same(edges, graph.Edges);
        Assert.Same(groups, graph.Groups);
        Assert.Same(metadata, graph.Metadata);
    }

    [Fact]
    public void Constructor_NullArguments_AreReplacedWithEmpty()
    {
        var graph = new TypeGraph(null, null, null, null, null);

        Assert.Equal(string.Empty, graph.Title);
        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Empty(graph.Groups);
        Assert.NotNull(graph.Metadata);
    }

    [Fact]
    public void TypeNodeData_DefaultsAreEmptyStrings()
    {
        var node = new TypeNodeData();

        Assert.Equal(string.Empty, node.Id);
        Assert.Equal(string.Empty, node.DisplayName);
        Assert.Equal(string.Empty, node.FullName);
        Assert.Equal(string.Empty, node.Namespace);
        Assert.Equal(string.Empty, node.AssemblyName);
        Assert.Equal(string.Empty, node.AssetPath);
        Assert.False(node.IsProjectType);
        Assert.Empty(node.Stereotypes);
        Assert.Empty(node.Members);
    }

    [Fact]
    public void GraphBuildOptions_DefaultsIncludeAllMemberTypes()
    {
        var opts = new GraphBuildOptions();

        Assert.True(opts.IncludeFields);
        Assert.True(opts.IncludeProperties);
        Assert.True(opts.IncludeMethods);
        Assert.True(opts.IncludeInterfaces);
        Assert.True(opts.IncludeAssociations);
        Assert.Equal(TypeGroupKind.Namespace, opts.PrimaryGroupKind);
        Assert.Equal(0, opts.MaxMemberCountPerNode);
    }
}
