namespace MermaidDiagramExporter.Gui.Theming;

/// <summary>
/// App-global (not per-project) settings. Persisted to
/// <c>%LocalAppData%/MermaidDiagramExporter/app.settings.json</c> on Windows
/// or <c>~/.local/share/MermaidDiagramExporter/app.settings.json</c> on Linux.
/// </summary>
public sealed class AppSettings
{
    public UiTheme Theme { get; set; } = UiTheme.System;
}
