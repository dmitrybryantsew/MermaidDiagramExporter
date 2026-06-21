using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Export;
using MermaidDiagramExporter.Extraction;

namespace MermaidDiagramExporter;

public static class Program
{
    public static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            var opts = CliOptions.Parse(args);
            if (opts == null)
            {
                PrintUsage();
                return 1;
            }

            Console.WriteLine($"Scanning: {opts.FolderPath}");
            var sw = Stopwatch.StartNew();

            var scanner = new RoslynTypeScanner();
            TypeGraph graph = scanner.ScanFolder(opts.FolderPath, opts.BuildOptions);

            Console.WriteLine($"Found {graph.Nodes.Count} types, {graph.Edges.Count} edges in {sw.ElapsedMilliseconds}ms");

            string mermaid = MermaidGraphExporter.BuildDiagram(graph);

            string outputDir = opts.OutputDir;
            Directory.CreateDirectory(outputDir);

            string safeName = MakeSafeFileName(graph.Title);
            string outputPath = Path.Combine(outputDir, safeName + ".md");

            File.WriteAllText(outputPath, "# " + graph.Title + "\n\n```mermaid\n" + mermaid + "\n```\n");

            Console.WriteLine($"Wrote: {outputPath}");

            if (opts.OpenAfter)
            {
                OpenInDefaultApp(outputPath);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    private static string MakeSafeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(title.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        if (string.IsNullOrWhiteSpace(safe)) safe = "diagram";
        return safe;
    }

    private static void OpenInDefaultApp(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            else if (OperatingSystem.IsMacOS())
                Process.Start("open", path);
            else
                Process.Start("xdg-open", path);
        }
        catch { /* best effort */ }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("MermaidDiagramExporter - Generate Mermaid class diagrams from C# source");
        Console.WriteLine();
        Console.WriteLine("Usage: mermaid-export <folder> [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  <folder>                  Folder containing .cs files (recursive)");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -o, --output <path>       Output directory (default: ./docs/mermaid)");
        Console.WriteLine("  --no-fields               Exclude fields");
        Console.WriteLine("  --no-properties           Exclude properties");
        Console.WriteLine("  --no-methods              Exclude methods");
        Console.WriteLine("  --no-interfaces           Exclude interface edges");
        Console.WriteLine("  --no-associations         Exclude association edges");
        Console.WriteLine("  --max-members <N>         Truncate members per node");
        Console.WriteLine("  --open                    Open output file after export");
        Console.WriteLine("  -h, --help                Show this help");
    }
}

internal sealed class CliOptions
{
    public string FolderPath { get; set; } = string.Empty;
    public string OutputDir { get; set; } = Path.Combine(Environment.CurrentDirectory, "docs", "mermaid");
    public bool OpenAfter { get; set; }
    public GraphBuildOptions BuildOptions { get; set; } = new GraphBuildOptions();

    public static CliOptions? Parse(string[] args)
    {
        var opts = new CliOptions();
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "-o":
                case "--output":
                    if (++i >= args.Length) return null;
                    opts.OutputDir = args[i];
                    break;
                case "--no-fields": opts.BuildOptions.IncludeFields = false; break;
                case "--no-properties": opts.BuildOptions.IncludeProperties = false; break;
                case "--no-methods": opts.BuildOptions.IncludeMethods = false; break;
                case "--no-interfaces": opts.BuildOptions.IncludeInterfaces = false; break;
                case "--no-associations": opts.BuildOptions.IncludeAssociations = false; break;
                case "--max-members":
                    if (++i >= args.Length) return null;
                    if (!int.TryParse(args[i], out int n)) return null;
                    opts.BuildOptions.MaxMemberCountPerNode = n;
                    break;
                case "--open": opts.OpenAfter = true; break;
                default:
                    if (a.StartsWith("-")) return null;
                    positional.Add(a);
                    break;
            }
        }

        if (positional.Count == 0) return null;
        opts.FolderPath = positional[0];

        if (!Path.IsPathRooted(opts.OutputDir))
            opts.OutputDir = Path.GetFullPath(opts.OutputDir);

        return opts;
    }
}
