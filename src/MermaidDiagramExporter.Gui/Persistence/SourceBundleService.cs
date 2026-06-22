using System;
using System.IO;
using System.Linq;
using System.Text;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Persistence;

/// <summary>
/// Generates a consolidated .txt file containing all scanned .cs files with headers.
/// Ready to paste into LLM prompts for context.
/// </summary>
public sealed class SourceBundleService
{
    private readonly SettingsService _settingsService = new();

    /// <summary>
    /// Generates a source bundle and returns the path to the created file.
    /// The filename includes a timestamp: source-bundle-{yyyyMMdd_HHmmss}.txt
    /// </summary>
    public string GenerateBundle(string sourceFolderPath, ProjectSettings settings)
    {
        string outputDir = _settingsService.ResolveSourceBundleDirectory(settings);
        Directory.CreateDirectory(outputDir);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePath = Path.Combine(outputDir, $"source-bundle-{timestamp}.txt");

        var sourceFiles = Directory.EnumerateFiles(sourceFolderPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(f => f)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Source Bundle: {Path.GetFileName(sourceFolderPath)}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:O}");
        sb.AppendLine($"# Files: {sourceFiles.Count}");
        sb.AppendLine($"# Folder: {Path.GetFullPath(sourceFolderPath)}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (var file in sourceFiles)
        {
            string relativePath = Path.GetRelativePath(sourceFolderPath, file);
            sb.AppendLine($"--- FILE: {relativePath} ---");
            try
            {
                string content = File.ReadAllText(file);
                sb.AppendLine(content);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Error reading file: {ex.Message}]");
            }
            sb.AppendLine();
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();
        }

        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        return filePath;
    }
}
