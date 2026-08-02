using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Tracks recently opened design files (MRU order). Per docs/design/05
/// storage locations section: max 10 entries, stored in
/// ProjectSettings.RecentDesignFiles (added in a separate PR).
/// </summary>
public sealed class DesignRecentFiles
{
    private const int MaxEntries = 10;
    private readonly List<string> _files = new();

    /// <summary>
    /// Creates a new instance, loading from the provided list of paths.
    /// </summary>
    public DesignRecentFiles(IEnumerable<string>? files = null)
    {
        if (files != null)
        {
            _files.AddRange(files.Where(File.Exists).Take(MaxEntries));
        }
    }

    /// <summary>
    /// Current list of recent files, most-recent first.
    /// </summary>
    public IReadOnlyList<string> Files => _files;

    /// <summary>
    /// Adds a file to the recent list (or moves it to the top if already present).
    /// Silently ignores null, empty, or non-existent paths.
    /// </summary>
    public void Add(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;
        if (!File.Exists(filePath)) return;

        // Remove existing entry (case-insensitive on Windows)
        _files.RemoveAll(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
        // Insert at top
        _files.Insert(0, filePath);
        // Cap
        if (_files.Count > MaxEntries)
            _files.RemoveRange(MaxEntries, _files.Count - MaxEntries);
    }

    /// <summary>
    /// Removes a file from the recent list (e.g. after deletion).
    /// </summary>
    public void Remove(string filePath)
    {
        _files.RemoveAll(f => string.Equals(f, filePath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Clears all recent files.
    /// </summary>
    public void Clear() => _files.Clear();
}
