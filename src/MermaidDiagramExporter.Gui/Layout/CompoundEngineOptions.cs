namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Options read only by the compound engine (<see cref="Compound.CompoundLayeredLayoutEngine"/>
/// and its helpers).
/// </summary>
public sealed class CompoundEngineOptions
{
    /// <summary>Weight used for cluster containment edges (docs/06 Step 2c).</summary>
    public float ClusterContainmentEdgeWeight { get; set; } = 24f;

    /// <summary>Number of coordinate-assignment passes (docs/08 Part A2).</summary>
    public int CoordinateAssignmentPasses { get; set; } = 6;
}
