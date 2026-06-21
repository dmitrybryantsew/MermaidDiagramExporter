using System.IO;
using System.Linq;
using MermaidDiagramExporter.Core;
using MermaidDiagramExporter.Extraction;
using Xunit;

namespace MermaidDiagramExporter.Tests;

public class RoslynTypeScannerTests
{
    [Fact]
    public void ScanFolder_EmptyFolder_ReturnsEmptyGraph()
    {
        using var temp = new TempSourceFolder();

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Empty(graph.Nodes);
        Assert.Empty(graph.Edges);
        Assert.Empty(graph.Groups);
        Assert.Equal(GraphSourceKind.Folder, graph.Metadata.SourceKind);
        Assert.Equal(temp.Path, graph.Metadata.SourceDescription);
    }

    [Fact]
    public void ScanFolder_NonExistentFolder_ThrowsDirectoryNotFoundException()
    {
        var scanner = new RoslynTypeScanner();
        var bogusPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "does_not_exist_" + System.Guid.NewGuid().ToString("N"));

        Assert.Throws<DirectoryNotFoundException>(
            () => scanner.ScanFolder(bogusPath));
    }

    [Fact]
    public void ScanFolder_SingleClass_DetectsOneNode()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Foo.cs", @"
namespace MyApp;

public class Foo
{
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        var node = graph.Nodes[0];
        Assert.Equal("Foo", node.DisplayName);
        Assert.Equal("MyApp", node.Namespace);
        Assert.Equal(TypeNodeKind.Class, node.Kind);
        Assert.True(node.IsProjectType);
    }

    [Fact]
    public void ScanFolder_NestedNamespaces_ArePreserved()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Foo.cs", @"
namespace MyApp.Sub.Sub2;

public class Foo { }
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal("MyApp.Sub.Sub2", graph.Nodes[0].Namespace);
    }

    [Fact]
    public void ScanFolder_MultipleFilesInOneFolder_AllDetected()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("A.cs", "namespace X; public class A { }");
        temp.WriteFile("B.cs", "namespace X; public class B { }");
        temp.WriteFile("C.cs", "namespace X; public class C { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Equal(3, graph.Nodes.Count);
        Assert.Contains(graph.Nodes, n => n.DisplayName == "A");
        Assert.Contains(graph.Nodes, n => n.DisplayName == "B");
        Assert.Contains(graph.Nodes, n => n.DisplayName == "C");
    }

    [Fact]
    public void ScanFolder_RecursiveFolders_DetectsNestedFiles()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Sub/A.cs", "namespace X; public class A { }");
        temp.WriteFile("Sub/Sub2/B.cs", "namespace X; public class B { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Equal(2, graph.Nodes.Count);
    }
}

public class RoslynInheritanceTests
{
    [Fact]
    public void ScanFolder_ClassInheritance_CreatesInheritanceEdge()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Base.cs", "namespace X; public class Base { }");
        temp.WriteFile("Derived.cs", "namespace X; public class Derived : Base { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Equal(2, graph.Nodes.Count);
        var baseNode = graph.Nodes.First(n => n.DisplayName == "Base");
        var derivedNode = graph.Nodes.First(n => n.DisplayName == "Derived");

        Assert.Contains(graph.Edges, e =>
            e.FromNodeId == baseNode.Id &&
            e.ToNodeId == derivedNode.Id &&
            e.Kind == TypeEdgeKind.Inheritance);
    }

    [Fact]
    public void ScanFolder_InterfaceImplementation_CreatesImplementsEdge()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("IRepo.cs", "namespace X; public interface IRepo { }");
        temp.WriteFile("Repo.cs", "namespace X; public class Repo : IRepo { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        var ifaceNode = graph.Nodes.First(n => n.DisplayName == "IRepo");
        var classNode = graph.Nodes.First(n => n.DisplayName == "Repo");

        Assert.Contains(graph.Edges, e =>
            e.FromNodeId == ifaceNode.Id &&
            e.ToNodeId == classNode.Id &&
            e.Kind == TypeEdgeKind.Implements);
    }
}

public class RoslynKindTests
{
    [Fact]
    public void ScanFolder_Interface_KindIsInterface()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("IFoo.cs", "namespace X; public interface IFoo { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal(TypeNodeKind.Interface, graph.Nodes[0].Kind);
    }

    [Fact]
    public void ScanFolder_Enum_KindIsEnum()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Color.cs", "namespace X; public enum Color { Red, Green, Blue }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal(TypeNodeKind.Enum, graph.Nodes[0].Kind);
    }

    [Fact]
    public void ScanFolder_Struct_KindIsStruct()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Point.cs", "namespace X; public struct Point { public int X; public int Y; }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal(TypeNodeKind.Struct, graph.Nodes[0].Kind);
    }

    [Fact]
    public void ScanFolder_AbstractClass_KindIsAbstractClass()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Abs.cs", "namespace X; public abstract class Abs { public abstract void Do(); }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal(TypeNodeKind.AbstractClass, graph.Nodes[0].Kind);
    }

    [Fact]
    public void ScanFolder_StaticClass_KindIsStaticClass()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Util.cs", "namespace X; public static class Util { public static void Go() { } }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal(TypeNodeKind.StaticClass, graph.Nodes[0].Kind);
    }
}

public class RoslynStereotypeTests
{
    [Fact]
    public void ScanFolder_ClassInheritingMonoBehaviour_StereotypeIsMonoBehaviour()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("MyBehaviour.cs", @"
using UnityEngine;

namespace Game
{
    public class MyBehaviour : MonoBehaviour { }
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        // MonoBehaviour is not resolvable (UnityEngine.dll not referenced),
        // so the class still appears but the base type is detected by name
        Assert.Single(graph.Nodes);
        var node = graph.Nodes[0];
        Assert.Contains("mono-behaviour", node.Stereotypes);
    }

    [Fact]
    public void ScanFolder_ClassInheritingScriptableObject_StereotypeIsScriptableObject()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("MySO.cs", @"
using UnityEngine;

namespace Game
{
    public class MySO : ScriptableObject { }
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Contains("scriptable-object", graph.Nodes[0].Stereotypes);
    }

    [Fact]
    public void ScanFolder_PlainClass_HasNoStereotypes()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Plain.cs", "namespace X; public class Plain { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Empty(graph.Nodes[0].Stereotypes);
    }
}

public class RoslynMemberTests
{
    [Fact]
    public void ScanFolder_PublicFields_AreIncludedAsMembers()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Data.cs", @"
namespace X;
public class Data
{
    public string Name;
    public int Age;
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        var members = graph.Nodes[0].Members;
        Assert.Equal(2, members.Count);
        Assert.Contains(members, m => m.Name == "Name" && m.Kind == TypeMemberKind.Field);
        Assert.Contains(members, m => m.Name == "Age" && m.Kind == TypeMemberKind.Field);
    }

    [Fact]
    public void ScanFolder_Properties_AreIncludedAsMembers()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Data.cs", @"
namespace X;
public class Data
{
    public string Name { get; set; }
    public int Value { get; }
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        var members = graph.Nodes[0].Members;
        Assert.Equal(2, members.Count);
        Assert.All(members, m => Assert.Equal(TypeMemberKind.Property, m.Kind));
    }

    [Fact]
    public void ScanFolder_Methods_AreIncludedWithParameters()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Svc.cs", @"
namespace X;
public class Svc
{
    public int Calculate(int a, int b) => a + b;
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        var method = graph.Nodes[0].Members.FirstOrDefault(m => m.Name == "Calculate");
        Assert.NotNull(method);
        Assert.Equal(TypeMemberKind.Method, method!.Kind);
        Assert.Equal(2, method.Parameters.Count);
        Assert.Equal("a", method.Parameters[0].Name);
        Assert.Equal("b", method.Parameters[1].Name);
    }

    [Fact]
    public void ScanFolder_PrivateMembers_AreExcluded()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Data.cs", @"
namespace X;
public class Data
{
    private int _hidden;
    public int Visible;
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        var members = graph.Nodes[0].Members;
        Assert.Single(members);
        Assert.Equal("Visible", members[0].Name);
    }

    [Fact]
    public void ScanFolder_NoFields_ExcludesFieldsFromMembers()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Data.cs", @"
namespace X;
public class Data
{
    public int Value;
    public string Name { get; set; }
}
");

        var scanner = new RoslynTypeScanner();
        var opts = new GraphBuildOptions { IncludeFields = false };
        var graph = scanner.ScanFolder(temp.Path, opts);

        var members = graph.Nodes[0].Members;
        Assert.All(members, m => Assert.NotEqual(TypeMemberKind.Field, m.Kind));
    }
}

public class RoslynAssociationTests
{
    [Fact]
    public void ScanFolder_FieldOfAnotherType_CreatesAssociationEdge()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Engine.cs", "namespace X; public class Engine { }");
        temp.WriteFile("Car.cs", @"
namespace X;
public class Car
{
    public Engine Motor;
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        var carNode = graph.Nodes.First(n => n.DisplayName == "Car");
        var engineNode = graph.Nodes.First(n => n.DisplayName == "Engine");

        Assert.Contains(graph.Edges, e =>
            e.FromNodeId == carNode.Id &&
            e.ToNodeId == engineNode.Id &&
            e.Kind == TypeEdgeKind.Association);
    }

    [Fact]
    public void ScanFolder_PropertyOfAnotherType_CreatesAssociationEdge()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Wheel.cs", "namespace X; public class Wheel { }");
        temp.WriteFile("Car.cs", @"
namespace X;
public class Car
{
    public Wheel FrontLeft { get; set; }
}
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        var carNode = graph.Nodes.First(n => n.DisplayName == "Car");
        var wheelNode = graph.Nodes.First(n => n.DisplayName == "Wheel");

        Assert.Contains(graph.Edges, e =>
            e.FromNodeId == carNode.Id &&
            e.ToNodeId == wheelNode.Id &&
            e.Kind == TypeEdgeKind.Association);
    }
}

public class RoslynGroupTests
{
    [Fact]
    public void ScanFolder_SameNamespace_CreatesSingleGroup()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("A.cs", "namespace MyApp; public class A { }");
        temp.WriteFile("B.cs", "namespace MyApp; public class B { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Groups);
        Assert.Equal("MyApp", graph.Groups[0].Label);
        Assert.Equal(2, graph.Groups[0].NodeIds.Count);
    }

    [Fact]
    public void ScanFolder_DifferentNamespaces_CreatesMultipleGroups()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("A.cs", "namespace Alpha; public class A { }");
        temp.WriteFile("B.cs", "namespace Beta; public class B { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Equal(2, graph.Groups.Count);
        Assert.Contains(graph.Groups, g => g.Label == "Alpha");
        Assert.Contains(graph.Groups, g => g.Label == "Beta");
    }
}

public class RoslynGenericTests
{
    [Fact]
    public void ScanFolder_GenericClass_DetectedWithGenericDisplayName()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Repo.cs", @"
namespace X;
public class Repo<T> { }
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal("Repo~T~", graph.Nodes[0].DisplayName);
    }

    [Fact]
    public void ScanFolder_GenericClassWithTwoParams_ShowsBothParams()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Pair.cs", @"
namespace X;
public class Pair<TKey, TValue> { }
");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.Equal("Pair~TKey, TValue~", graph.Nodes[0].DisplayName);
    }
}

public class RoslynAssetPathTests
{
    [Fact]
    public void ScanFolder_NodeAssetPath_IsAbsoluteFilePath()
    {
        using var temp = new TempSourceFolder();
        temp.WriteFile("Foo.cs", "namespace X; public class Foo { }");

        var scanner = new RoslynTypeScanner();
        var graph = scanner.ScanFolder(temp.Path);

        Assert.Single(graph.Nodes);
        Assert.True(graph.Nodes[0].AssetPath.Contains("Foo.cs"));
        Assert.True(System.IO.Path.IsPathRooted(graph.Nodes[0].AssetPath));
    }
}
