using System;
using System.Collections.Generic;
using System.Linq;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Layout;

// ───────────────────────── Models ─────────────────────────
public enum LayoutNodeRole { Real, ClusterInboundAnchor, ClusterOutboundAnchor, SelfLoopHelper }
public enum LayoutEdgeRole { Direct, BoundarySourceLink, BoundaryBridge, BoundaryTargetLink, SelfLoopSourceLink, SelfLoopBridge, SelfLoopTargetLink }
public enum LayoutDirection { LeftToRight, TopToBottom }

public sealed class LayoutGraph
{
    public string Title { get; set; } = "";
    public IReadOnlyList<LayoutNode> Nodes { get; set; } = Array.Empty<LayoutNode>();
    public IReadOnlyList<LayoutEdge> Edges { get; set; } = Array.Empty<LayoutEdge>();
    public IReadOnlyList<LayoutCluster> Clusters { get; set; } = Array.Empty<LayoutCluster>();
    public IReadOnlyList<LayoutSubgraph> ExtractedSubgraphs { get; set; } = Array.Empty<LayoutSubgraph>();
    public LayoutGraphMetadata Metadata { get; set; } = new();
}

public sealed class LayoutNode
{
    public string Id { get; set; } = "";
    public string ClusterId { get; set; } = "";
    public string Label { get; set; } = "";
    public LayoutNodeRole Role { get; set; } = LayoutNodeRole.Real;
    public string SourceNodeId { get; set; } = "";
    public string BadgeText { get; set; } = "";
    public IReadOnlyList<string> MemberLines { get; set; } = Array.Empty<string>();
    public float EstimatedWidth { get; set; }
    public float EstimatedHeight { get; set; }
    public float MeasuredWidth { get; set; }
    public float MeasuredHeight { get; set; }
    public bool IsMeasured { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}

public sealed class LayoutEdge
{
    public string Id { get; set; } = "";
    public string OriginalEdgeId { get; set; } = "";
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public TypeEdgeKind Kind { get; set; } = TypeEdgeKind.Association;
    public LayoutEdgeRole Role { get; set; } = LayoutEdgeRole.Direct;
}

public sealed class LayoutCluster
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public TypeGroupKind Kind { get; set; } = TypeGroupKind.Namespace;
    public string ParentClusterId { get; set; } = "";
    public IReadOnlyList<string> NodeIds { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> ChildClusterIds { get; set; } = Array.Empty<string>();
    public bool HasExternalConnections { get; set; }
    public string RepresentativeNodeId { get; set; } = "";
    public bool IsExtractedSubgraph { get; set; }
    public ClusterTitleMetrics TitleMetrics { get; set; } = new();
}

public sealed class LayoutSubgraph
{
    public string ClusterId { get; set; } = "";
    public LayoutGraph Graph { get; set; } = new();
    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;
    public LayoutSpacingProfile Spacing { get; set; } = new();
}

public sealed class LayoutResult
{
    public IReadOnlyDictionary<string, Rect> NodeBounds { get; set; } = new Dictionary<string, Rect>();
    public IReadOnlyDictionary<string, Rect> ClusterBounds { get; set; } = new Dictionary<string, Rect>();
    public IReadOnlyDictionary<string, string> NodeClusterIds { get; set; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, LayoutClusterVisual> ClusterVisuals { get; set; } = new Dictionary<string, LayoutClusterVisual>();
    public IReadOnlyList<LayoutEdgePath> EdgePaths { get; set; } = Array.Empty<LayoutEdgePath>();
    public Vector2 ContentSize { get; set; }
}

public sealed class LayoutEdgePath
{
    public string EdgeId { get; set; } = "";
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public TypeEdgeKind Kind { get; set; } = TypeEdgeKind.Association;
    public bool IsClippedToClusters { get; set; }
    public IReadOnlyList<Vector2> Points { get; set; } = Array.Empty<Vector2>();
}

public sealed class ClusterTitleMetrics
{
    public float LabelWidth { get; set; }
    public float LabelHeight { get; set; }
    public float TopMargin { get; set; }
    public float BottomMargin { get; set; }
    public float TotalMargin => TopMargin + LabelHeight + BottomMargin;
}

public sealed class LayoutSpacingProfile
{
    public float NodeSeparation { get; set; }
    public float RankSeparation { get; set; }
    public float MarginX { get; set; }
    public float MarginY { get; set; }
}

public sealed class LayoutGraphMetadata
{
    public string SourceDescription { get; set; } = "";
    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;
    public bool UsesMeasuredNodes { get; set; }
    public LayoutSpacingProfile Spacing { get; set; } = new();
}

public sealed class LayoutClusterVisual
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public ClusterTitleMetrics TitleMetrics { get; set; } = new();
}

// ───────────────────────── Primitives ─────────────────────────
public struct Rect
{
    public float X, Y, Width, Height;
    public Rect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }

    // Lowercase aliases (Unity compatibility)
    public float x { get => X; set => X = value; }
    public float y { get => Y; set => Y = value; }
    public float width { get => Width; set => Width = value; }
    public float height { get => Height; set => Height = value; }

    public float xMin => X;
    public float yMin => Y;
    public float xMax => X + Width;
    public float yMax => Y + Height;
    public Vector2 position { get => new(X, Y); set { X = value.X; Y = value.Y; } }
    public Vector2 center => new(X + Width * 0.5f, Y + Height * 0.5f);
    public static Rect MinMaxRect(float minX, float minY, float maxX, float maxY) => new(minX, minY, maxX - minX, maxY - minY);
}

public readonly struct Vector2
{
    public readonly float X, Y;
    public Vector2(float x, float y) { X = x; Y = y; }
    public float x => X;
    public float y => Y;
    public static Vector2 zero => new(0f, 0f);
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vector2 operator *(Vector2 a, float s) => new(a.X * s, a.Y * s);
    public float sqrMagnitude => X * X + Y * Y;
    public static float Distance(Vector2 a, Vector2 b) => (float)Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    public static implicit operator Vector2((float, float) t) => new(t.Item1, t.Item2);
}

public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static float Max(float a, float b, float c) => Math.Max(a, Math.Max(b, c));
    public static float Min(float a, float b, float c) => Math.Min(a, Math.Min(b, c));
    public static int Max(int a, int b) => Math.Max(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static int RoundToInt(float f) => (int)Math.Round(f);
    public static int CeilToInt(float f) => (int)Math.Ceiling(f);
    public static int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);
    public static float Clamp(float v, float min, float max) => Math.Clamp(v, min, max);
    public static float Abs(float f) => Math.Abs(f);
    public static float Sqrt(float f) => (float)Math.Sqrt(f);
}
