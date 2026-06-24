using MermaidDiagramExporter.Gui.Design;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for DesignModeController — the M0 scaffold that owns the current
/// operating mode (Analyze vs Design) and a placeholder DesignGraph slot.
/// </summary>
public class DesignModeControllerTests
{
    [Fact]
    public void DefaultMode_IsAnalyze()
    {
        var controller = new DesignModeController();
        Assert.Equal(AppMode.Analyze, controller.CurrentMode);
    }

    [Fact]
    public void DefaultDesign_IsNull()
    {
        // M0 scaffold: no DesignGraph yet. M1 will populate this.
        var controller = new DesignModeController();
        Assert.Null(controller.CurrentDesign);
    }

    [Fact]
    public void EnterDesignMode_SwitchesToDesign()
    {
        var controller = new DesignModeController();
        controller.EnterDesignMode();
        Assert.Equal(AppMode.Design, controller.CurrentMode);
    }

    [Fact]
    public void EnterAnalyzeMode_SwitchesBackToAnalyze()
    {
        var controller = new DesignModeController();
        controller.EnterDesignMode();
        controller.EnterAnalyzeMode();
        Assert.Equal(AppMode.Analyze, controller.CurrentMode);
    }

    [Fact]
    public void EnterDesignMode_StoresStartingFrom()
    {
        var controller = new DesignModeController();
        var startingFrom = new object(); // M1 will use a real DesignGraph here
        controller.EnterDesignMode(startingFrom);
        Assert.Same(startingFrom, controller.CurrentDesign);
    }

    [Fact]
    public void EnterDesignMode_KeepsPreviousDesign_WhenStartingFromIsNull()
    {
        // Switching to Design without a startingFrom keeps the current design
        // (it doesn't clear it). This allows the user to return without
        // losing work.
        var controller = new DesignModeController();
        var firstDesign = new object();
        controller.EnterDesignMode(firstDesign);
        controller.EnterAnalyzeMode();
        controller.EnterDesignMode(); // no startingFrom
        Assert.Same(firstDesign, controller.CurrentDesign);
    }

    [Fact]
    public void ModeChanged_FiresOnTransition()
    {
        var controller = new DesignModeController();
        AppMode? capturedMode = null;
        controller.ModeChanged += (_, mode) => capturedMode = mode;

        controller.EnterDesignMode();
        Assert.Equal(AppMode.Design, capturedMode);

        controller.EnterAnalyzeMode();
        Assert.Equal(AppMode.Analyze, capturedMode);
    }

    [Fact]
    public void ModeChanged_DoesNotFire_WhenAlreadyInMode()
    {
        var controller = new DesignModeController();
        int fireCount = 0;
        controller.ModeChanged += (_, _) => fireCount++;

        controller.EnterAnalyzeMode(); // already Analyze, should not fire
        Assert.Equal(0, fireCount);

        controller.EnterDesignMode(); // transitions to Design, should fire once
        Assert.Equal(1, fireCount);

        controller.EnterDesignMode(); // already Design, should not fire
        Assert.Equal(1, fireCount);
    }
}
