using System;
using System.Collections.Generic;

namespace MermaidDiagramExporter.Core;

public sealed class GraphBuildOptions
{
    public bool IncludeFields { get; set; } = true;

    public bool IncludeProperties { get; set; } = true;

    public bool IncludeMethods { get; set; } = true;

    public bool IncludeInterfaces { get; set; } = true;

    public bool IncludeAssociations { get; set; } = true;

    public bool IncludeDeclaredMembersOnly { get; set; } = true;

    public TypeGroupKind PrimaryGroupKind { get; set; } = TypeGroupKind.Namespace;

    public int MaxMemberCountPerNode { get; set; }

    /// <summary>
    /// User-defined custom stereotype rules to apply during scanning.
    /// </summary>
    public List<StereotypeConfig> CustomStereotypes { get; set; } = new();
}

/// <summary>
/// A user-defined stereotype configuration passed to the scanner.
/// </summary>
public sealed class StereotypeConfig
{
    public string Pattern { get; set; } = ".*";
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "#4ECDC4";
}

public enum TypeNodeKind
{
    Class,
    Interface,
    Enum,
    Struct,
    StaticClass,
    AbstractClass
}

public enum TypeMemberKind
{
    Field,
    Property,
    Method
}

public enum TypeVisibility
{
    Public,
    Protected,
    Internal,
    Private
}

public enum TypeEdgeKind
{
    Inheritance,
    Implements,
    Association
}

public enum TypeGroupKind
{
    Namespace,
    Folder,
    Assembly
}

public enum GraphSourceKind
{
    Unknown,
    Selection,
    Folder
}

public sealed class TypeGraph
{
    public TypeGraph(
        string title,
        IReadOnlyList<TypeNodeData> nodes,
        IReadOnlyList<TypeEdgeData> edges,
        IReadOnlyList<TypeGroupData> groups,
        TypeGraphMetadata metadata)
    {
        Title = title ?? string.Empty;
        Nodes = nodes ?? Array.Empty<TypeNodeData>();
        Edges = edges ?? Array.Empty<TypeEdgeData>();
        Groups = groups ?? Array.Empty<TypeGroupData>();
        Metadata = metadata ?? new TypeGraphMetadata();
    }

    public string Title { get; }

    public IReadOnlyList<TypeNodeData> Nodes { get; }

    public IReadOnlyList<TypeEdgeData> Edges { get; }

    public IReadOnlyList<TypeGroupData> Groups { get; }

    public TypeGraphMetadata Metadata { get; }
}

public sealed class TypeGraphMetadata
{
    public DateTime GeneratedAtUtc { get; set; } = DateTime.UtcNow;

    public string SourceDescription { get; set; } = string.Empty;

    public GraphSourceKind SourceKind { get; set; } = GraphSourceKind.Unknown;

    public GraphBuildOptions Options { get; set; } = new GraphBuildOptions();

    public bool IsDerivedView { get; set; }

    public string ParentGraphTitle { get; set; } = string.Empty;

    public string FocusSummary { get; set; } = string.Empty;

    public IReadOnlyList<string> SeedNodeIds { get; set; } = Array.Empty<string>();

    public int FocusDepth { get; set; }
}

public sealed class TypeNodeData
{
    public string Id { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Namespace { get; set; } = string.Empty;

    public string AssemblyName { get; set; } = string.Empty;

    public string AssetPath { get; set; } = string.Empty;

    public TypeNodeKind Kind { get; set; } = TypeNodeKind.Class;

    public bool IsProjectType { get; set; }

    /// <summary>
    /// Framework-specific stereotypes (e.g. "mono-behaviour", "scriptable-object", "component").
    /// Populated by heuristic when scanning Unity projects without Unity loaded.
    /// </summary>
    public IReadOnlyList<string> Stereotypes { get; set; } = Array.Empty<string>();

    public IReadOnlyList<TypeMemberData> Members { get; set; } = Array.Empty<TypeMemberData>();
}

public sealed class TypeMemberData
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;

    public TypeMemberKind Kind { get; set; } = TypeMemberKind.Field;

    public TypeVisibility Visibility { get; set; } = TypeVisibility.Public;

    public bool IsStatic { get; set; }

    public bool IsAbstract { get; set; }

    public IReadOnlyList<TypeMemberParameterData> Parameters { get; set; } = Array.Empty<TypeMemberParameterData>();
}

public sealed class TypeMemberParameterData
{
    public string Name { get; set; } = string.Empty;

    public string TypeName { get; set; } = string.Empty;
}

public sealed class TypeEdgeData
{
    public string FromNodeId { get; set; } = string.Empty;

    public string ToNodeId { get; set; } = string.Empty;

    public TypeEdgeKind Kind { get; set; } = TypeEdgeKind.Association;

    public string Label { get; set; } = string.Empty;

    public bool IsStrongRelation { get; set; }
}

public sealed class TypeGroupData
{
    public string Id { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    public TypeGroupKind Kind { get; set; } = TypeGroupKind.Namespace;

    public string ParentGroupId { get; set; } = string.Empty;

    public IReadOnlyList<string> NodeIds { get; set; } = Array.Empty<string>();
}
