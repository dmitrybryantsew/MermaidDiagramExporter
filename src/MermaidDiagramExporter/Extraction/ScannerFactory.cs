using System.IO;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Extraction;

public static class ScannerFactory
{
    public static ITypeScanner CreateScanner(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            if (File.Exists(Path.Combine(folderPath, "tsconfig.json")) ||
                File.Exists(Path.Combine(folderPath, "package.json")))
            {
                return new TypeScriptSubprocessScanner();
            }

            try
            {
                // Shallow check to avoid deep scanning into node_modules
                var tsFiles = Directory.EnumerateFiles(folderPath, "*.ts", SearchOption.TopDirectoryOnly).Take(1).Count();
                var csProjFiles = Directory.EnumerateFiles(folderPath, "*.csproj", SearchOption.TopDirectoryOnly).Take(1).Count();
                var slnFiles = Directory.EnumerateFiles(folderPath, "*.sln", SearchOption.TopDirectoryOnly).Take(1).Count();

                if (tsFiles > 0 && csProjFiles == 0 && slnFiles == 0)
                {
                    return new TypeScriptSubprocessScanner();
                }
            }
            catch
            {
                // Ignore exceptions (e.g., unauthorized access) and fallback
            }
        }

        return new RoslynTypeScanner();
    }
}
