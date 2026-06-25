using System.IO;
using System.Linq;
using MermaidDiagramExporter.Gui.Design;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for DesignRecentFiles (MRU list). Per docs/design/05 storage
/// locations section.
/// </summary>
public class DesignRecentFilesTests
{
    private static string CreateTempFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"recent-{Guid.NewGuid()}.dgraph.json");
        File.WriteAllText(path, "{}");
        return path;
    }

    [Fact]
    public void New_EmptyList()
    {
        var files = new DesignRecentFiles();
        Assert.Empty(files.Files);
    }

    [Fact]
    public void Add_AppendsToTop()
    {
        var files = new DesignRecentFiles();
        var path1 = CreateTempFile();
        var path2 = CreateTempFile();
        try
        {
            files.Add(path1);
            files.Add(path2);
            Assert.Equal(2, files.Files.Count);
            Assert.Equal(path2, files.Files[0]); // most recent first
            Assert.Equal(path1, files.Files[1]);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public void Add_Duplicate_MovesToTop()
    {
        var files = new DesignRecentFiles();
        var path1 = CreateTempFile();
        var path2 = CreateTempFile();
        try
        {
            files.Add(path1);
            files.Add(path2);
            files.Add(path1); // re-add path1

            Assert.Equal(2, files.Files.Count);
            Assert.Equal(path1, files.Files[0]); // moved to top
            Assert.Equal(path2, files.Files[1]);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public void Add_NonexistentFile_Ignored()
    {
        var files = new DesignRecentFiles();
        files.Add(@"C:\nonexistent\path\does-not-exist.dgraph.json");
        Assert.Empty(files.Files);
    }

    [Fact]
    public void Add_EmptyOrNull_Ignored()
    {
        var files = new DesignRecentFiles();
        files.Add("");
        files.Add("   ");
        files.Add(null!);
        Assert.Empty(files.Files);
    }

    [Fact]
    public void Add_CapsAt10Entries()
    {
        var files = new DesignRecentFiles();
        var paths = new string[15];
        try
        {
            for (int i = 0; i < 15; i++)
            {
                paths[i] = CreateTempFile();
                files.Add(paths[i]);
            }

            Assert.Equal(10, files.Files.Count);
            // Most recent (last added) should be first
            Assert.Equal(paths[14], files.Files[0]);
            // Oldest 5 should have been dropped
            Assert.DoesNotContain(files.Files, f => f == paths[0]);
        }
        finally
        {
            foreach (var p in paths)
                if (File.Exists(p)) File.Delete(p);
        }
    }

    [Fact]
    public void Remove_RemovesFile()
    {
        var files = new DesignRecentFiles();
        var path1 = CreateTempFile();
        var path2 = CreateTempFile();
        try
        {
            files.Add(path1);
            files.Add(path2);

            files.Remove(path1);

            Assert.Single(files.Files);
            Assert.Equal(path2, files.Files[0]);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public void Clear_RemovesAll()
    {
        var files = new DesignRecentFiles();
        var path = CreateTempFile();
        try
        {
            files.Add(path);
            files.Clear();
            Assert.Empty(files.Files);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Constructor_LoadsExistingFiles()
    {
        var path1 = CreateTempFile();
        var path2 = CreateTempFile();
        try
        {
            var files = new DesignRecentFiles(new[] { path1, path2 });
            Assert.Equal(2, files.Files.Count);
        }
        finally
        {
            File.Delete(path1);
            File.Delete(path2);
        }
    }

    [Fact]
    public void Constructor_FiltersNonexistentFiles()
    {
        var path = CreateTempFile();
        try
        {
            var files = new DesignRecentFiles(new[] { path, @"C:\nonexistent\foo.dgraph.json" });
            Assert.Single(files.Files);
            Assert.Equal(path, files.Files[0]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
