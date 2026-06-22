using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var settingsService = new SettingsService();
            var layoutEngine = new LayoutEngine();
            var scanner = new RoslynTypeScanner();
            desktop.MainWindow = new MainWindow(settingsService, layoutEngine, scanner);
        }

        base.OnFrameworkInitializationCompleted();
    }
}