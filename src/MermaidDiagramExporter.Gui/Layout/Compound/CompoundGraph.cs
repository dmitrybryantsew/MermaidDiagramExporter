using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// A node in the unified ranking/ordering graph used by the new compound
/// layout engine. Either a real LayoutNode, a cluster border marker, or an
/// edge-segment dummy (intermediate point of a long edge).
/// This is the moral equivalent of dagre's single flattened-but-parent-tagged
/// graph used during rank/order (see docs/01-research-findings.md §2, §4).
/// </summary>
public enum CompoundNodeKind
{
    Real,
    ClusterBorderTop,
    ClusterBorderBottom,
    EdgeSegment
}

public sealed class CompoundNode
{
    /// <summary>Stable, unique across the whole compound graph.</summary>
    public string Id { get; set; } = "";

    public CompoundNodeKind Kind { get; set; }

    /// <summary>Set iff Kind == Real.</summary>
    public string? SourceLayoutNodeId { get; set; }

    /// <summary>The cluster this node (or border) directly belongs to; null = top-level / no cluster.</summary>
    public string? OwningClusterId { get; set; }

    /// <summary>Set iff Kind == EdgeSegment — which LayoutEdge this dummy is a segment of.</summary>
    public string? OriginalEdgeId { get; set; }

    public float Width { get; set; }
    public float Height { get; set; }

    /// <summary>Filled in by the ranking phase.</summary>
    public int Rank { get; set; }

    /// <summary>Filled in by the ordering phase.</summary>
    public int OrderInRank { get; set; }

    /// <summary>Filled in by the coordinate-assignment phase.</summary>
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class CompoundEdge
{
    public string FromId { get; set; } = "";   // CompoundNode.Id
    public string ToId { get; set; } = "";     // CompoundNode.Id
    public float Weight { get; set; } = 1f;    // higher = ranker tries harder to keep this edge short
    public int MinRankSpan { get; set; } = 1;  // normally 1; nesting/border edges may differ
    public bool IsContainment { get; set; }    // true for cluster containment edges (06 Step 2c/2d)
    public bool IsReversedForRanking { get; set; } // true if this edge was flipped to break a cycle
    /// <summary>The original TypeEdgeKind, preserved so dummy-chain projection can
    /// produce a canonical edge key compatible with TypeEdgeData.CreateEdgeId.</summary>
    public MermaidDiagramExporter.Core.TypeEdgeKind Kind { get; set; }
        = MermaidDiagramExporter.Core.TypeEdgeKind.Association;
    /// <summary>The original LayoutEdge.Id, if this edge originated from a LayoutEdge.</summary>
    public string? OriginalLayoutEdgeId { get; set; }
}

public sealed class ClusterBorderChain
{
    public string ClusterId { get; set; } = "";
    /// <summary>Rank -> the border CompoundNode id at that rank, for the cluster's "low" side
    /// (left, if rankdir is the LeftToRight direction this codebase already defaults to).</summary>
    public Dictionary<int, string> LowBorderByRank { get; } = new();
    /// <summary>Rank -> the border CompoundNode id at that rank, for the cluster's "high" side.</summary>
    public Dictionary<int, string> HighBorderByRank { get; } = new();
    public int MinRank { get; set; }
    public int MaxRank { get; set; }
}

public sealed class CompoundGraph
{
    public List<CompoundNode> Nodes { get; } = new();
    public List<CompoundEdge> Edges { get; } = new();

    /// <summary>Key = clusterId, Value = parent clusterId or null if top-level.</summary>
    public Dictionary<string, string?> ClusterParent { get; } = new();
    /// <summary>Key = clusterId, Value = direct child cluster ids (namespace nesting).</summary>
    public Dictionary<string, List<string>> ClusterChildren { get; } = new();
    /// <summary>Key = clusterId, Value = the cluster's border chain info.</summary>
    public Dictionary<string, ClusterBorderChain> ClusterBorders { get; } = new();

    /// <summary>Key = original LayoutEdge id, Value = ordered list of CompoundNode ids
    /// (segment dummies + endpoints) that represent the edge's polyline path.</summary>
    public Dictionary<string, List<string>> EdgeDummyChains { get; } = new();
}
