using System.Linq;
using MermaidDiagramExporter.Gui.Design;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for M6 polish: DesignUndoManager (undo/redo), DesignCommand concrete
/// commands, and DesignRecentFiles. Per docs/design/07-implementation-phases.md
/// M6 acceptance criteria.
/// </summary>
public class DesignUndoRedoTests
{
    private static DesignGraph CreateGraph()
    {
        var graph = new DesignGraph { Title = "Test" };
        graph.Classes.Add(new DesignClass { Id = "c1", Name = "Animal", X = 10, Y = 20, Width = 200, Height = 100 });
        return graph;
    }

    // ── DesignUndoManager basics ──

    [Fact]
    public void UndoManager_New_CannotUndoOrRedo()
    {
        var manager = new DesignUndoManager();
        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
        Assert.Null(manager.UndoDescription);
        Assert.Null(manager.RedoDescription);
    }

    [Fact]
    public void UndoManager_Execute_CanUndo()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();

        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "new", Name = "New" }), graph);

        Assert.True(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void UndoManager_Execute_AppliesCommand()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        int initialCount = graph.Classes.Count;

        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "new", Name = "New" }), graph);

        Assert.Equal(initialCount + 1, graph.Classes.Count);
    }

    [Fact]
    public void UndoManager_Undo_ReversesCommand()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        var cls = new DesignClass { Id = "new", Name = "New" };
        manager.Execute(new DesignCommands.AddClass(cls), graph);
        Assert.Single(graph.Classes.Where(c => c.Id == "new"));

        var undone = manager.Undo(graph);

        Assert.True(undone);
        Assert.Empty(graph.Classes.Where(c => c.Id == "new"));
    }

    [Fact]
    public void UndoManager_Undo_CanRedo()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "new", Name = "New" }), graph);
        manager.Undo(graph);

        Assert.True(manager.CanRedo);
    }

    [Fact]
    public void UndoManager_Redo_ReappliesCommand()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "new", Name = "New" }), graph);
        manager.Undo(graph);
        Assert.Empty(graph.Classes.Where(c => c.Id == "new"));

        var redone = manager.Redo(graph);

        Assert.True(redone);
        Assert.Single(graph.Classes.Where(c => c.Id == "new"));
    }

    [Fact]
    public void UndoManager_ExecuteAfterUndo_ClearsRedo()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "a", Name = "A" }), graph);
        manager.Undo(graph);
        Assert.True(manager.CanRedo);

        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "b", Name = "B" }), graph);

        Assert.False(manager.CanRedo); // new action clears redo stack
    }

    [Fact]
    public void UndoManager_UndoEmpty_ReturnsFalse()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        Assert.False(manager.Undo(graph));
    }

    [Fact]
    public void UndoManager_RedoEmpty_ReturnsFalse()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        Assert.False(manager.Redo(graph));
    }

    [Fact]
    public void UndoManager_Clear_EmptiesBothStacks()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "a", Name = "A" }), graph);
        manager.Undo(graph);

        manager.Clear();

        Assert.False(manager.CanUndo);
        Assert.False(manager.CanRedo);
    }

    [Fact]
    public void UndoManager_StateChanged_FiresOnExecute()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        int fireCount = 0;
        manager.StateChanged += (_, _) => fireCount++;

        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "a", Name = "A" }), graph);

        Assert.True(fireCount >= 1);
    }

    [Fact]
    public void UndoManager_UndoDescription_ReturnsCommandDescription()
    {
        var manager = new DesignUndoManager();
        var graph = CreateGraph();
        manager.Execute(new DesignCommands.AddClass(new DesignClass { Id = "a", Name = "Animal" }), graph);

        Assert.Contains("Animal", manager.UndoDescription);
    }

    // ── Concrete command tests ──

    [Fact]
    public void AddClass_Undo_RemovesClassAndReferencingEdges()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A" });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B" });
        graph.Edges.Add(new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Association });

        var cmd = new DesignCommands.AddClass(new DesignClass { Id = "c", Name = "C" });
        cmd.Apply(graph);
        Assert.Equal(3, graph.Classes.Count);

        // Add an edge referencing "c"
        graph.Edges.Add(new DesignEdge { Id = "e2", FromClassId = "a", ToClassId = "c", Kind = EdgeKind.Association });
        Assert.Equal(2, graph.Edges.Count);

        cmd.Undo(graph);
        Assert.Equal(2, graph.Classes.Count);
        Assert.Single(graph.Edges); // only e1 remains
        Assert.DoesNotContain(graph.Edges, e => e.FromClassId == "c" || e.ToClassId == "c");
    }

    [Fact]
    public void RemoveClass_Undo_RestoresClassAndEdges()
    {
        var graph = new DesignGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A" });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B" });
        graph.Edges.Add(new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Association });

        var cmd = new DesignCommands.RemoveClass(graph, "b");
        cmd.Apply(graph);
        Assert.Single(graph.Classes);
        Assert.Empty(graph.Edges);

        cmd.Undo(graph);
        Assert.Equal(2, graph.Classes.Count);
        Assert.Single(graph.Edges);
        Assert.Equal("B", graph.Classes[1].Name); // restored at original index
    }

    [Fact]
    public void MoveClass_Undo_RestoresOriginalPosition()
    {
        var graph = CreateGraph();
        var cmd = new DesignCommands.MoveClass("c1", 10, 20, 100, 200);
        cmd.Apply(graph);
        Assert.Equal(100f, graph.Classes[0].X);
        Assert.Equal(200f, graph.Classes[0].Y);

        cmd.Undo(graph);
        Assert.Equal(10f, graph.Classes[0].X);
        Assert.Equal(20f, graph.Classes[0].Y);
    }

    [Fact]
    public void ResizeClass_Undo_RestoresOriginalSize()
    {
        var graph = CreateGraph();
        var cmd = new DesignCommands.ResizeClass("c1", 200, 100, 400, 300);
        cmd.Apply(graph);
        Assert.Equal(400f, graph.Classes[0].Width);

        cmd.Undo(graph);
        Assert.Equal(200f, graph.Classes[0].Width);
    }

    [Fact]
    public void RenameClass_Undo_RestoresOriginalName()
    {
        var graph = CreateGraph();
        var cmd = new DesignCommands.RenameClass("c1", "Animal", "Creature");
        cmd.Apply(graph);
        Assert.Equal("Creature", graph.Classes[0].Name);

        cmd.Undo(graph);
        Assert.Equal("Animal", graph.Classes[0].Name);
    }

    [Fact]
    public void AddEdge_Undo_RemovesEdge()
    {
        var graph = CreateGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A" });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B" });

        var cmd = new DesignCommands.AddEdge(new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Association });
        cmd.Apply(graph);
        Assert.Single(graph.Edges);

        cmd.Undo(graph);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public void ChangeEdgeType_Undo_RestoresOriginalType()
    {
        var graph = CreateGraph();
        graph.Classes.Add(new DesignClass { Id = "a", Name = "A" });
        graph.Classes.Add(new DesignClass { Id = "b", Name = "B" });
        graph.Edges.Add(new DesignEdge { Id = "e1", FromClassId = "a", ToClassId = "b", Kind = EdgeKind.Association });

        var cmd = new DesignCommands.ChangeEdgeType("e1", EdgeKind.Association, EdgeKind.Inheritance);
        cmd.Apply(graph);
        Assert.Equal(EdgeKind.Inheritance, graph.Edges[0].Kind);

        cmd.Undo(graph);
        Assert.Equal(EdgeKind.Association, graph.Edges[0].Kind);
    }

    [Fact]
    public void AddMember_Undo_RemovesMember()
    {
        var graph = CreateGraph();
        var member = new DesignMember { Kind = MemberKind.Field, Name = "Test", TypeName = "int" };

        var cmd = new DesignCommands.AddMember("c1", member);
        cmd.Apply(graph);
        Assert.Single(graph.Classes[0].Members);

        cmd.Undo(graph);
        Assert.Empty(graph.Classes[0].Members);
    }

    [Fact]
    public void RemoveMember_Undo_RestoresMember()
    {
        var graph = CreateGraph();
        graph.Classes[0].Members.Add(new DesignMember { Kind = MemberKind.Field, Name = "Test", TypeName = "int" });

        var cmd = new DesignCommands.RemoveMember(graph, "c1", 0);
        cmd.Apply(graph);
        Assert.Empty(graph.Classes[0].Members);

        cmd.Undo(graph);
        Assert.Single(graph.Classes[0].Members);
        Assert.Equal("Test", graph.Classes[0].Members[0].Name);
    }

    [Fact]
    public void RenameMember_Undo_RestoresOriginalName()
    {
        var graph = CreateGraph();
        graph.Classes[0].Members.Add(new DesignMember { Kind = MemberKind.Field, Name = "Old", TypeName = "int" });

        var cmd = new DesignCommands.RenameMember("c1", graph.Classes[0].Members[0].Id, "Old", "New");
        cmd.Apply(graph);
        Assert.Equal("New", graph.Classes[0].Members[0].Name);

        cmd.Undo(graph);
        Assert.Equal("Old", graph.Classes[0].Members[0].Name);
    }

    [Fact]
    public void ChangeMemberType_Undo_RestoresOriginalType()
    {
        var graph = CreateGraph();
        graph.Classes[0].Members.Add(new DesignMember { Kind = MemberKind.Field, Name = "X", TypeName = "int" });

        var cmd = new DesignCommands.ChangeMemberType("c1", graph.Classes[0].Members[0].Id, "int", "string");
        cmd.Apply(graph);
        Assert.Equal("string", graph.Classes[0].Members[0].TypeName);

        cmd.Undo(graph);
        Assert.Equal("int", graph.Classes[0].Members[0].TypeName);
    }

    [Fact]
    public void CycleMemberVisibility_Undo_RestoresOriginalVisibility()
    {
        var graph = CreateGraph();
        graph.Classes[0].Members.Add(new DesignMember { Kind = MemberKind.Field, Name = "X", TypeName = "int", Visibility = Visibility.Public });

        var cmd = new DesignCommands.CycleMemberVisibility("c1", graph.Classes[0].Members[0].Id, Visibility.Public, Visibility.Private);
        cmd.Apply(graph);
        Assert.Equal(Visibility.Private, graph.Classes[0].Members[0].Visibility);

        cmd.Undo(graph);
        Assert.Equal(Visibility.Public, graph.Classes[0].Members[0].Visibility);
    }

    [Fact]
    public void MoveMember_Undo_RestoresOriginalPosition()
    {
        var graph = CreateGraph();
        graph.Classes[0].Members.Add(new DesignMember { Kind = MemberKind.Field, Name = "A", TypeName = "int" });
        graph.Classes[0].Members.Add(new DesignMember { Kind = MemberKind.Field, Name = "B", TypeName = "int" });

        var memberId = graph.Classes[0].Members[0].Id;
        var cmd = new DesignCommands.MoveMember("c1", memberId, 0, 1);
        cmd.Apply(graph);
        Assert.Equal("B", graph.Classes[0].Members[0].Name);

        cmd.Undo(graph);
        Assert.Equal("A", graph.Classes[0].Members[0].Name);
    }
}
