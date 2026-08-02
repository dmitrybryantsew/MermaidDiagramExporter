using System;
using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// The editable graph model for Design Mode. A clean, serializable,
/// diffable representation of a class diagram that is independent of the
/// runtime layout coordinates, rendering state, and scan output.
/// </summary>
public sealed class DesignGraph
{
    public string Title { get; set; } = "Untitled Design";
    public string Version { get; set; } = "1";
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<DesignClass> Classes { get; set; } = new();
    public List<DesignEdge> Edges { get; set; } = new();
    public List<DesignNamespace> Namespaces { get; set; } = new();
}

public sealed class DesignClass
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "NewClass";
    public string Namespace { get; set; } = "";
    public ClassKind Kind { get; set; } = ClassKind.Class;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 200f;
    public float Height { get; set; } = 100f;
    public List<DesignMember> Members { get; set; } = new();
    public string? Stereotype { get; set; }
}

public enum ClassKind
{
    Class,
    Interface,
    Enum,
    Struct,
    StaticClass,
    AbstractClass
}

public sealed class DesignMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MemberKind Kind { get; set; } = MemberKind.Field;
    public string Name { get; set; } = "NewMember";
    public string TypeName { get; set; } = "object";
    public Visibility Visibility { get; set; } = Visibility.Public;
    public List<DesignParameter> Parameters { get; set; } = new();
}

public enum MemberKind
{
    Field,
    Property,
    Method,
    Constructor,
    Event
}

public enum Visibility
{
    Public,
    Private,
    Protected,
    Internal
}

public sealed class DesignParameter
{
    public string Name { get; set; } = "param";
    public string TypeName { get; set; } = "object";
}

public sealed class DesignEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FromClassId { get; set; } = "";
    public string ToClassId { get; set; } = "";
    public EdgeKind Kind { get; set; } = EdgeKind.Association;
    public string? Label { get; set; }
}

public enum EdgeKind
{
    Association,
    Inheritance,
    Implements,
    Dependency,
    Aggregation,
    Composition
}

public sealed class DesignNamespace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "MyNamespace";
    public string? ParentNamespaceId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}
