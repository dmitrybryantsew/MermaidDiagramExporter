namespace MermaidDiagramExporter.Gui.Theming;

/// <summary>
/// Application-wide UI theme choice. <see cref="System"/> follows the OS
/// theme (Avalonia <c>ThemeVariant.Default</c>); <see cref="Dark"/> and
/// <see cref="Light"/> force the variant regardless of OS.
/// </summary>
public enum UiTheme
{
    System,
    Dark,
    Light,
}
