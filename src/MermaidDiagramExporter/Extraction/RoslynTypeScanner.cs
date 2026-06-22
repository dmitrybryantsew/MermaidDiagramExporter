using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Extraction;

public sealed class RoslynTypeScanner
{
    public TypeGraph ScanFolder(string folderPath, GraphBuildOptions options = null)
    {
        GraphBuildOptions resolvedOptions = options ?? new GraphBuildOptions();
        string absolutePath = Path.GetFullPath(folderPath);

        if (!Directory.Exists(absolutePath))
        {
            throw new DirectoryNotFoundException($"Folder not found: {absolutePath}");
        }

        string[] sourceFiles = Directory.EnumerateFiles(absolutePath, "*.cs", SearchOption.AllDirectories).ToArray();

        if (sourceFiles.Length == 0)
        {
            return new TypeGraph(
                Path.GetFileName(absolutePath),
                Array.Empty<TypeNodeData>(),
                Array.Empty<TypeEdgeData>(),
                Array.Empty<TypeGroupData>(),
                new TypeGraphMetadata { SourceKind = GraphSourceKind.Folder, SourceDescription = absolutePath, Options = resolvedOptions });
        }

        CSharpCompilation compilation = BuildCompilation(sourceFiles);
        Dictionary<INamedTypeSymbol, TypeNodeData> nodeBySymbol = new(SymbolEqualityComparer.Default);
        List<TypeNodeData> nodes = new();

        foreach (INamedTypeSymbol type in CollectTypeSymbols(compilation))
        {
            if (!ShouldIncludeType(type))
                continue;

            TypeNodeData node = BuildNode(type, resolvedOptions);
            nodes.Add(node);
            nodeBySymbol[type] = node;
        }

        List<TypeNodeData> orderedNodes = nodes
            .OrderBy(n => n.Namespace)
            .ThenBy(n => n.DisplayName)
            .ToList();

        List<TypeGroupData> groups = BuildGroups(orderedNodes, resolvedOptions);
        List<TypeEdgeData> edges = BuildEdges(nodeBySymbol, resolvedOptions);

        return new TypeGraph(
            Path.GetFileName(absolutePath),
            orderedNodes,
            edges,
            groups,
            new TypeGraphMetadata
            {
                GeneratedAtUtc = DateTime.UtcNow,
                SourceDescription = absolutePath,
                SourceKind = GraphSourceKind.Folder,
                Options = resolvedOptions
            });
    }

    private CSharpCompilation BuildCompilation(string[] sourceFiles)
    {
        List<SyntaxTree> syntaxTrees = new();
        foreach (string file in sourceFiles)
        {
            string source = File.ReadAllText(file);
            CSharpParseOptions parseOptions = CSharpParseOptions.Default
                .WithLanguageVersion(LanguageVersion.Latest)
                .WithPreprocessorSymbols(new[] { "DEBUG", "TRACE" });
            SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions, file);
            syntaxTrees.Add(tree);
        }

        CSharpCompilation compilation = CSharpCompilation.Create(
            "MermaidScan",
            syntaxTrees,
            GetBasicReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        return compilation;
    }

    private static MetadataReference[] GetBasicReferences()
    {
        List<MetadataReference> refs = new()
        {
            MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.IEnumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Collections.Generic.List<>).Assembly.Location),
            MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.RuntimeHelpers).Assembly.Location),
        };

        string netstandardPath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "netstandard.dll");
        if (File.Exists(netstandardPath))
        {
            refs.Add(MetadataReference.CreateFromFile(netstandardPath));
        }

        string systemRuntimePath = Path.Combine(
            Path.GetDirectoryName(typeof(object).Assembly.Location)!,
            "System.Runtime.dll");
        if (File.Exists(systemRuntimePath))
        {
            refs.Add(MetadataReference.CreateFromFile(systemRuntimePath));
        }

        return refs.ToArray();
    }

    private IEnumerable<INamedTypeSymbol> CollectTypeSymbols(CSharpCompilation compilation)
    {
        HashSet<INamedTypeSymbol> seen = new(SymbolEqualityComparer.Default);

        foreach (SyntaxTree tree in compilation.SyntaxTrees)
        {
            SemanticModel semanticModel = compilation.GetSemanticModel(tree);
            IEnumerable<BaseTypeDeclarationSyntax> typeDeclarations = tree.GetRoot().DescendantNodes()
                .OfType<BaseTypeDeclarationSyntax>();

            foreach (BaseTypeDeclarationSyntax declaration in typeDeclarations)
            {
                INamedTypeSymbol symbol = semanticModel.GetDeclaredSymbol(declaration);
                if (symbol != null && seen.Add(symbol))
                {
                    yield return symbol;
                }
            }
        }
    }

    private bool ShouldIncludeType(INamedTypeSymbol type)
    {
        if (type == null)
            return false;

        if (type.IsAnonymousType)
            return false;

        if (type.AssociatedSymbol != null && type.AssociatedSymbol.Kind == SymbolKind.Method)
            return false;

        string name = type.Name ?? string.Empty;
        if (name.StartsWith("<") || name.Contains("__"))
            return false;

        return true;
    }

    private TypeNodeData BuildNode(INamedTypeSymbol type, GraphBuildOptions options)
    {
        List<TypeMemberData> members = BuildMembers(type, options);
        List<string> stereotypes = BuildStereotypes(type);

        return new TypeNodeData
        {
            Id = BuildTypeId(type),
            DisplayName = BuildTypeDisplayName(type),
            FullName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Namespace = type.ContainingNamespace?.ToDisplayString() ?? string.Empty,
            AssemblyName = type.ContainingAssembly?.Name ?? "Source",
            AssetPath = type.Locations.FirstOrDefault()?.SourceTree?.FilePath ?? string.Empty,
            Kind = BuildNodeKind(type),
            IsProjectType = true,
            Stereotypes = stereotypes,
            Members = members
        };
    }

    private List<string> BuildStereotypes(INamedTypeSymbol type)
    {
        List<string> stereotypes = new();
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

        // Walk the full base type chain to detect Unity stereotypes
        // even through indirect inheritance (e.g. MyClass -> MyBaseClass -> MonoBehaviour)
        INamedTypeSymbol? current = type.BaseType;
        while (current != null)
        {
            string baseName = current.Name ?? string.Empty;
            string baseFullName = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

            string? stereotype = null;
            if (baseName == "MonoBehaviour" || baseFullName.Contains("MonoBehaviour"))
                stereotype = "mono-behaviour";
            else if (baseName == "ScriptableObject" || baseFullName.Contains("ScriptableObject"))
                stereotype = "scriptable-object";
            else if (baseName == "Component" || baseFullName.Contains("UnityEngine.Component"))
                stereotype = "component";

            if (stereotype != null && seen.Add(stereotype))
                stereotypes.Add(stereotype);

            current = current.BaseType;
        }

        return stereotypes;
    }

    private List<TypeMemberData> BuildMembers(INamedTypeSymbol type, GraphBuildOptions options)
    {
        List<TypeMemberData> members = new();

        if (options.IncludeDeclaredMembersOnly)
        {
            CollectMembers(type, options, members);
        }
        else
        {
            // Walk the full inheritance chain to collect inherited members
            // from base types. Start from the base type and walk up,
            // then add declared members of the current type.
            CollectInheritedMembers(type.BaseType, options, members);
            CollectMembers(type, options, members);
        }

        if (options.MaxMemberCountPerNode > 0 && members.Count > options.MaxMemberCountPerNode)
        {
            members = members.Take(options.MaxMemberCountPerNode).ToList();
        }

        return members;
    }

    private void CollectInheritedMembers(INamedTypeSymbol? baseType, GraphBuildOptions options, List<TypeMemberData> members)
    {
        if (baseType == null) return;
        // Walk up the chain first (so base type members come first in the list)
        CollectInheritedMembers(baseType.BaseType, options, members);
        CollectMembers(baseType, options, members);
    }

    private void CollectMembers(INamedTypeSymbol type, GraphBuildOptions options, List<TypeMemberData> members)
    {
        if (options.IncludeFields)
        {
            foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsImplicitlyDeclared || field.Name.StartsWith("<"))
                    continue;
                if (!IsAccessible(field))
                    continue;

                members.Add(new TypeMemberData
                {
                    Name = field.Name,
                    TypeName = BuildTypeName(field.Type),
                    Kind = TypeMemberKind.Field,
                    Visibility = MapVisibility(field.DeclaredAccessibility),
                    IsStatic = field.IsStatic
                });
            }
        }

        if (options.IncludeProperties)
        {
            foreach (IPropertySymbol property in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (property.IsImplicitlyDeclared || property.Name.StartsWith("<"))
                    continue;
                if (property.IsIndexer)
                    continue;
                if (!IsAccessible(property))
                    continue;

                members.Add(new TypeMemberData
                {
                    Name = property.Name,
                    TypeName = BuildTypeName(property.Type),
                    Kind = TypeMemberKind.Property,
                    Visibility = MapVisibility(property.DeclaredAccessibility),
                    IsStatic = property.IsStatic
                });
            }
        }

        if (options.IncludeMethods)
        {
            foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>())
            {
                if (method.IsImplicitlyDeclared || method.Name.StartsWith("<"))
                    continue;
                if (method.MethodKind != MethodKind.Ordinary)
                    continue;
                if (!IsAccessible(method))
                    continue;

                members.Add(new TypeMemberData
                {
                    Name = method.Name,
                    TypeName = BuildTypeName(method.ReturnType),
                    Kind = TypeMemberKind.Method,
                    Visibility = MapVisibility(method.DeclaredAccessibility),
                    IsStatic = method.IsStatic,
                    IsAbstract = method.IsAbstract,
                    Parameters = method.Parameters
                        .Select(p => new TypeMemberParameterData
                        {
                            Name = p.Name ?? "arg",
                            TypeName = BuildTypeName(p.Type)
                        })
                        .ToArray()
                });
            }
        }
    }

    private static bool IsAccessible(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility != Accessibility.Private
            && symbol.DeclaredAccessibility != Accessibility.NotApplicable;
    }

    private List<TypeGroupData> BuildGroups(List<TypeNodeData> orderedNodes, GraphBuildOptions options)
    {
        TypeGroupKind groupKind = options.PrimaryGroupKind;
        if (groupKind == TypeGroupKind.Folder)
            groupKind = TypeGroupKind.Namespace;

        IEnumerable<IGrouping<string, TypeNodeData>> grouped = groupKind == TypeGroupKind.Assembly
            ? orderedNodes.GroupBy(n => n.AssemblyName)
            : orderedNodes.GroupBy(n => n.Namespace);

        return grouped
            .Select(g => new TypeGroupData
            {
                Id = groupKind + ":" + (string.IsNullOrEmpty(g.Key) ? "global" : g.Key),
                Label = string.IsNullOrEmpty(g.Key)
                    ? groupKind == TypeGroupKind.Assembly ? "Unnamed Assembly" : "Global Namespace"
                    : g.Key,
                Kind = groupKind,
                NodeIds = g.Select(n => n.Id).ToArray()
            })
            .OrderBy(g => g.Label)
            .ToList();
    }

    private List<TypeEdgeData> BuildEdges(Dictionary<INamedTypeSymbol, TypeNodeData> nodeBySymbol, GraphBuildOptions options)
    {
        HashSet<string> edgeKeys = new(StringComparer.Ordinal);
        List<TypeEdgeData> edges = new();

        foreach (var kvp in nodeBySymbol)
        {
            INamedTypeSymbol type = kvp.Key;
            TypeNodeData currentNode = kvp.Value;

            if (type.BaseType != null && nodeBySymbol.TryGetValue(type.BaseType, out TypeNodeData baseNode))
            {
                TryAddEdge(edges, edgeKeys, baseNode.Id, currentNode.Id, TypeEdgeKind.Inheritance, string.Empty, true);
            }

            if (options.IncludeInterfaces)
            {
                foreach (INamedTypeSymbol iface in type.AllInterfaces)
                {
                    if (nodeBySymbol.TryGetValue(iface, out TypeNodeData ifaceNode))
                    {
                        TryAddEdge(edges, edgeKeys, ifaceNode.Id, currentNode.Id, TypeEdgeKind.Implements, "implements", true);
                    }
                }
            }

            if (options.IncludeAssociations)
            {
                foreach (INamedTypeSymbol associatedType in GetAssociatedTypes(type, nodeBySymbol))
                {
                    if (associatedType.Equals(type, SymbolEqualityComparer.Default))
                        continue;

                    if (nodeBySymbol.TryGetValue(associatedType, out TypeNodeData assocNode))
                    {
                        TryAddEdge(edges, edgeKeys, currentNode.Id, assocNode.Id, TypeEdgeKind.Association, string.Empty, false);
                    }
                }
            }
        }

        return edges
            .OrderBy(e => e.FromNodeId)
            .ThenBy(e => e.ToNodeId)
            .ThenBy(e => e.Kind)
            .ToList();
    }

    private IEnumerable<INamedTypeSymbol> GetAssociatedTypes(INamedTypeSymbol type, Dictionary<INamedTypeSymbol, TypeNodeData> nodeBySymbol)
    {
        HashSet<INamedTypeSymbol> results = new(SymbolEqualityComparer.Default);

        foreach (IFieldSymbol field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (field.IsImplicitlyDeclared) continue;
            foreach (INamedTypeSymbol t in ExpandType(field.Type))
            {
                if (nodeBySymbol.ContainsKey(t))
                    results.Add(t);
            }
        }

        foreach (IPropertySymbol prop in type.GetMembers().OfType<IPropertySymbol>())
        {
            if (prop.IsImplicitlyDeclared) continue;
            foreach (INamedTypeSymbol t in ExpandType(prop.Type))
            {
                if (nodeBySymbol.ContainsKey(t))
                    results.Add(t);
            }
        }

        return results;
    }

    private IEnumerable<INamedTypeSymbol> ExpandType(ITypeSymbol type)
    {
        if (type == null)
            yield break;

        if (type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol nullableType)
        {
            foreach (var t in ExpandType(nullableType.TypeArguments.FirstOrDefault()))
                yield return t;
            yield break;
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            foreach (var t in ExpandType(arrayType.ElementType))
                yield return t;
            yield break;
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            foreach (var t in ExpandType(pointerType.PointedAtType))
                yield return t;
            yield break;
        }

        if (type is INamedTypeSymbol genericType && genericType.TypeArguments.Length > 0)
        {
            foreach (ITypeSymbol arg in genericType.TypeArguments)
            {
                foreach (var t in ExpandType(arg))
                    yield return t;
            }
        }

        if (type is INamedTypeSymbol named)
        {
            yield return named;
        }
    }

    private static void TryAddEdge(
        ICollection<TypeEdgeData> edges,
        ISet<string> edgeKeys,
        string fromNodeId,
        string toNodeId,
        TypeEdgeKind kind,
        string label,
        bool isStrongRelation)
    {
        string key = fromNodeId + "|" + toNodeId + "|" + kind + "|" + label;
        if (!edgeKeys.Add(key))
            return;

        edges.Add(new TypeEdgeData
        {
            FromNodeId = fromNodeId,
            ToNodeId = toNodeId,
            Kind = kind,
            Label = label ?? string.Empty,
            IsStrongRelation = isStrongRelation
        });
    }

    public static string BuildTypeId(INamedTypeSymbol type)
    {
        string source = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return "T_" + new string(source.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
    }

    public static string BuildTypeDisplayName(INamedTypeSymbol type)
    {
        return BuildTypeName(type, useMermaidGenerics: true);
    }

    public static string BuildTypeName(ITypeSymbol type, bool useMermaidGenerics = false)
    {
        if (type == null || type.SpecialType == SpecialType.System_Void)
            return "void";

        if (type.OriginalDefinition?.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol nullableType)
        {
            ITypeSymbol underlying = nullableType.TypeArguments.FirstOrDefault();
            return BuildTypeName(underlying, useMermaidGenerics) + "?";
        }

        if (type is IArrayTypeSymbol arrayType)
        {
            return BuildTypeName(arrayType.ElementType, useMermaidGenerics) + "[]";
        }

        if (type is IPointerTypeSymbol pointerType)
        {
            return BuildTypeName(pointerType.PointedAtType, useMermaidGenerics);
        }

        if (type is ITypeParameterSymbol typeParam)
        {
            return typeParam.Name;
        }

        if (type is INamedTypeSymbol namedType)
        {
            string name = namedType.Name;

            int tickIndex = name.IndexOf('`');
            if (tickIndex >= 0)
                name = name.Substring(0, tickIndex);

            name = name.Replace('+', '.');

            if (namedType.TypeArguments.Length > 0)
            {
                string sepLeft = useMermaidGenerics ? "~" : "<";
                string sepRight = useMermaidGenerics ? "~" : ">";
                string args = string.Join(", ", namedType.TypeArguments.Select(a => BuildTypeName(a, useMermaidGenerics)));
                return name + sepLeft + args + sepRight;
            }

            return GetFriendlyTypeName(namedType.SpecialType) ?? name;
        }

        return type.Name ?? "object";
    }

    private static string GetFriendlyTypeName(SpecialType specialType)
    {
        return specialType switch
        {
            SpecialType.System_Int32 => "int",
            SpecialType.System_Single => "float",
            SpecialType.System_Double => "double",
            SpecialType.System_Decimal => "decimal",
            SpecialType.System_Boolean => "bool",
            SpecialType.System_String => "string",
            SpecialType.System_Int64 => "long",
            SpecialType.System_Int16 => "short",
            SpecialType.System_Byte => "byte",
            SpecialType.System_Char => "char",
            SpecialType.System_UInt32 => "uint",
            SpecialType.System_UInt64 => "ulong",
            SpecialType.System_UInt16 => "ushort",
            SpecialType.System_SByte => "sbyte",
            SpecialType.System_Object => "object",
            SpecialType.System_Void => "void",
            _ => null
        };
    }

    private static TypeNodeKind BuildNodeKind(INamedTypeSymbol type)
    {
        if (type.TypeKind == TypeKind.Interface)
            return TypeNodeKind.Interface;

        if (type.TypeKind == TypeKind.Enum)
            return TypeNodeKind.Enum;

        if (type.TypeKind == TypeKind.Struct)
            return TypeNodeKind.Struct;

        if (type.IsStatic)
            return TypeNodeKind.StaticClass;

        if (type.IsAbstract)
            return TypeNodeKind.AbstractClass;

        return TypeNodeKind.Class;
    }

    private static TypeVisibility MapVisibility(Accessibility accessibility)
    {
        return accessibility switch
        {
            Accessibility.Public => TypeVisibility.Public,
            Accessibility.Protected => TypeVisibility.Protected,
            Accessibility.ProtectedOrInternal => TypeVisibility.Protected,
            Accessibility.ProtectedAndInternal => TypeVisibility.Protected,
            Accessibility.Internal => TypeVisibility.Internal,
            _ => TypeVisibility.Private
        };
    }
}
