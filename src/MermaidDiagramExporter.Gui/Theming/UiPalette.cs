using Avalonia.Media;

namespace MermaidDiagramExporter.Gui.Theming;

/// <summary>
/// Avalonia-bridge palette for chrome (sidebar, panels, status bar, borders).
/// Two instances: <see cref="Dark"/> and <see cref="Light"/>. The active
/// one is selected by <see cref="ThemeService.Apply"/> and consumed via
/// <c>{DynamicResource XyzBrush}</c> in XAML.
/// </summary>
public sealed class UiPalette
{
    public required string Name { get; init; }

    // Sidebar / inspector / toolbar backgrounds
    public required IBrush ChromeBg { get; init; }
    // Cards / alt panels
    public required IBrush ChromeBgAlt { get; init; }
    // Borders, splitters
    public required IBrush ChromeBorder { get; init; }
    // Primary text on chrome
    public required IBrush ChromeText { get; init; }
    // Muted/secondary text on chrome
    public required IBrush ChromeTextMuted { get; init; }
    // Status bar bg + text
    public required IBrush StatusBarBg { get; init; }
    public required IBrush StatusBarText { get; init; }

    // Accents (same in both themes)
    public required IBrush AccentPrimary { get; init; }   // green
    public required IBrush AccentSecondary { get; init; } // blue
    public required IBrush AccentWarning { get; init; }   // orange
    public required IBrush AccentAi { get; init; }        // purple
    public required IBrush AccentSelection { get; init; } // orange selection

    // Mode-toggle inactive button background (used by code-behind in UpdateModeUi)
    public required IBrush ModeInactiveBg { get; init; }

    // Matrix view colors (NamespaceMatrixView)
    public required IBrush MatrixHeaderBg { get; init; }
    public required IBrush MatrixHeaderText { get; init; }
    public required IBrush MatrixCellBorder { get; init; }
    public required IBrush MatrixText { get; init; }
    public required IBrush MatrixCellBg { get; init; }
    public required IBrush MatrixCellAlt { get; init; }
    public required IBrush MatrixHotCell { get; init; }

    public static UiPalette Dark { get; } = new()
    {
        Name = "Dark",
        ChromeBg = new SolidColorBrush(Color.Parse("#1E2329")),
        ChromeBgAlt = new SolidColorBrush(Color.Parse("#252B33")),
        ChromeBorder = new SolidColorBrush(Color.Parse("#3A4250")),
        ChromeText = new SolidColorBrush(Color.Parse("#E0E6EC")),
        ChromeTextMuted = new SolidColorBrush(Color.Parse("#889098")),
        StatusBarBg = new SolidColorBrush(Color.Parse("#15191E")),
        StatusBarText = new SolidColorBrush(Color.Parse("#889098")),
        AccentPrimary = new SolidColorBrush(Color.Parse("#4CAF50")),
        AccentSecondary = new SolidColorBrush(Color.Parse("#2196F3")),
        AccentWarning = new SolidColorBrush(Color.Parse("#FF9800")),
        AccentAi = new SolidColorBrush(Color.Parse("#9C27B0")),
        AccentSelection = new SolidColorBrush(Color.Parse("#FF8C00")),
        ModeInactiveBg = new SolidColorBrush(Color.Parse("#3A4250")),
        MatrixHeaderBg = new SolidColorBrush(Color.Parse("#252B33")),
        MatrixHeaderText = new SolidColorBrush(Color.Parse("#B0B8C4")),
        MatrixCellBorder = new SolidColorBrush(Color.Parse("#3A4250")),
        MatrixText = new SolidColorBrush(Color.Parse("#E0E4EA")),
        MatrixCellBg = new SolidColorBrush(Color.Parse("#1E242C")),
        MatrixCellAlt = new SolidColorBrush(Color.Parse("#1A1F26")),
        MatrixHotCell = new SolidColorBrush(Color.Parse("#FF6040")),
    };

    public static UiPalette Light { get; } = new()
    {
        Name = "Light",
        ChromeBg = new SolidColorBrush(Color.Parse("#F0F0F0")),
        ChromeBgAlt = new SolidColorBrush(Color.Parse("#FFFFFF")),
        ChromeBorder = new SolidColorBrush(Color.Parse("#D0D0D0")),
        ChromeText = new SolidColorBrush(Color.Parse("#1A1A1A")),
        ChromeTextMuted = new SolidColorBrush(Color.Parse("#666666")),
        StatusBarBg = new SolidColorBrush(Color.Parse("#E4E7EA")),
        StatusBarText = new SolidColorBrush(Color.Parse("#555555")),
        AccentPrimary = new SolidColorBrush(Color.Parse("#4CAF50")),
        AccentSecondary = new SolidColorBrush(Color.Parse("#2196F3")),
        AccentWarning = new SolidColorBrush(Color.Parse("#FF9800")),
        AccentAi = new SolidColorBrush(Color.Parse("#9C27B0")),
        AccentSelection = new SolidColorBrush(Color.Parse("#FF8C00")),
        ModeInactiveBg = new SolidColorBrush(Color.Parse("#3A4250")),
        MatrixHeaderBg = new SolidColorBrush(Color.Parse("#E4E7EA")),
        MatrixHeaderText = new SolidColorBrush(Color.Parse("#3A4250")),
        MatrixCellBorder = new SolidColorBrush(Color.Parse("#D0D0D0")),
        MatrixText = new SolidColorBrush(Color.Parse("#1A1A1A")),
        MatrixCellBg = new SolidColorBrush(Color.Parse("#FFFFFF")),
        MatrixCellAlt = new SolidColorBrush(Color.Parse("#F2F4F6")),
        MatrixHotCell = new SolidColorBrush(Color.Parse("#E8532F")),
    };

    // NOTE: no theme→palette mapping here on purpose. UiTheme.System must
    // resolve against the OS (Application.ActualThemeVariant), which only
    // ThemeService can do correctly. Use ThemeService.ActivePalette.
}
