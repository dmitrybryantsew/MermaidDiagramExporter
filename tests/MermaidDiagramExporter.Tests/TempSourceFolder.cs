using System.IO;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Creates a temporary folder with .cs files for scanner tests.
/// Disposed automatically - cleans up the folder.
/// </summary>
public sealed class TempSourceFolder : System.IDisposable
{
    public string Path { get; }

    public TempSourceFolder()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "MermaidDiagramExporter.Tests_" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    /// <summary>
    /// Write a .cs file to the folder. Subfolder is relative path with forward slashes.
    /// </summary>
    public string WriteFile(string relativePath, string content)
    {
        string normalized = relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar);
        string fullPath = System.IO.Path.Combine(Path, normalized);
        string dir = System.IO.Path.GetDirectoryName(fullPath)!;
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(fullPath, content);
        return fullPath;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
        catch { /* best effort */ }
    }
}
