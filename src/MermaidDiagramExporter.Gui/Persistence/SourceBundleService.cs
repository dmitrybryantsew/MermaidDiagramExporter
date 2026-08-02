using System;
using System.Collections.Generic;
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
    private const long MaxSourceFileSizeBytes = 10 * 1024 * 1024; // 10 MB
    private readonly SettingsService _settingsService;

    public SourceBundleService(SettingsService settingsService)
    {
        _settingsService = settingsService;
    }

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
                var fileInfo = new FileInfo(file);
                if (fileInfo.Length > MaxSourceFileSizeBytes)
                {
                    sb.AppendLine($"[Skipped: file exceeds {MaxSourceFileSizeBytes / (1024 * 1024)} MB limit]");
                    continue;
                }
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

    /// <summary>
    /// Builds a source-bundle text from an explicit set of file paths (deduped,
    /// sorted). Used by the Analyze Mode "Get Code" context menu actions which
    /// target a specific subset of files (a class, a namespace, or a focused
    /// subgraph). Returns the assembled text without writing it to disk.
    /// </summary>
    public string BuildBundleFromFiles(IEnumerable<string> filePaths, string title, string? basePath = null)
    {
        var ordered = filePaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p))
            .Distinct()
            .OrderBy(p => p)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"# Source Bundle: {title}");
        sb.AppendLine($"# Generated: {DateTime.UtcNow:O}");
        sb.AppendLine($"# Files: {ordered.Count}");
        if (!string.IsNullOrEmpty(basePath))
            sb.AppendLine($"# Base Folder: {Path.GetFullPath(basePath)}");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();

        foreach (var file in ordered)
        {
            string label = string.IsNullOrEmpty(basePath)
                ? file
                : Path.GetRelativePath(basePath, file);
            sb.AppendLine($"--- FILE: {label} ---");
            try
            {
                var fileInfo = new FileInfo(file);
                if (!fileInfo.Exists)
                {
                    sb.AppendLine("[Skipped: file not found]");
                }
                else if (fileInfo.Length > MaxSourceFileSizeBytes)
                {
                    sb.AppendLine($"[Skipped: file exceeds {MaxSourceFileSizeBytes / (1024 * 1024)} MB limit]");
                    continue;
                }
                else
                {
                    sb.AppendLine(File.ReadAllText(file));
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Error reading file: {ex.Message}]");
            }
            sb.AppendLine();
            sb.AppendLine(new string('-', 60));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Writes a bundle text to the resolved source-bundle directory and returns
    /// the full path. Filename: code-bundle-{yyyyMMdd_HHmmss}.txt.
    /// </summary>
    public string SaveBundle(string content, ProjectSettings settings, string filePrefix = "code-bundle")
    {
        string outputDir = _settingsService.ResolveSourceBundleDirectory(settings);
        Directory.CreateDirectory(outputDir);

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string filePath = Path.Combine(outputDir, $"{filePrefix}-{timestamp}.txt");
        File.WriteAllText(filePath, content, Encoding.UTF8);
        return filePath;
    }
}
