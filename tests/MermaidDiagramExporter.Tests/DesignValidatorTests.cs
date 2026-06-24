using MermaidDiagramExporter.Gui.Design;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for DesignValidator — structural validation of DesignGraph.
/// Per docs/design/05-data-model-and-persistence.md and
/// docs/design/07-implementation-phases.md M1 acceptance criteria.
/// </summary>
public class DesignValidatorTests
{
    [Fact]
    public void Validate_EmptyGraph_ReturnsNoErrors()
    {
        var graph = new DesignGraph();
        var errors = DesignValidator.Validate(graph);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_ValidGraph_ReturnsNoErrors()
    {
        var graph = new DesignGraph
        {
            Classes =
            {
                new DesignClass { Id = "a", Name = "Animal", Namespace = "Zoo" },
                new DesignClass { Id = "b", Name = "Dog", Namespace = "Zoo" }
            },
            Edges =
            {
                new DesignEdge { Id = "e1", FromClassId = "b", ToClassId = "a", Kind = EdgeKind.Inheritance }
            }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_DuplicateClassNamesInSameNamespace_ReturnsError()
    {
        var graph = new DesignGraph
        {
            Classes =
            {
                new DesignClass { Id = "a", Name = "Animal", Namespace = "Zoo" },
                new DesignClass { Id = "b", Name = "Animal", Namespace = "Zoo" } // duplicate name + namespace
            }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Contains(errors, e => e.Contains("Duplicate") && e.Contains("Animal") && e.Contains("Zoo"));
    }

    [Fact]
    public void Validate_SameClassNameInDifferentNamespaces_NoError()
    {
        // Same name is fine if namespaces differ — they're distinct types
        var graph = new DesignGraph
        {
            Classes =
            {
                new DesignClass { Id = "a", Name = "Animal", Namespace = "Zoo" },
                new DesignClass { Id = "b", Name = "Animal", Namespace = "Pets" }
            }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_EdgeWithMissingSourceClass_ReturnsError()
    {
        var graph = new DesignGraph
        {
            Classes = { new DesignClass { Id = "a", Name = "Animal" } },
            Edges = { new DesignEdge { Id = "e1", FromClassId = "ghost", ToClassId = "a", Kind = EdgeKind.Association } }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Contains(errors, e => e.Contains("missing source") && e.Contains("ghost"));
    }

    [Fact]
    public void Validate_EdgeWithMissingTargetClass_ReturnsError()
    {
        var graph = new DesignGraph
        {
            Classes = { new DesignClass { Id = "a", Name = "Animal" } },
            Edges = { new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "ghost", Kind = EdgeKind.Association } }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Contains(errors, e => e.Contains("missing target") && e.Contains("ghost"));
    }

    [Fact]
    public void Validate_SelfEdgeOnInheritance_ReturnsError()
    {
        var graph = new DesignGraph
        {
            Classes = { new DesignClass { Id = "a", Name = "Animal" } },
            Edges = { new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "a", Kind = EdgeKind.Inheritance } }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Contains(errors, e => e.Contains("inherit from or implement itself"));
    }

    [Fact]
    public void Validate_SelfEdgeOnImplements_ReturnsError()
    {
        var graph = new DesignGraph
        {
            Classes = { new DesignClass { Id = "a", Name = "IRunnable", Kind = ClassKind.Interface } },
            Edges = { new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "a", Kind = EdgeKind.Implements } }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Contains(errors, e => e.Contains("inherit from or implement itself"));
    }

    [Fact]
    public void Validate_SelfEdgeOnAssociation_NoError()
    {
        // Self-edges on Association are weird but not structurally invalid
        // (the validator only catches inheritance/implements self-edges).
        var graph = new DesignGraph
        {
            Classes = { new DesignClass { Id = "a", Name = "Animal" } },
            Edges = { new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "a", Kind = EdgeKind.Association } }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_InheritanceCycle_ReturnsError()
    {
        // A → B → A (cycle)
        var graph = new DesignGraph
        {
            Classes =
            {
                new DesignClass { Id = "a", Name = "A" },
                new DesignClass { Id = "b", Name = "B" }
            },
            Edges =
            {
                new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Inheritance },
                new DesignEdge { Id = "e2", FromClassId = "b", ToClassId = "a", Kind = EdgeKind.Inheritance }
            }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Contains(errors, e => e.Contains("Inheritance cycle"));
    }

    [Fact]
    public void Validate_DiamondInheritance_NoFalseCycle()
    {
        // A → B, A → C, B → D, C → D — diamond shape, no cycle
        var graph = new DesignGraph
        {
            Classes =
            {
                new DesignClass { Id = "a", Name = "A" },
                new DesignClass { Id = "b", Name = "B" },
                new DesignClass { Id = "c", Name = "C" },
                new DesignClass { Id = "d", Name = "D" }
            },
            Edges =
            {
                new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Inheritance },
                new DesignEdge { Id = "e2", FromClassId = "a", ToClassId = "c", Kind = EdgeKind.Inheritance },
                new DesignEdge { Id = "e3", FromClassId = "b", ToClassId = "d", Kind = EdgeKind.Inheritance },
                new DesignEdge { Id = "e4", FromClassId = "c", ToClassId = "d", Kind = EdgeKind.Inheritance }
            }
        };
        var errors = DesignValidator.Validate(graph);
        Assert.Empty(errors);
    }

    [Fact]
    public void IsValid_EmptyGraph_ReturnsTrue()
    {
        Assert.True(DesignValidator.IsValid(new DesignGraph()));
    }

    [Fact]
    public void IsValid_GraphWithErrors_ReturnsFalse()
    {
        var graph = new DesignGraph
        {
            Classes = { new DesignClass { Id = "a", Name = "Animal" } },
            Edges = { new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "a", Kind = EdgeKind.Inheritance } }
        };
        Assert.False(DesignValidator.IsValid(graph));
    }
}
