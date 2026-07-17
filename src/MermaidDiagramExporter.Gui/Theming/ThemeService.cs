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

    /// <summary>
    /// The most recently applied theme.
    /// </summary>
    public UiTheme Current => _current;

    /// <summary>
    /// The palette matching the most recently applied theme. Read by code
    /// that sets brushes dynamically (e.g. mode-toggle buttons in code-behind).
    /// </summary>
    public UiPalette ActivePalette => UiPalette.For(_current);

    /// <summary>
    /// Raised after <see cref="Apply"/>. Subscribers should force a redraw of
    /// any cached pixel output (canvas, minimap, matrix).
    /// </summary>
    public event Action? ThemeChanged;

    /// <summary>
    /// Applies <paramref name="theme"/> to the running application. Safe to
    /// call before or after the main window is created.
    /// </summary>
    public void Apply(UiTheme theme)
    {
        if (_current == theme)
            return;

        _current = theme;

        // Set the Skia palette FIRST so any ThemeChanged handlers that draw
        // immediately see the new colors.
        RenderPalette.SetCurrent(UiPalette.For(theme) == UiPalette.Dark
            ? RenderPalette.Dark
            : RenderPalette.Light);

        var app = Application.Current;
        if (app != null)
        {
            app.RequestedThemeVariant = theme switch
            {
                UiTheme.Dark => ThemeVariant.Dark,
                UiTheme.Light => ThemeVariant.Light,
                _ => ThemeVariant.Default,
            };

            RewriteChromeBrushes(app);
        }

        ThemeChanged?.Invoke();
    }

    /// <summary>
    /// Overwrites the chrome brush resources in <see cref="Application.Resources"/>
    /// with values from the active <see cref="UiPalette"/>. Controls using
    /// <c>{DynamicResource XyzBrush}</c> pick up the change automatically.
    /// </summary>
    private void RewriteChromeBrushes(Application app)
    {
        var palette = ActivePalette;
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
