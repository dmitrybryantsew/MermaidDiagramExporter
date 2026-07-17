using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using MermaidDiagramExporter.Extraction;
using MermaidDiagramExporter.Gui.Settings;
using MermaidDiagramExporter.Gui.Theming;

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
            var appSettingsService = new AppSettingsService();
            var layoutEngine = new LayoutEngine();
            var scanner = new RoslynTypeScanner();

            // Load and apply the saved theme BEFORE creating the main window so
            // the first frame is already themed (avoids a Light→Dark flash).
            var themeService = new ThemeService();
            themeService.Apply(appSettingsService.Load().Theme);

            desktop.MainWindow = new MainWindow(settingsService, appSettingsService, layoutEngine, scanner, themeService);
        }

        base.OnFrameworkInitializationCompleted();
    }
}