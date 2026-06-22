using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Search;

/// <summary>
/// Fast inverted index for symbol search. Built from TypeGraph after scan.
/// Supports namespace-qualified and partial queries.
/// </summary>
public sealed class SymbolIndex
{
    // nodeId -> node data for quick lookup
    public IReadOnlyDictionary<string, TypeNodeData> NodesById { get; private set; }
        = new Dictionary<string, TypeNodeData>();

    // "Player" -> ["T_MyGame_Player", "T_MyGame_PlayerController"]
    public IReadOnlyDictionary<string, List<string>> NameIndex { get; private set; }
        = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    // "MyGame" -> ["T_MyGame_Player", ...] (all nodes in that namespace)
    public IReadOnlyDictionary<string, List<string>> NamespaceIndex { get; private set; }
        = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

    // "Health" -> [("T_Player", "Property", "Health", 42), ...]
    public IReadOnlyDictionary<string, List<MemberIndexEntry>> MemberIndex { get; private set; }
        = new Dictionary<string, List<MemberIndexEntry>>(StringComparer.OrdinalIgnoreCase);

    // All unique namespace prefixes for autocomplete: "MyGame", "MyGame.AI", "MyGame.UI"
    public IReadOnlyList<string> NamespacePrefixes { get; private set; }
        = Array.Empty<string>();

    public static SymbolIndex Build(TypeGraph graph)
    {
        var nodesById = graph.Nodes.ToDictionary(n => n.Id);
        var nameIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var namespaceIndex = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var memberIndex = new Dictionary<string, List<MemberIndexEntry>>(StringComparer.OrdinalIgnoreCase);
        var namespacePrefixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var node in graph.Nodes)
        {
            // Index node display name
            IndexToken(nameIndex, node.DisplayName, node.Id);
            // Also index the full name
            IndexToken(nameIndex, node.FullName, node.Id);

            // Index namespace and all its prefixes
            if (!string.IsNullOrEmpty(node.Namespace))
            {
                IndexToken(namespaceIndex, node.Namespace, node.Id);
                namespacePrefixes.Add(node.Namespace);

                // Add parent prefixes: "A.B.C" -> "A.B", "A"
                string ns = node.Namespace;
                while (true)
                {
                    int lastDot = ns.LastIndexOf('.');
                    if (lastDot < 0) break;
                    ns = ns.Substring(0, lastDot);
                    namespacePrefixes.Add(ns);
                }
            }

            // Index members
            foreach (var member in node.Members)
            {
                var entry = new MemberIndexEntry(node.Id, node.DisplayName, node.Namespace,
                    member.Kind.ToString(), member.Name, member.TypeName);

                IndexMember(memberIndex, member.Name, entry);
                IndexMember(memberIndex, member.TypeName, entry);
            }
        }

        return new SymbolIndex
        {
            NodesById = nodesById,
            NameIndex = nameIndex,
            NamespaceIndex = namespaceIndex,
            MemberIndex = memberIndex,
            NamespacePrefixes = namespacePrefixes.OrderBy(n => n).ToList()
        };
    }

    private static void IndexToken(Dictionary<string, List<string>> index, string token, string nodeId)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (!index.TryGetValue(token, out var list))
        {
            list = new List<string>();
            index[token] = list;
        }
        if (!list.Contains(nodeId))
            list.Add(nodeId);
    }

    private static void IndexMember(Dictionary<string, List<MemberIndexEntry>> index, string token, MemberIndexEntry entry)
    {
        if (string.IsNullOrWhiteSpace(token)) return;
        if (!index.TryGetValue(token, out var list))
        {
            list = new List<MemberIndexEntry>();
            index[token] = list;
        }
        list.Add(entry);
    }
}

public sealed class MemberIndexEntry
{
    public string NodeId { get; }
    public string NodeDisplayName { get; }
    public string NodeNamespace { get; }
    public string MemberKind { get; }
    public string MemberName { get; }
    public string MemberTypeName { get; }

    public MemberIndexEntry(string nodeId, string nodeDisplayName, string nodeNamespace,
        string memberKind, string memberName, string memberTypeName)
    {
        NodeId = nodeId;
        NodeDisplayName = nodeDisplayName;
        NodeNamespace = nodeNamespace;
        MemberKind = memberKind;
        MemberName = memberName;
        MemberTypeName = memberTypeName;
    }
}
