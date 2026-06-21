using System;
using System.Collections.Generic;

namespace MermaidDiagramExporter.Focus;

public sealed class GraphFocusRequest
{
    public IReadOnlyList<string> SeedNodeIds { get; set; } = Array.Empty<string>();

    public int AssociationDepth { get; set; } = 1;

    public GraphFocusTraversalMode TraversalMode { get; set; } = GraphFocusTraversalMode.UndirectedAssociations;

    public bool IncludeIncomingAssociations { get; set; } = true;

    public bool IncludeOutgoingAssociations { get; set; } = true;

    public bool IncludeInheritanceInsideFocusedSet { get; set; } = true;

    public bool IncludeImplementsInsideFocusedSet { get; set; } = true;

    public bool IncludeSeedNamespaces { get; set; } = true;

    public bool PreserveGroupKinds { get; set; } = true;
}
