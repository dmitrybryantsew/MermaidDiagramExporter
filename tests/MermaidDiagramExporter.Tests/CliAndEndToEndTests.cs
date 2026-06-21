using System;
using System.IO;
using MermaidDiagramExporter.Core;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class CliOptionsTests
{
    [Fact]
    public void Parse_NoArgs_ReturnsNull()
    {
        var result = CliOptions.Parse(Array.Empty<string>());
        Assert.Null(result);
    }

    [Fact]
    public void Parse_HelpFlag_ReturnsNull()
    {
        var result = CliOptions.Parse(new[] { "--help" });
        Assert.Null(result);
    }

    [Fact]
    public void Parse_FolderOnly_ReturnsDefaultOptions()
    {
        var result = CliOptions.Parse(new[] { "/some/folder" });

        Assert.NotNull(result);
        Assert.Equal("/some/folder", result.FolderPath);
        Assert.True(result.BuildOptions.IncludeFields);
        Assert.True(result.BuildOptions.IncludeProperties);
        Assert.True(result.BuildOptions.IncludeMethods);
        Assert.False(result.OpenAfter);
    }

    [Fact]
    public void Parse_OutputDir_IsSet()
    {
        var result = CliOptions.Parse(new[] { "/src", "-o", "/out" });

        Assert.NotNull(result);
        Assert.Equal("/out", result.OutputDir);
    }

    [Fact]
    public void Parse_NoFields_SetsIncludeFieldsFalse()
    {
        var result = CliOptions.Parse(new[] { "/src", "--no-fields" });

        Assert.NotNull(result);
        Assert.False(result.BuildOptions.IncludeFields);
    }

    [Fact]
    public void Parse_NoMethods_SetsIncludeMethodsFalse()
    {
        var result = CliOptions.Parse(new[] { "/src", "--no-methods" });

        Assert.NotNull(result);
        Assert.False(result.BuildOptions.IncludeMethods);
    }

    [Fact]
    public void Parse_NoInterfaces_SetsIncludeInterfacesFalse()
    {
        var result = CliOptions.Parse(new[] { "/src", "--no-interfaces" });

        Assert.NotNull(result);
        Assert.False(result.BuildOptions.IncludeInterfaces);
    }

    [Fact]
    public void Parse_NoAssociations_SetsIncludeAssociationsFalse()
    {
        var result = CliOptions.Parse(new[] { "/src", "--no-associations" });

        Assert.NotNull(result);
        Assert.False(result.BuildOptions.IncludeAssociations);
    }

    [Fact]
    public void Parse_MaxMembers_SetsMaxMemberCount()
    {
        var result = CliOptions.Parse(new[] { "/src", "--max-members", "5" });

        Assert.NotNull(result);
        Assert.Equal(5, result.BuildOptions.MaxMemberCountPerNode);
    }

    [Fact]
    public void Parse_MaxMembersMissingValue_ReturnsNull()
    {
        var result = CliOptions.Parse(new[] { "/src", "--max-members" });

        Assert.Null(result);
    }

    [Fact]
    public void Parse_MaxMembersNonNumeric_ReturnsNull()
    {
        var result = CliOptions.Parse(new[] { "/src", "--max-members", "abc" });

        Assert.Null(result);
    }

    [Fact]
    public void Parse_UnknownFlag_ReturnsNull()
    {
        var result = CliOptions.Parse(new[] { "/src", "--bogus" });

        Assert.Null(result);
    }

    [Fact]
    public void Parse_OpenFlag_SetsOpenAfter()
    {
        var result = CliOptions.Parse(new[] { "/src", "--open" });

        Assert.NotNull(result);
        Assert.True(result.OpenAfter);
    }

    [Fact]
    public void Parse_RelativeOutputDir_IsMadeAbsolute()
    {
        var result = CliOptions.Parse(new[] { "/src", "-o", "relative/path" });

        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result.OutputDir));
    }
}

public class EndToEndTests
{
    [Fact]
    public void FullPipeline_WritesOutputFile()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Foo.cs", "namespace X; public class Foo { }");

        string outDir = Path.Combine(Path.GetTempPath(), "mermaid_e2e_" + Guid.NewGuid().ToString("N"));
        try
        {
            int exitCode = MermaidDiagramExporter.Program.Main(new[] { temp.Path, "-o", outDir });

            Assert.Equal(0, exitCode);
            string[] files = Directory.GetFiles(outDir, "*.md");
            Assert.Single(files);

            string content = File.ReadAllText(files[0]);
            Assert.Contains("classDiagram", content);
            Assert.Contains("Foo", content);
        }
        finally
        {
            try { if (Directory.Exists(outDir)) Directory.Delete(outDir, true); } catch { }
        }
    }

    [Fact]
    public void FullPipeline_NonExistentFolder_Returns1()
    {
        string bogus = Path.Combine(Path.GetTempPath(), "no_such_" + Guid.NewGuid().ToString("N"));
        int exitCode = MermaidDiagramExporter.Program.Main(new[] { bogus });
        Assert.Equal(1, exitCode);
    }
}
