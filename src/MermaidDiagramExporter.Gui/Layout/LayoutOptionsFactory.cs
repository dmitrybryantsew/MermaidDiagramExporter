using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// The single place where <see cref="ProjectSettings"/> is mapped to
/// <see cref="LayoutOptions"/>. Replaces the four hand-copied construction
/// sites in MainWindow.axaml.cs (three of which silently dropped the
/// partition flags).
/// </summary>
public static class LayoutOptionsFactory
{
    /// <summary>
    /// Builds layout options from project settings. Numeric layout values
    /// use <see cref="LayoutOptions"/> defaults (they are not yet persisted
    /// per-project); only engine selection and engine-group settings map.
    /// </summary>
    public static LayoutOptions FromSettings(ProjectSettings settings)
    {
        if (settings == null) return new LayoutOptions();

        return new LayoutOptions
        {
            Engine = settings.Engine,
            Msagl = new MsaglEngineOptions
            {
                Partition = settings.Msagl?.PartitionMode ?? MsaglPartitionMode.None,
            },
            Force = new ForceEngineOptions
            {
                PreventClusterOverlap = settings.Force?.PreventClusterOverlap ?? true,
            },
            ZoneFirst = new ZoneFirstEngineOptions
            {
                MicroEngine = settings.ZoneFirst?.MicroEngine ?? ZoneFirstMicroEngine.Sugiyama,
                ZoneSpacing = settings.ZoneFirst?.ZoneSpacing ?? 120f,
            },
        };
    }
}
