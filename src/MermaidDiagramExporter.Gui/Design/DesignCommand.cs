using System;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Base class for undoable commands in Design Mode. Each command knows how
/// to apply itself (do) and reverse itself (undo). Commands are stored on
/// an undo stack; redo is the inverse of undo.
/// </summary>
public abstract class DesignCommand
{
    /// <summary>
    /// Human-readable description for debugging/UI display.
    /// </summary>
    public abstract string Description { get; }

    /// <summary>
    /// Applies the command to the graph.
    /// </summary>
    public abstract void Apply(DesignGraph graph);

    /// <summary>
    /// Reverses the command, restoring the graph to its prior state.
    /// </summary>
    public abstract void Undo(DesignGraph graph);
}
