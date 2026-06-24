using System.Collections.Generic;
using System.Linq;
using FoggyBalrog.MermaidDotNet;
using FoggyBalrog.MermaidDotNet.ClassDiagram.Model;
using MermaidDiagramExporter.Core;
using MermaidApi = FoggyBalrog.MermaidDotNet.Mermaid;
using MermaidClass = FoggyBalrog.MermaidDotNet.ClassDiagram.Model.Class;

namespace MermaidDiagramExporter.Export;

public static class MermaidGraphExporter
{
    public static string BuildDiagram(TypeGraph graph)
    {
        MermaidDotNetOptions options = new MermaidDotNetOptions
        {
            SanitizeInputs = true,
            ValidateInputs = true
        };

        var builder = MermaidApi.ClassDiagram(
            title: graph.Title,
            direction: ClassDiagramDirection.LeftToRight,
            options: options);

        Dictionary<string, MermaidClass> classMap = new Dictionary<string, MermaidClass>();
        List<TypeNodeData> orderedNodes = graph.Nodes
            .OrderBy(node => node.Namespace ?? string.Empty)
            .ThenBy(node => node.DisplayName)
            .ToList();

        foreach (IGrouping<string, TypeNodeData> group in orderedNodes.GroupBy(node => node.Namespace ?? string.Empty))
        {
            string namespaceLabel = string.IsNullOrEmpty(group.Key)
                ? "Global"
                : group.Key.Replace('<', '_').Replace('>', '_');
            if (string.IsNullOrEmpty(group.Key))
            {
                AddClasses(builder, group, classMap);
            }
            else
            {
                builder.AddNamespace(namespaceLabel, namespaceBuilder => AddClasses(namespaceBuilder, group, classMap));
            }
        }

        AddRelationships(builder, graph.Edges, classMap);
        return builder.Build();
    }

    private static void AddClasses(
        FoggyBalrog.MermaidDotNet.ClassDiagram.ClassDiagramBuilder builder,
        IEnumerable<TypeNodeData> nodes,
        IDictionary<string, MermaidClass> classMap)
    {
        foreach (TypeNodeData node in nodes)
        {
            builder.AddClass(
                node.Id,
                out MermaidClass mermaidClass,
                label: node.DisplayName,
                annotation: BuildAnnotation(node.Kind));

            classMap[node.Id] = mermaidClass;
            AddMembers(builder, mermaidClass, node.Members);
        }
    }

    private static void AddMembers(
        FoggyBalrog.MermaidDotNet.ClassDiagram.ClassDiagramBuilder builder,
        MermaidClass mermaidClass,
        IEnumerable<TypeMemberData> members)
    {
        foreach (TypeMemberData member in members)
        {
            if (member.Kind == TypeMemberKind.Method)
            {
                builder.AddMethod(
                    mermaidClass,
                    member.TypeName,
                    member.Name,
                    BuildVisibility(member),
                    member.Parameters.Select(parameter => (parameter.TypeName, parameter.Name)).ToArray());

                continue;
            }

            builder.AddProperty(mermaidClass, member.TypeName, member.Name);
        }
    }

    private static void AddRelationships(
        FoggyBalrog.MermaidDotNet.ClassDiagram.ClassDiagramBuilder builder,
        IEnumerable<TypeEdgeData> edges,
        IReadOnlyDictionary<string, MermaidClass> classMap)
    {
        HashSet<string> relationshipKeys = new HashSet<string>();

        foreach (TypeEdgeData edge in edges)
        {
            if (!classMap.ContainsKey(edge.FromNodeId) || !classMap.ContainsKey(edge.ToNodeId))
            {
                continue;
            }

            RelationshipType relationshipType = edge.Kind == TypeEdgeKind.Association
                ? RelationshipType.Association
                : RelationshipType.Inheritance;

            string label = edge.Kind == TypeEdgeKind.Implements ? "implements" : NullIfEmpty(edge.Label);
            string key = edge.FromNodeId + "|" + edge.ToNodeId + "|" + relationshipType + "|" + (label ?? string.Empty);
            if (!relationshipKeys.Add(key))
            {
                continue;
            }

            builder.AddRelationship(
                classMap[edge.FromNodeId],
                classMap[edge.ToNodeId],
                relationshipType,
                label: label);
        }
    }

    private static string BuildAnnotation(TypeNodeKind kind)
    {
        switch (kind)
        {
            case TypeNodeKind.Interface:
                return "interface";
            case TypeNodeKind.Enum:
                return "enumeration";
            case TypeNodeKind.StaticClass:
                return "static";
            case TypeNodeKind.AbstractClass:
                return "abstract";
            default:
                return null;
        }
    }

    private static Visibilities BuildVisibility(TypeMemberData member)
    {
        Visibilities visibility = Visibilities.None;

        switch (member.Visibility)
        {
            case TypeVisibility.Public:
                visibility |= Visibilities.Public;
                break;
            case TypeVisibility.Protected:
                visibility |= Visibilities.Protected;
                break;
            case TypeVisibility.Internal:
                visibility |= Visibilities.Internal;
                break;
            default:
                visibility |= Visibilities.Private;
                break;
        }

        if (member.IsStatic)
        {
            visibility |= Visibilities.Static;
        }

        if (member.IsAbstract)
        {
            visibility |= Visibilities.Abstract;
        }

        return visibility;
    }

    private static string NullIfEmpty(string value)
    {
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
