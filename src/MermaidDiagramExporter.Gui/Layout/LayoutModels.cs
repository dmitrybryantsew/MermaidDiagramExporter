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
    public float Width { get; set; }
    public float Height { get; set; }
}

public sealed class LayoutEdge
{
    public string Id { get; set; } = "";
    public string FromNodeId { get; set; } = "";
    public string ToNodeId { get; set; } = "";
    public TypeEdgeKind Kind { get; set; } = TypeEdgeKind.Association;
    public LayoutEdgeRole Role { get; set; } = LayoutEdgeRole.Direct;
}

public sealed class LayoutCluster
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public IReadOnlyList<string> NodeIds { get; set; } = Array.Empty<string>();
}

public sealed class LayoutOptions
{
    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;
    public float RankSpacing { get; set; } = 90f;
    public float ClusterSpacing { get; set; } = 30f;
    public float ComponentSpacing { get; set; } = 60f;
    public float GroupLeftPadding { get; set; } = 18f;
    public float GroupTopPadding { get; set; } = 34f;
    public float GroupWidth { get; set; } = 320f;
    public float GroupSpacing { get; set; } = 26f;
    public float NodeSpacing { get; set; } = 18f;
    public float OuterMarginX { get; set; } = 40f;
    public float OuterMarginY { get; set; } = 52f;
    public float NodeWidth { get; set; } = 280f;
    public float GroupBottomPadding { get; set; } = 18f;
    public float ClusterTitleHorizontalPadding { get; set; } = 24f;
    public float NodeColumnSpacing { get; set; } = 16f;
    public float TargetRowWidth { get; set; } = 2400f;
    public float StructuredClusterMaxRowWidth { get; set; } = 980f;
    public int StructuredClusterMaxNodesPerRow { get; set; } = 3;
    public float StructuredNodeColumnSpacing { get; set; } = 24f;
    public float StructuredRankGap { get; set; } = 34f;
    public float StructuredWrappedRowGap { get; set; } = 14f;
    public float StructuredRowIndentStep { get; set; } = 18f;
    public float StructuredRowMaxIndent { get; set; } = 56f;
    public float StructuredRowCenteringBias { get; set; } = 0.10f;
    public float MinimumContentWidth { get; set; } = 2200f;
    public float MinimumContentHeight { get; set; } = 2200f;
}

public sealed class LayoutResult
{
    public Dictionary<string, Rect> NodeBounds { get; set; } = new();
    public Dictionary<string, Rect> ClusterBounds { get; set; } = new();
    public Dictionary<string, string> NodeClusterIds { get; set; } = new();
    public Vector2 ContentSize { get; set; }
}

public struct Rect
{
    public float X, Y, Width, Height;
    public Rect(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
    public float xMin => X;
    public float yMin => Y;
    public float xMax => X + Width;
    public float yMax => Y + Height;
    public Vector2 position => new(X, Y);
    public static Rect MinMaxRect(float minX, float minY, float maxX, float maxY) => new(minX, minY, maxX - minX, maxY - minY);
}

public readonly struct Vector2
{
    public readonly float X, Y;
    public Vector2(float x, float y) { X = x; Y = y; }
    public static Vector2 operator +(Vector2 a, Vector2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vector2 operator -(Vector2 a, Vector2 b) => new(a.X - b.X, a.Y - b.Y);
    public static implicit operator Vector2((float, float) t) => new(t.Item1, t.Item2);
}

public static class Mathf
{
    public static float Max(float a, float b) => Math.Max(a, b);
    public static float Min(float a, float b) => Math.Min(a, b);
    public static int Max(int a, int b) => Math.Max(a, b);
    public static int Min(int a, int b) => Math.Min(a, b);
    public static int RoundToInt(float f) => (int)Math.Round(f);
    public static int CeilToInt(float f) => (int)Math.Ceiling(f);
    public static int Clamp(int v, int min, int max) => Math.Clamp(v, min, max);
    public static float Sqrt(float f) => (float)Math.Sqrt(f);
}
