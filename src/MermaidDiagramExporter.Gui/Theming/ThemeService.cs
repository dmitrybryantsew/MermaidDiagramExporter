using System;
using Avalonia;
using Avalonia.Styling;

namespace MermaidDiagramExporter.Gui.Theming;

/// <summary>
/// Owns the application theme. Controls the Fluent theme variant, the
/// chrome brush resources (sidebar, status bar, borders), and the active
/// <see cref="UiPalette"/>. <see cref="ThemeChanged"/> fires after every
/// <see cref="Apply"/> so renderers can redraw.
/// </summary>
public sealed class ThemeService
{
    private UiTheme _current = UiTheme.System;
    // The first Apply must always run — even when it re-applies the default
    // System theme — so chrome resources and the render palette match the
    // effective OS variant before the main window is created.
    private bool _hasApplied;

    /// <summary>
    /// The most recently applied theme.
    /// </summary>
    public UiTheme Current => _current;

    /// <summary>
    /// The palette matching the most recently applied theme. For
    /// <see cref="UiTheme.System"/> this resolves against the effective OS
    /// theme variant. Read by code that sets brushes dynamically (e.g.
    /// mode-toggle buttons in code-behind).
    /// </summary>
    public UiPalette ActivePalette => ResolvePalette(_current);

    /// <summary>
    /// Raised after the theme is applied. Subscribers should force a redraw
    /// of any cached pixel output (canvas, minimap, matrix).
    /// </summary>
    public event Action? ThemeChanged;

    /// <summary>
    /// Applies <paramref name="theme"/> to the running application. Safe to
    /// call before or after the main window is created.
    /// </summary>
    public void Apply(UiTheme theme)
    {
        if (_hasApplied && _current == theme)
            return;

        _current = theme;
        _hasApplied = true;

        ApplyCore();
    }

    /// <summary>
    /// Re-resolves the palette when the OS theme changes while the app runs.
    /// No-op unless the current theme is <see cref="UiTheme.System"/>.
    /// </summary>
    public void RefreshSystemTheme()
    {
        if (_current != UiTheme.System)
            return;

        ApplyCore();
    }

    private void ApplyCore()
    {
        var app = Application.Current;
        if (app != null)
        {
            // Set the Fluent variant FIRST: for System this resets
            // ActualThemeVariant back to the OS value, so the palette
            // resolution below never reads a previously forced variant.
            app.RequestedThemeVariant = _current switch
            {
                UiTheme.Dark => ThemeVariant.Dark,
                UiTheme.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };
        }

        var palette = ResolvePalette(_current);

        // Set the Skia palette BEFORE raising ThemeChanged so handlers that
        // draw immediately see the new colors.
        RenderPalette.SetCurrent(palette == UiPalette.Light
            ? RenderPalette.Light
            : RenderPalette.Dark);

        if (app != null)
            RewriteChromeBrushes(app, palette);

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Maps a theme to its palette. <see cref="UiTheme.System"/> follows the
    /// OS via <see cref="Application.ActualThemeVariant"/> (meaningful once
    /// RequestedThemeVariant is Default). Falls back to Dark when no
    /// application exists (unit tests).
    /// </summary>
    private static UiPalette ResolvePalette(UiTheme theme) => theme switch
    {
        UiTheme.Dark => UiPalette.Dark,
        UiTheme.Light => UiPalette.Light,
        _ => Application.Current?.ActualThemeVariant == ThemeVariant.Light
            ? UiPalette.Light
            : UiPalette.Dark,
    };

    /// <summary>
    /// Overwrites the chrome brush resources in <see cref="Application.Resources"/>
    /// with values from <paramref name="palette"/>. Controls using
    /// <c>{DynamicResource XyzBrush}</c> pick up the change automatically.
    /// </summary>
    private void RewriteChromeBrushes(Application app, UiPalette palette)
    {
        var resources = app.Resources;

        resources["ChromeBgBrush"] = palette.ChromeBg;
        resources["ChromeBgAltBrush"] = palette.ChromeBgAlt;
        resources["ChromeBorderBrush"] = palette.ChromeBorder;
        resources["ChromeTextBrush"] = palette.ChromeText;
        resources["ChromeTextMutedBrush"] = palette.ChromeTextMuted;
        resources["StatusBarBgBrush"] = palette.StatusBarBg;
        resources["StatusBarTextBrush"] = palette.StatusBarText;
        resources["AccentPrimaryBrush"] = palette.AccentPrimary;
        resources["AccentSecondaryBrush"] = palette.AccentSecondary;
        resources["AccentWarningBrush"] = palette.AccentWarning;
        resources["AccentAiBrush"] = palette.AccentAi;
        resources["AccentSelectionBrush"] = palette.AccentSelection;
        resources["ModeInactiveBgBrush"] = palette.ModeInactiveBg;
        resources["MatrixHeaderBgBrush"] = palette.MatrixHeaderBg;
        resources["MatrixHeaderTextBrush"] = palette.MatrixHeaderText;
        resources["MatrixCellBorderBrush"] = palette.MatrixCellBorder;
        resources["MatrixTextBrush"] = palette.MatrixText;
        resources["MatrixCellBgBrush"] = palette.MatrixCellBg;
        resources["MatrixCellAltBrush"] = palette.MatrixCellAlt;
        resources["MatrixHotCellBrush"] = palette.MatrixHotCell;
    }
}
