using System;
using System.IO;
using System.Linq;
using MermaidDiagramExporter.Gui.Design;
using Xunit;

namespace MermaidDiagramExporter.Tests;

/// <summary>
/// Tests for DesignSerialization — JSON save/load round-trip and version
/// validation. Per docs/design/05-data-model-and-persistence.md and
/// docs/design/07-implementation-phases.md M1 acceptance criteria.
/// </summary>
public class DesignSerializationTests
{
    private static string CreateTempFile()
    {
        string path = Path.Combine(Path.GetTempPath(), $"design-{Guid.NewGuid()}.dgraph.json");
        return path;
    }

    [Fact]
    public void SaveAndLoad_EmptyGraph_RoundTripsIdentically()
    {
        var original = new DesignGraph { Title = "Empty Test" };
        string path = CreateTempFile();
        try
        {
            DesignSerialization.Save(original, path);
            var loaded = DesignSerialization.Load(path);

            Assert.Equal(original.Title, loaded.Title);
            Assert.Equal(original.Version, loaded.Version);
            Assert.Empty(loaded.Classes);
            Assert.Empty(loaded.Edges);
            Assert.Empty(loaded.Namespaces);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveAndLoad_GraphWithClassesAndEdges_RoundTripsIdentically()
    {
        var classA = new DesignClass
        {
            Id = "class-a",
            Name = "Animal",
            Namespace = "Zoo",
            Kind = ClassKind.Class,
            X = 10, Y = 20, Width = 200, Height = 100,
            Members =
            {
                new DesignMember { Kind = MemberKind.Field, Name = "Name", TypeName = "string", Visibility = Visibility.Public },
                new DesignMember { Kind = MemberKind.Method, Name = "Speak", TypeName = "void", Visibility = Visibility.Public,
                    Parameters = { new DesignParameter { Name = "volume", TypeName = "int" } } }
            }
        };
        var classB = new DesignClass
        {
            Id = "class-b",
            Name = "Dog",
            Namespace = "Zoo",
            Kind = ClassKind.Class,
            X = 300, Y = 20, Width = 200, Height = 100
        };
        var edge = new DesignEdge
        {
            Id = "edge-1",
            FromClassId = "class-b",
            ToClassId = "class-a",
            Kind = EdgeKind.Inheritance
        };

        var original = new DesignGraph
        {
            Title = "Zoo Diagram",
            Classes = { classA, classB },
            Edges = { edge }
        };
        string path = CreateTempFile();
        try
        {
            DesignSerialization.Save(original, path);
            var loaded = DesignSerialization.Load(path);

            Assert.Equal("Zoo Diagram", loaded.Title);
            Assert.Equal(2, loaded.Classes.Count);
            Assert.Single(loaded.Edges);

            var loadedA = loaded.Classes.First(c => c.Id == "class-a");
            Assert.Equal("Animal", loadedA.Name);
            Assert.Equal("Zoo", loadedA.Namespace);
            Assert.Equal(ClassKind.Class, loadedA.Kind);
            Assert.Equal(10f, loadedA.X);
            Assert.Equal(2, loadedA.Members.Count);
            Assert.Single(loadedA.Members[1].Parameters);

            var loadedEdge = loaded.Edges[0];
            Assert.Equal("class-b", loadedEdge.FromClassId);
            Assert.Equal("class-a", loadedEdge.ToClassId);
            Assert.Equal(EdgeKind.Inheritance, loadedEdge.Kind);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void SaveAndLoad_PreservesEnumStringFormat()
    {
        // Verify that enums serialize as strings (not integers) — makes the
        // JSON human-readable and diffable per doc 05.
        var original = new DesignGraph
        {
            Classes =
            {
                new DesignClass { Kind = ClassKind.Interface },
                new DesignClass { Kind = ClassKind.Enum }
            },
            Edges =
            {
                new DesignEdge { Kind = EdgeKind.Composition }
            }
        };
        string path = CreateTempFile();
        try
        {
            DesignSerialization.Save(original, path);
            string json = File.ReadAllText(path);

            // Enums should be string-encoded (PascalCase by default with
            // JsonStringEnumConverter). Verify the exact enum member names
            // appear, not their integer values.
            Assert.Contains("\"Interface\"", json);
            Assert.Contains("\"Enum\"", json);
            Assert.Contains("\"Composition\"", json);
            // Negative: integer encoding should NOT appear
            Assert.DoesNotContain("\"kind\": 1", json);
            Assert.DoesNotContain("\"kind\": 2", json);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TryLoad_NonExistentFile_ReturnsNull()
    {
        var result = DesignSerialization.TryLoad(@"C:\nonexistent\path\does-not-exist.dgraph.json");
        Assert.Null(result);
    }

    [Fact]
    public void TryLoad_MalformedJson_ReturnsNull()
    {
        string path = CreateTempFile();
        try
        {
            File.WriteAllText(path, "{ this is not valid json ]]]");
            var result = DesignSerialization.TryLoad(path);
            Assert.Null(result);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void IsVersionSupported_Version1_ReturnsTrue()
    {
        Assert.True(DesignSerialization.IsVersionSupported("1"));
    }

    [Fact]
    public void IsVersionSupported_UnknownVersion_ReturnsFalse()
    {
        Assert.False(DesignSerialization.IsVersionSupported("999"));
        Assert.False(DesignSerialization.IsVersionSupported("0"));
        Assert.False(DesignSerialization.IsVersionSupported(""));
    }

    [Fact]
    public void Save_UpdatesModifiedUtc()
    {
        var original = new DesignGraph { ModifiedUtc = DateTime.MinValue };
        string path = CreateTempFile();
        try
        {
            DesignSerialization.Save(original, path);
            var loaded = DesignSerialization.Load(path);

            // ModifiedUtc should be updated to roughly "now" by Save()
            Assert.True(loaded.ModifiedUtc > DateTime.MinValue);
            Assert.True((DateTime.UtcNow - loaded.ModifiedUtc).TotalMinutes < 1);
        }
        finally { File.Delete(path); }
    }
}
