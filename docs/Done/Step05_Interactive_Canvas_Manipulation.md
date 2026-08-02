# Step 05: Interactive Canvas Manipulation

## Overview
Add drag-to-reposition for individual nodes and entire namespace clusters, with Shift+drag for cluster movement. Overridden positions persist in the project cache. Include a layout reset button to discard overrides and re-run the layout engine.

## Dependencies
- **Step 01** — reads `CurrentSettings.EnableNodeDragging`, `PersistManualLayout`
- **Step 02** — cache service stores/retrieves the `ManualLayoutOverrides` dictionary

---

## Part A: Manual Position Override Storage Model

### 1. Create `src/MermaidDiagramExporter.Gui/Layout/ManualLayoutOverride.cs`

This DTO is persisted in the cache alongside the TypeGraph:

```csharp
using System;
using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Stores user-manually-adjusted node positions as deltas from the engine-computed positions.
/// Key = nodeId, Value = delta (offset) from the engine position.
/// Using deltas (not absolute positions) means adding new nodes won't break existing layouts.
/// </summary>
public sealed class ManualLayoutOverrides
{
    /// <summary>
    /// nodeId -> delta offset from engine-computed position.
    /// </summary>
    public Dictionary<string, Vector2> NodePositionDeltas { get; set; } = new();

    /// <summary>
    /// UTC timestamp of when these overrides were last saved.
    /// </summary>
    public DateTime LastSavedUtc { get; set; }

    /// <summary>
    /// Whether any overrides exist.
    /// </summary>
    public bool HasOverrides => NodePositionDeltas.Count > 0;

    /// <summary>
    /// Clears all overrides.
    /// </summary>
    public void Clear() => NodePositionDeltas.Clear();

    /// <summary>
    /// Records a manual override delta for a node.
    /// </summary>
    public void SetDelta(string nodeId, Vector2 delta)
    {
        if (delta.X == 0 && delta.Y == 0)
        {
            NodePositionDeltas.Remove(nodeId);
            return;
        }
        NodePositionDeltas[nodeId] = delta;
    }

    /// <summary>
    /// Gets the override delta for a node, or (0,0) if none.
    /// </summary>
    public Vector2 GetDelta(string nodeId)
    {
        return NodePositionDeltas.TryGetValue(nodeId, out var delta) ? delta : Vector2.zero;
    }
}
```

---

## Part B: Apply Overrides to Layout Result

### 2. Create `src/MermaidDiagramExporter.Gui/Layout/ManualLayoutApplier.cs`

This is called AFTER the layout engine runs but BEFORE the post-layout pipeline (or after it, depending on your preference). The advice in the prompt says to apply after the engine runs and before/within the post-layout pipeline:

```csharp
using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Layout;

/// <summary>
/// Applies manual position overrides to a LayoutResult.
/// Called after the layout engine computes positions, before the canvas renders.
/// </summary>
public static class ManualLayoutApplier
{
    /// <summary>
    /// Adjusts node bounds in the LayoutResult by applying stored manual deltas.
    /// Returns a NEW LayoutResult (immutable transform).
    /// </summary>
    public static LayoutResult ApplyOverrides(LayoutResult result, ManualLayoutOverrides overrides)
    {
        if (overrides == null || !overrides.HasOverrides)
            return result;

        var clone = PostLayoutResultUtility.CloneResult(result);
        var nodeBounds = (Dictionary<string, Rect>)clone.NodeBounds;

        foreach (var kvp in overrides.NodePositionDeltas)
        {
            string nodeId = kvp.Key;
            Vector2 delta = kvp.Value;
            if (nodeBounds.TryGetValue(nodeId, out var rect))
            {
                nodeBounds[nodeId] = new Rect(rect.X + delta.X, rect.Y + delta.Y, rect.Width, rect.Height);
            }
        }

        // Recalculate cluster bounds to encompass moved nodes
        RecalculateClusterBounds(clone, nodeBounds);

        return clone;
    }

    /// <summary>
    /// After moving nodes, recalculate cluster bounds to ensure they still contain their nodes.
    /// </summary>
    private static void RecalculateClusterBounds(LayoutResult result, Dictionary<string, Rect> nodeBounds)
    {
        var clusterBounds = (Dictionary<string, Rect>)result.ClusterBounds;
        foreach (var clusterId in clusterBounds.Keys.ToList())
        {
            // Find all nodes belonging to this cluster
            var nodeIdsInCluster = result.NodeClusterIds
                .Where(kvp => kvp.Value == clusterId)
                .Select(kvp => kvp.Key)
                .ToList();

            if (nodeIdsInCluster.Count == 0) continue;

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            foreach (var nodeId in nodeIdsInCluster)
            {
                if (nodeBounds.TryGetValue(nodeId, out var r))
                {
                    minX = Mathf.Min(minX, r.xMin);
                    minY = Mathf.Min(minY, r.yMin);
                    maxX = Mathf.Max(maxX, r.xMax);
                    maxY = Mathf.Max(maxY, r.yMax);
                }
            }

            if (minX < float.MaxValue)
            {
                // Add padding around the cluster
                float padding = 24;
                float titleHeight = 24;
                clusterBounds[clusterId] = new Rect(
                    minX - padding,
                    minY - padding - titleHeight,
                    maxX - minX + padding * 2,
                    maxY - minY + padding * 2 + titleHeight);
            }
        }

        // Recalculate content size
        float contentMaxX = nodeBounds.Values.Concat(clusterBounds.Values).Max(r => r.xMax);
        float contentMaxY = nodeBounds.Values.Concat(clusterBounds.Values).Max(r => r.yMax);
        result.ContentSize = new Vector2(contentMaxX + 40, contentMaxY + 52);
    }
}
```

---

## Part C: Integrate Overrides into LayoutEngine

### 3. Modify `src/MermaidDiagramExporter.Gui/LayoutEngine.cs`

The `LayoutEngine` class needs to accept and apply manual overrides:

```csharp
public class LayoutEngine
{
    private readonly GraphLayoutCoordinator _coordinator = new();

    /// <summary>
    /// Manual position overrides to apply after layout. Set before calling Layout().
    /// </summary>
    public ManualLayoutOverrides? ManualOverrides { get; set; }

    public (List<GraphNode> nodes, List<GraphEdge> edges) Layout(Core.TypeGraph graph)
    {
        var result = _coordinator.CreateLayout(graph);

        // Apply manual overrides if present
        if (ManualOverrides != null && ManualOverrides.HasOverrides)
        {
            result = ManualLayoutApplier.ApplyOverrides(result, ManualOverrides);
        }

        // ... rest of existing code ...
        // (the node/edge conversion that follows stays the same)
    }
}
```

---

## Part D: Canvas Drag Interaction

### 4. Modify `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`

Add drag state fields at the top of the class:

```csharp
// Dragging state
private GraphNode? _draggedNode;
private bool _isDraggingNode;
private bool _isDraggingCluster;
private string? _draggedClusterId;
private float _dragStartMouseX;
private float _dragStartMouseY;
private float _dragStartNodeX;
private float _dragStartNodeY;
private Dictionary<string, Vector2> _clusterDragStartPositions = new();

public ManualLayoutOverrides ManualOverrides { get; set; } = new();

/// <summary>
/// Raised when the user finishes dragging a node. MainWindow should persist the overrides.
/// </summary>
public event Action? ManualLayoutChanged;
```

**Modify `OnPointerPressed()`** to detect drag start vs. normal click:

```csharp
protected override void OnPointerPressed(PointerPressedEventArgs e)
{
    base.OnPointerPressed(e);
    var pos = e.GetPosition(this);
    var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);

    // Middle button or Ctrl+Left = pan (existing behavior)
    if (e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed ||
        (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
         (e.KeyModifiers & KeyModifiers.Control) != 0))
    {
        _isPanning = true;
        _lastMouseX = (float)pos.X;
        _lastMouseY = (float)pos.Y;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
        return;
    }

    // Left click on node = start drag (if enabled)
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
    {
        var hit = HitTest(worldPos);

        // Shift+click on a node = select entire cluster for dragging
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0 && hit != null)
        {
            string? clusterId = GetNodeClusterId(hit);
            if (clusterId != null)
            {
                StartClusterDrag(clusterId, worldPos, (float)pos.X, (float)pos.Y);
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
        }

        // Normal node drag
        if (hit != null)
        {
            StartNodeDrag(hit, worldPos, (float)pos.X, (float)pos.Y);
            if (hit != _selectedNode)
            {
                _selectedNode = hit;
                SelectionChanged?.Invoke(hit);
            }
            e.Pointer.Capture(this);
            e.Handled = true;
            return;
        }

        // Click on empty space (start pan)
        _isPanning = true;
        _lastMouseX = (float)pos.X;
        _lastMouseY = (float)pos.Y;
        Cursor = new Cursor(StandardCursorType.SizeAll);
        e.Pointer.Capture(this);
        e.Handled = true;
    }
}
```

**Add drag start helpers:**

```csharp
private void StartNodeDrag(GraphNode node, SKPoint worldPos, float screenX, float screenY)
{
    _draggedNode = node;
    _isDraggingNode = true;
    _isDraggingCluster = false;
    _draggedClusterId = null;
    _dragStartMouseX = worldPos.X;
    _dragStartMouseY = worldPos.Y;
    _dragStartNodeX = node.X;
    _dragStartNodeY = node.Y;
    Cursor = new Cursor(StandardCursorType.DragMove);
}

private void StartClusterDrag(string clusterId, SKPoint worldPos, float screenX, float screenY)
{
    _isDraggingNode = false;
    _isDraggingCluster = true;
    _draggedClusterId = clusterId;
    _draggedNode = null;
    _dragStartMouseX = worldPos.X;
    _dragStartMouseY = worldPos.Y;
    _clusterDragStartPositions.Clear();

    // Record start positions of all nodes in this cluster
    foreach (var node in _nodes)
    {
        if (GetNodeClusterId(node) == clusterId)
        {
            _clusterDragStartPositions[node.Id] = new Vector2(node.X, node.Y);
        }
    }
    Cursor = new Cursor(StandardCursorType.DragMove);
}

private string? GetNodeClusterId(GraphNode node)
{
    // Simple namespace-based cluster matching
    // In a real implementation, you'd want to pass the cluster mapping from LayoutResult
    return node.Namespace;
}
```

**Modify `OnPointerMoved()`** to handle dragging:

```csharp
protected override void OnPointerMoved(PointerEventArgs e)
{
    base.OnPointerMoved(e);
    var pos = e.GetPosition(this);

    // Panning (existing behavior)
    if (_isPanning)
    {
        float dx = (float)(pos.X - _lastMouseX);
        float dy = (float)(pos.Y - _lastMouseY);
        _panX += dx;
        _panY += dy;
        _lastMouseX = (float)pos.X;
        _lastMouseY = (float)pos.Y;
        e.Handled = true;
        Invalidate();
        return;
    }

    // Node dragging
    if (_isDraggingNode && _draggedNode != null)
    {
        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
        float deltaWorldX = worldPos.X - _dragStartMouseX;
        float deltaWorldY = worldPos.Y - _dragStartMouseY;

        float newX = _dragStartNodeX + deltaWorldX;
        float newY = _dragStartNodeY + deltaWorldY;

        // Apply delta as manual override (relative to engine position)
        Vector2 enginePos = GetEnginePosition(_draggedNode);
        Vector2 overrideDelta = new Vector2(newX - enginePos.X, newY - enginePos.Y);
        ManualOverrides.SetDelta(_draggedNode.Id, overrideDelta);

        _draggedNode.X = newX;
        _draggedNode.Y = newY;
        e.Handled = true;
        Invalidate();
        return;
    }

    // Cluster dragging
    if (_isDraggingCluster && _draggedClusterId != null)
    {
        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
        float deltaWorldX = worldPos.X - _dragStartMouseX;
        float deltaWorldY = worldPos.Y - _dragStartMouseY;

        foreach (var node in _nodes)
        {
            if (_clusterDragStartPositions.TryGetValue(node.Id, out var startPos))
            {
                float newX = startPos.X + deltaWorldX;
                float newY = startPos.Y + deltaWorldY;

                Vector2 enginePos = GetEnginePosition(node);
                Vector2 overrideDelta = new Vector2(newX - enginePos.X, newY - enginePos.Y);
                ManualOverrides.SetDelta(node.Id, overrideDelta);

                node.X = newX;
                node.Y = newY;
            }
        }
        e.Handled = true;
        Invalidate();
        return;
    }

    // Hover detection (existing behavior, but skip during drag)
    if (!_isDraggingNode && !_isDraggingCluster)
    {
        var worldPos = ScreenToWorld((float)pos.X, (float)pos.Y);
        var hovered = HitTest(worldPos);
        if (hovered != _hoveredNode)
        {
            _hoveredNode = hovered;
            Invalidate();
        }
    }
}
```

**Modify `OnPointerReleased()`** to end drag:

```csharp
protected override void OnPointerReleased(PointerReleasedEventArgs e)
{
    base.OnPointerReleased(e);

    if (_isDraggingNode || _isDraggingCluster)
    {
        _isDraggingNode = false;
        _isDraggingCluster = false;
        _draggedNode = null;
        _draggedClusterId = null;
        Cursor = Cursor.Default;
        e.Pointer.Capture(null);
        ManualLayoutChanged?.Invoke(); // notify MainWindow to persist
        e.Handled = true;
        return;
    }

    _isPanning = false;
    Cursor = Cursor.Default;
    e.Pointer.Capture(null);
    e.Handled = true;
}
```

**Add helper to get the engine-computed position:**

```csharp
/// <summary>
/// The engine-computed position before manual overrides were applied.
/// We store this by subtracting the current override from the displayed position.
/// </summary>
private Vector2 GetEnginePosition(GraphNode node)
{
    Vector2 delta = ManualOverrides.GetDelta(node.Id);
    return new Vector2(node.X - delta.X, node.Y - delta.Y);
}
```

**Optimize rendering during drag:**

The concern note says dragging should not re-render the entire bitmap. For the initial implementation, use the simple `Invalidate()` approach. If performance is poor with >200 nodes, add a fast path:

```csharp
// In RenderNow(), during active drag, only translate the bitmap and overlay the dragged element:
// (This is an optimization to be added if needed)
private bool _isInFastDrag;
```

For now, the standard invalidate approach is acceptable for initial implementation.

---

## Part E: Persist and Load Overrides

### 5. Extend `TypeGraphCacheService` to include manual overrides

Add a companion file to the cache. In `TypeGraphCacheService.SaveCache()`, after saving the TypeGraph, also save the overrides:

```csharp
public void SaveManualOverrides(ManualLayoutOverrides overrides, ProjectSettings settings)
{
    if (overrides == null || !overrides.HasOverrides) return;

    string cacheDir = _settingsService.ResolveCacheDirectory(settings);
    string path = Path.Combine(cacheDir, "layout.overrides.json");
    overrides.LastSavedUtc = DateTime.UtcNow;
    var json = JsonSerializer.Serialize(overrides, new JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(path, json);
}

public ManualLayoutOverrides LoadManualOverrides(ProjectSettings settings)
{
    string cacheDir = _settingsService.ResolveCacheDirectory(settings);
    string path = Path.Combine(cacheDir, "layout.overrides.json");
    if (!File.Exists(path)) return new ManualLayoutOverrides();

    try
    {
        var json = File.ReadAllText(path);
        var overrides = JsonSerializer.Deserialize<ManualLayoutOverrides>(json);
        return overrides ?? new ManualLayoutOverrides();
    }
    catch { return new ManualLayoutOverrides(); }
}
```

The DTO uses `Vector2` which needs a custom JSON converter since it's a struct. Add this converter:

```csharp
// In the same file, or create a separate converter file
public class Vector2JsonConverter : System.Text.Json.Serialization.JsonConverter<Vector2>
{
    public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        float x = root.GetProperty("x").GetSingle();
        float y = root.GetProperty("y").GetSingle();
        return new Vector2(x, y);
    }

    public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteEndObject();
    }
}
```

Register it in `ManualLayoutOverrides` serialization by using `JsonSerializerOptions` with the converter, or add `[JsonConverter(typeof(Vector2JsonConverter))]` attribute to the `NodePositionDeltas` property.

### 6. Wire everything in `MainWindow.axaml.cs`

**Add fields:**
```csharp
private ManualLayoutOverrides _manualOverrides = new();
```

**In `OnScan()` or `SetDisplayedGraph()`**, load overrides before layout:
```csharp
// Before calling _layoutEngine.Layout():
if (_currentSettings.PersistManualLayout)
{
    _manualOverrides = _cacheService.LoadManualOverrides(_currentSettings);
}
else
{
    _manualOverrides = new ManualLayoutOverrides();
}
_layoutEngine.ManualOverrides = _manualOverrides;
```

**After layout**, pass overrides to canvas:
```csharp
GraphCanvasView.ManualOverrides = _manualOverrides;
```

**Subscribe to canvas drag events** (in the constructor):
```csharp
GraphCanvasView.ManualLayoutChanged += OnManualLayoutChanged;
```

**Handler:**
```csharp
private void OnManualLayoutChanged()
{
    if (_currentSettings.PersistManualLayout)
    {
        _cacheService.SaveManualOverrides(GraphCanvasView.ManualOverrides, _currentSettings);
    }
}
```

**Add "Reset Layout" button handler:**
```csharp
private void OnResetLayout(object? sender, RoutedEventArgs e)
{
    _manualOverrides.Clear();
    _cacheService.SaveManualOverrides(_manualOverrides, _currentSettings);

    // Re-run layout without overrides
    if (_currentGraph != null)
    {
        _layoutEngine.ManualOverrides = _manualOverrides;
        SetDisplayedGraph(_currentGraph);
    }
}
```

Add the button to the toolbar/XAML:
```xml
<Button x:Name="ResetLayoutButton" Content="Reset Layout" Click="OnResetLayout" />
```

---

## Testing Checklist

1. Scan a project. Drag a single node — it should move.
2. Release — position should persist (reopen project, load from cache, node should be at overridden position).
3. Hold Shift, click a node in a namespace, drag — all nodes in that cluster should move together.
4. Release Shift drag, then drag an individual node within that cluster — only that node moves.
5. Click "Reset Layout" — all nodes return to engine positions.
6. Disable "Persist manual layout" in Settings, drag nodes, close and reopen — positions should reset.
7. Add a new file/class and rescan — previously overridden nodes should maintain their relative positions.
