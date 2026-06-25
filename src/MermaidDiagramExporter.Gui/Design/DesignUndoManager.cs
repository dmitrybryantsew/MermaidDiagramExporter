using System;
using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Undo/redo stack for Design Mode. Scoped to Design Mode only — Analyze
/// Mode has no undo (it's read-only). Switching modes clears the undo/redo
/// stack per docs/design/08 D7.
/// </summary>
public sealed class DesignUndoManager
{
    private readonly Stack<DesignCommand> _undoStack = new();
    private readonly Stack<DesignCommand> _redoStack = new();
    private const int MaxStackSize = 200;

    /// <summary>
    /// Fired when the undo/redo state changes (a command is pushed, undone,
    /// or redone). Subscribers can update UI buttons (e.g. enable/disable
    /// Undo/Redo menu items).
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// True if there is a command available to undo.
    /// </summary>
    public bool CanUndo => _undoStack.Count > 0;

    /// <summary>
    /// True if there is a command available to redo.
    /// </summary>
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Description of the command that would be undone next (for tooltips).
    /// </summary>
    public string? UndoDescription => _undoStack.Count > 0 ? _undoStack.Peek().Description : null;

    /// <summary>
    /// Description of the command that would be redone next.
    /// </summary>
    public string? RedoDescription => _redoStack.Count > 0 ? _redoStack.Peek().Description : null;

    /// <summary>
    /// Executes a command: applies it to the graph and pushes it onto the
    /// undo stack. Clears the redo stack (a new action invalidates redo history).
    /// </summary>
    public void Execute(DesignCommand command, DesignGraph graph)
    {
        command.Apply(graph);
        _undoStack.Push(command);
        // Cap the stack to prevent unbounded memory growth
        if (_undoStack.Count > MaxStackSize)
        {
            // Rebuild without the oldest
            var temp = _undoStack.ToArray();
            _undoStack.Clear();
            for (int i = MaxStackSize; i < temp.Length; i++)
                _undoStack.Push(temp[i]);
        }
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Undoes the most recent command. Returns true if a command was undone.
    /// </summary>
    public bool Undo(DesignGraph graph)
    {
        if (_undoStack.Count == 0) return false;
        var command = _undoStack.Pop();
        command.Undo(graph);
        _redoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Redoes the most recently undone command. Returns true if a command was redone.
    /// </summary>
    public bool Redo(DesignGraph graph)
    {
        if (_redoStack.Count == 0) return false;
        var command = _redoStack.Pop();
        command.Apply(graph);
        _undoStack.Push(command);
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>
    /// Clears both stacks. Called when switching modes or loading a new design.
    /// </summary>
    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
        StateChanged?.Invoke(this, EventArgs.Empty);
    }
}
