using System;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// The two operating modes of the application. Analyze Mode is the existing
/// scan-and-visualize workflow. Design Mode is the new authoring workflow
/// where the user draws class rectangles directly on the canvas.
/// </summary>
public enum AppMode
{
    Analyze,
    Design
}

/// <summary>
/// Owns the current operating mode and a placeholder DesignGraph for Design
/// Mode. M0 scaffold — full DesignGraph model comes in M1.
/// </summary>
public sealed class DesignModeController
{
    private AppMode _currentMode = AppMode.Analyze;

    /// <summary>
    /// Fires when <see cref="CurrentMode"/> changes.
    /// </summary>
    public event EventHandler<AppMode>? ModeChanged;

    /// <summary>
    /// The current operating mode. Default is Analyze (preserves existing
    /// behavior for users who never toggle to Design Mode).
    /// </summary>
    public AppMode CurrentMode
    {
        get => _currentMode;
        private set
        {
            if (_currentMode == value) return;
            _currentMode = value;
            ModeChanged?.Invoke(this, value);
        }
    }

    /// <summary>
    /// The current design graph. Null when in Analyze Mode or when Design Mode
    /// has just been entered without a startingFrom argument. M1: this is now
    /// a real <see cref="DesignGraph"/> (M0 used <c>object?</c> as a placeholder).
    /// </summary>
    public DesignGraph? CurrentDesign { get; private set; }

    /// <summary>
    /// Switches to Design Mode. Uses <paramref name="startingFrom"/> as the
    /// current design if non-null; otherwise keeps the previous design (so
    /// the user can toggle away and back without losing work).
    /// </summary>
    public void EnterDesignMode(DesignGraph? startingFrom = null)
    {
        if (startingFrom != null)
            CurrentDesign = startingFrom;
        CurrentMode = AppMode.Design;
    }

    /// <summary>
    /// Switches back to Analyze Mode. The design is kept in memory (not
    /// discarded) so the user can return without losing work — they'll be
    /// prompted to save on exit if dirty.
    /// </summary>
    public void EnterAnalyzeMode()
    {
        CurrentMode = AppMode.Analyze;
    }
}
