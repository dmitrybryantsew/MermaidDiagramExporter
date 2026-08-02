using System;
using MermaidDiagramExporter.Gui.Theming;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for <see cref="ThemeService"/> palette mapping and apply semantics.
/// Pure logic: no <see cref="Avalonia.Application"/> exists in the test host,
/// so <see cref="UiTheme.System"/> resolves via the documented Dark fallback
/// and no Fluent variant or chrome resources are touched.
/// </summary>
public class ThemeServiceTests : IDisposable
{
    public ThemeServiceTests()
    {
        // RenderPalette.Current is static; isolate tests from each other.
        RenderPalette.SetCurrent(RenderPalette.Dark);
    }

    public void Dispose()
    {
        RenderPalette.SetCurrent(RenderPalette.Dark);
    }

    [Fact]
    public void Apply_Dark_SelectsDarkPalettesAndRaisesThemeChanged()
    {
        var svc = new ThemeService();
        int raised = 0;
        svc.ThemeChanged += () => raised++;

        svc.Apply(UiTheme.Dark);

        Assert.Equal(UiTheme.Dark, svc.Current);
        Assert.Same(UiPalette.Dark, svc.ActivePalette);
        Assert.Same(RenderPalette.Dark, RenderPalette.Current);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void Apply_Light_SelectsLightPalettes()
    {
        var svc = new ThemeService();
        svc.Apply(UiTheme.Light);

        Assert.Equal(UiTheme.Light, svc.Current);
        Assert.Same(UiPalette.Light, svc.ActivePalette);
        Assert.Same(RenderPalette.Light, RenderPalette.Current);
    }

    [Fact]
    public void Apply_SystemOnFirstCall_StillApplies()
    {
        // Regression test: System is both the field default and the persisted
        // default, so an early-return-on-equal guard used to skip the very
        // first apply — leaving chrome/render palettes mismatched with the
        // OS (the light-OS "white ListBox / dark-on-dark buttons" defect).
        var svc = new ThemeService();
        int raised = 0;
        svc.ThemeChanged += () => raised++;

        svc.Apply(UiTheme.System);

        Assert.Equal(UiTheme.System, svc.Current);
        Assert.Equal(1, raised);
        // No Application in tests → documented fallback is Dark.
        Assert.Same(RenderPalette.Dark, RenderPalette.Current);
    }

    [Fact]
    public void Apply_SameThemeTwice_SecondCallIsNoOp()
    {
        var svc = new ThemeService();
        svc.Apply(UiTheme.Dark);
        int raised = 0;
        svc.ThemeChanged += () => raised++;

        svc.Apply(UiTheme.Dark);

        Assert.Equal(0, raised);
    }

    [Fact]
    public void RefreshSystemTheme_NoOpForExplicitThemes()
    {
        var svc = new ThemeService();
        svc.Apply(UiTheme.Light);
        int raised = 0;
        svc.ThemeChanged += () => raised++;

        svc.RefreshSystemTheme();

        Assert.Equal(0, raised);
        Assert.Same(RenderPalette.Light, RenderPalette.Current);
    }

    [Fact]
    public void RefreshSystemTheme_WhenSystem_Reapplies()
    {
        var svc = new ThemeService();
        svc.Apply(UiTheme.System);
        int raised = 0;
        svc.ThemeChanged += () => raised++;

        svc.RefreshSystemTheme();

        Assert.Equal(1, raised);
    }
}
