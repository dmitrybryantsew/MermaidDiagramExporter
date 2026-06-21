using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Focus;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class FocusTests
{
    [Fact]
    public void BuildFocusedGraph_DepthOne_IncludesImmediateAssociationNeighbors()
    {
        TypeGraph graph = BuildGraph(
            Nodes("A", "B", "C"),
            Association("A", "B"),
            Association("B", "C"));

        var builder = new FocusedSubgraphBuilder();
        TypeGraph? focused = builder.BuildFocusedGraph(graph, new GraphFocusRequest
        {
            SeedNodeIds = new[] { "A" },
            AssociationDepth = 1
        });

        Assert.NotNull(focused);
        Assert.Equal(new[] { "A", "B" }, focused!.Nodes.Select(n => n.Id).OrderBy(id => id));
        Assert.Single(focused.Edges);
    }

    [Fact]
    public void BuildFocusedGraph_DepthTwo_WalksAssociationChain()
    {
        TypeGraph graph = BuildGraph(
            Nodes("A", "B", "C"),
            Association("A", "B"),
            Association("B", "C"));

        var builder = new FocusedSubgraphBuilder();
        TypeGraph? focused = builder.BuildFocusedGraph(graph, new GraphFocusRequest
        {
            SeedNodeIds = new[] { "A" },
            AssociationDepth = 2
        });

        Assert.NotNull(focused);
        Assert.Equal(new[] { "A", "B", "C" }, focused!.Nodes.Select(n => n.Id).OrderBy(id => id));
        Assert.Equal(2, focused.Edges.Count);
    }

    [Fact]
    public void BuildFocusedGraph_TraversalMode_RespectsAssociationDirection()
    {
        TypeGraph graph = BuildGraph(
            Nodes("A", "B", "C"),
            Association("A", "B"),
            Association("B", "C"));

        var builder = new FocusedSubgraphBuilder();
        TypeGraph? outgoing = builder.BuildFocusedGraph(graph, new GraphFocusRequest
        {
            SeedNodeIds = new[] { "B" },
            AssociationDepth = 1,
            TraversalMode = GraphFocusTraversalMode.OutgoingAssociationsOnly
        });
        TypeGraph? incoming = builder.BuildFocusedGraph(graph, new GraphFocusRequest
        {
            SeedNodeIds = new[] { "B" },
            AssociationDepth = 1,
            TraversalMode = GraphFocusTraversalMode.IncomingAssociationsOnly
        });

        Assert.NotNull(outgoing);
        Assert.NotNull(incoming);
        Assert.Equal(new[] { "B", "C" }, outgoing!.Nodes.Select(n => n.Id).OrderBy(id => id));
        Assert.Equal(new[] { "A", "B" }, incoming!.Nodes.Select(n => n.Id).OrderBy(id => id));
    }

    [Fact]
    public void NavigationController_BackForwardAndReset_RestoresSnapshots()
    {
        TypeGraph graph = BuildGraph(
            Nodes("A", "B", "C"),
            Association("A", "B"),
            Association("B", "C"));
        var controller = new FocusedGraphNavigationController();
        controller.SetRootGraph(graph, "test");

        TypeGraph? focusedA = controller.FocusSelection("A", 1, GraphFocusTraversalMode.UndirectedAssociations);
        TypeGraph? focusedB = controller.FocusSelection("B", 1, GraphFocusTraversalMode.UndirectedAssociations);

        Assert.NotNull(focusedA);
        Assert.NotNull(focusedB);
        Assert.True(controller.CanGoBack());

        GraphViewSnapshot? back = controller.GoBack();
        Assert.NotNull(back);
        Assert.Same(focusedA, controller.CurrentGraph);
        Assert.True(controller.CanGoForward());

        GraphViewSnapshot? forward = controller.GoForward();
        Assert.NotNull(forward);
        Assert.Same(focusedB, controller.CurrentGraph);

        TypeGraph? root = controller.ResetToRoot();
        Assert.Same(graph, root);
        Assert.False(controller.CanGoBack());
        Assert.False(controller.CanGoForward());
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
