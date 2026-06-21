using System;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Focus;

public sealed class GraphViewSnapshot
{
    public TypeGraph? Graph { get; set; }

    public GraphFocusRequest? Request { get; set; }

    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public string SelectedNodeId { get; set; } = string.Empty;
}
