# Step 06: Minimap Navigation

## Overview
Add a small minimap control in the corner of the canvas showing the full graph overview with a viewport rectangle. Clicking on the minimap jumps the main canvas viewport. Helps navigate large graphs (>200 nodes).

## Dependencies
- **Step 01** — reads `CurrentSettings.ShowMinimap`
- **Step 05** — manual layout overrides affect node positions that the minimap must reflect

---

## Part A: Minimap Control

### 1. Create `src/MermaidDiagramExporter.Gui/MinimapControl.axaml`

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="MermaidDiagramExporter.Gui.MinimapControl"
             Width="200"
             Height="150"
             IsVisible="False">
  <Border Background="#15191E"
          BorderBrush="#3A4250"
          BorderThickness="1"
          CornerRadius="4">
    <Panel>
      <!-- The minimap bitmap -->
      <Image x:Name="MinimapImage" Stretch="Uniform" />
      <!-- Viewport overlay rectangle -->
      <Canvas x:Name="ViewportOverlay">
        <Border x:Name="ViewportRect"
                BorderBrush="#FFE040"
                BorderThickness="1"
                Background="#FFE04020"
                IsHitTestVisible="False" />
      </Canvas>
    </Panel>
  </Border>
</UserControl>
```

### 2. Create `src/MermaidDiagramExporter.Gui/MinimapControl.axaml.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using SkiaSharp;

namespace MermaidDiagramExporter.Gui;

/// <summary>
/// A minimap showing the full graph with a viewport rectangle overlay.
/// Clicking on the minimap pans the main canvas to that location.
/// </summary>
public partial class MinimapControl : UserControl
{
    private List<GraphNode> _nodes = new();
    private List<GraphEdge> _edges = new();
    private Rect _graphBounds; // total bounds of all nodes
    private float _mainCanvasZoom = 1f;
    private float _mainCanvasPanX = 0f;
    private float _mainCanvasPanY = 0f;
    private float _mainCanvasViewportW = 800f;
    private float _mainCanvasViewportH = 600f;

    // Colors (match GraphCanvas dark theme)
    private static readonly SKColor MinimapBg = new(0x15, 0x19, 0x1E);
    private static readonly SKColor MinimapNodeFill = new(0x2D, 0x33, 0x3F);
    private static readonly SKColor MinimapNodeStroke = new(0x4A, 0x6A, 0x8A);
    private static readonly SKColor MinimapEdgeColor = new(0x3A, 0x42, 0x50);
    private static readonly SKColor ViewportBorderColor = new(0xFF, 0xE0, 0x40);
    private static readonly SKColor ViewportFillColor = new(0xFF, 0xE0, 0x40);

    /// <summary>
    /// Raised when the user clicks on the minimap. Arguments are the requested pan offset.
    /// </summary>
    public event Action<float, float>? ViewportJumpRequested;

    public MinimapControl()
    {
        InitializeComponent();
        PointerPressed += OnMinimapPointerPressed;
    }

    /// <summary>
    /// Updates the minimap content. Call whenever the graph changes.
    /// </summary>
    public void SetGraph(List<GraphNode> nodes, List<GraphEdge> edges)
    {
        _nodes = nodes;
        _edges = edges;
        RecalculateGraphBounds();
        RenderMinimap();
        UpdateViewportRect();
    }

    /// <summary>
    /// Updates the viewport rectangle position to match the main canvas view state.
    /// Call on every pan/zoom of the main canvas.
    /// </summary>
    public void UpdateViewport(float zoom, float panX, float panY, float viewW, float viewH)
    {
        _mainCanvasZoom = zoom;
        _mainCanvasPanX = panX;
        _mainCanvasPanY = panY;
        _mainCanvasViewportW = viewW;
        _mainCanvasViewportH = viewH;
        UpdateViewportRect();
    }

    private void RecalculateGraphBounds()
    {
        if (_nodes.Count == 0)
        {
            _graphBounds = new Rect(0, 0, 200, 150);
            return;
        }

        float minX = _nodes.Min(n => n.X);
        float minY = _nodes.Min(n => n.Y);
        float maxX = _nodes.Max(n => n.X + n.Width);
        float maxY = _nodes.Max(n => n.Y + n.Height);

        // Add padding
        float padding = 50;
        _graphBounds = new Rect(minX - padding, minY - padding,
            maxX - minX + padding * 2, maxY - minY + padding * 2);
    }

    private void RenderMinimap()
    {
        if (_nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            MinimapImage.Source = null;
            return;
        }

        int w = (int)Bounds.Width;
        int h = (int)Bounds.Height;
        if (w <= 0 || h <= 0) return;

        float scaleX = w / _graphBounds.Width;
        float scaleY = h / _graphBounds.Height;
        float minimapScale = Math.Min(scaleX, scaleY);

        using var bitmap = new SKBitmap(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(MinimapBg);

        // Center the graph in the minimap
        float offsetX = (w - _graphBounds.Width * minimapScale) / 2 - _graphBounds.X * minimapScale;
        float offsetY = (h - _graphBounds.Height * minimapScale) / 2 - _graphBounds.Y * minimapScale;

        canvas.Translate(offsetX, offsetY);
        canvas.Scale(minimapScale);

        // Draw edges (simplified)
        using var edgePaint = new SKPaint
        {
            Color = MinimapEdgeColor,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f / minimapScale, // counter-scale stroke
            IsAntialias = true
        };

        foreach (var edge in _edges)
        {
            if (edge.FromNode == null || edge.ToNode == null) continue;
            float x1 = edge.FromNode.X + edge.FromNode.Width / 2;
            float y1 = edge.FromNode.Y + edge.FromNode.Height / 2;
            float x2 = edge.ToNode.X + edge.ToNode.Width / 2;
            float y2 = edge.ToNode.Y + edge.ToNode.Height / 2;
            canvas.DrawLine(x1, y1, x2, y2, edgePaint);
        }

        // Draw nodes (simplified as rectangles)
        using var nodeFillPaint = new SKPaint { Color = MinimapNodeFill, Style = SKPaintStyle.Fill, IsAntialias = true };
        using var nodeStrokePaint = new SKPaint { Color = MinimapNodeStroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1f / minimapScale, IsAntialias = true };

        foreach (var node in _nodes)
        {
            canvas.DrawRect(node.X, node.Y, node.Width, node.Height, nodeFillPaint);
            canvas.DrawRect(node.X, node.Y, node.Width, node.Height, nodeStrokePaint);
        }

        // Convert to Avalonia bitmap
        using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
        var ms = new System.IO.MemoryStream(data.ToArray());
        var avaloniaBitmap = new Bitmap(ms);
        MinimapImage.Source = avaloniaBitmap;
    }

    private void UpdateViewportRect()
    {
        if (_nodes.Count == 0 || Bounds.Width <= 1 || Bounds.Height <= 1)
        {
            ViewportRect.IsVisible = false;
            return;
        }

        ViewportRect.IsVisible = true;

        int w = (int)Bounds.Width;
        int h = (int)Bounds.Height;
        float scaleX = w / _graphBounds.Width;
        float scaleY = h / _graphBounds.Height;
        float minimapScale = Math.Min(scaleX, scaleY);

        float offsetX = (w - _graphBounds.Width * minimapScale) / 2 - _graphBounds.X * minimapScale;
        float offsetY = (h - _graphBounds.Height * minimapScale) / 2 - _graphBounds.Y * minimapScale;

        // Convert main canvas viewport to minimap coordinates
        float vpX = (-_mainCanvasPanX / _mainCanvasZoom) * minimapScale + offsetX;
        float vpY = (-_mainCanvasPanY / _mainCanvasZoom) * minimapScale + offsetY;
        float vpW = (_mainCanvasViewportW / _mainCanvasZoom) * minimapScale;
        float vpH = (_mainCanvasViewportH / _mainCanvasZoom) * minimapScale;

        // Clamp to minimap bounds
        vpX = Math.Max(0, Math.Min(w, vpX));
        vpY = Math.Max(0, Math.Min(h, vpY));
        vpW = Math.Max(10, Math.Min(w, vpW));
        vpH = Math.Max(10, Math.Min(h, vpH));

        Canvas.SetLeft(ViewportRect, vpX);
        Canvas.SetTop(ViewportRect, vpY);
        ViewportRect.Width = vpW;
        ViewportRect.Height = vpH;
    }

    private void OnMinimapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var pos = e.GetPosition(this);
        float clickX = (float)pos.X;
        float clickY = (float)pos.Y;

        // Convert minimap click to main canvas pan coordinates
        int w = (int)Bounds.Width;
        int h = (int)Bounds.Height;
        if (w <= 0 || h <= 0) return;

        float scaleX = w / _graphBounds.Width;
        float scaleY = h / _graphBounds.Height;
        float minimapScale = Math.Min(scaleX, scaleY);
        float offsetX = (w - _graphBounds.Width * minimapScale) / 2 - _graphBounds.X * minimapScale;
        float offsetY = (h - _graphBounds.Height * minimapScale) / 2 - _graphBounds.Y * minimapScale;

        // Click position in world coordinates
        float worldX = (clickX - offsetX) / minimapScale;
        float worldY = (clickY - offsetY) / minimapScale;

        // Set main canvas pan so the clicked point is centered
        float newPanX = _mainCanvasViewportW / 2 - worldX * _mainCanvasZoom;
        float newPanY = _mainCanvasViewportH / 2 - worldY * _mainCanvasZoom;

        ViewportJumpRequested?.Invoke(newPanX, newPanY);
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_nodes.Count > 0)
            RenderMinimap();
    }
}
```

---

## Part B: Integrate Minimap with GraphCanvas and MainWindow

### 3. Modify `GraphCanvas.cs` to expose viewport state

The minimap needs to know the canvas zoom/pan on every change. Add an event:

```csharp
/// <summary>
/// Raised when zoom or pan changes. Used by the minimap to update its viewport rectangle.
/// </summary>
public event Action<float, float, float, float, float>? ViewportChanged;
```

In `Invalidate()` or after any zoom/pan change, raise the event. Add a helper:

```csharp
private void NotifyViewportChanged()
{
    ViewportChanged?.Invoke(_zoom, _panX, _panY, (float)Bounds.Width, (float)Bounds.Height);
}
```

Call `NotifyViewportChanged()` at the end of:
- `ZoomBy()`
- `FitToScreen()`
- `RecalculateLayout()`
- `OnPointerMoved()` (during panning)
- `OnPointerWheelChanged()`
- `CenterOnNode()` (from Step 04)

### 4. Place MinimapControl in the main layout

In `MainWindow.axaml`, position the minimap in the bottom-right corner of the canvas area using a `Grid` overlay:

```xml
<!-- Inside the panel that contains GraphCanvasView -->
<Panel>
  <!-- Main canvas -->
  <local:GraphCanvas x:Name="GraphCanvasView" />

  <!-- Minimap overlay -->
  <local:MinimapControl x:Name="MinimapView"
                        HorizontalAlignment="Right"
                        VerticalAlignment="Bottom"
                        Margin="12" />
</Panel>
```

### 5. Wire in `MainWindow.axaml.cs`

**In the constructor**, wire events:
```csharp
GraphCanvasView.ViewportChanged += (zoom, panX, panY, viewW, viewH) =>
{
    MinimapView.UpdateViewport(zoom, panX, panY, viewW, viewH);
};

MinimapView.ViewportJumpRequested += (newPanX, newPanY) =>
{
    GraphCanvasView.SetPan(newPanX, newPanY);
};
```

**Add `SetPan()` to `GraphCanvas.cs`:**

```csharp
public void SetPan(float panX, float panY)
{
    _panX = panX;
    _panY = panY;
    Invalidate();
}
```

**In `SetDisplayedGraph()`**, update the minimap:
```csharp
GraphCanvasView.SetGraph(nodes, edges);
MinimapView.SetGraph(nodes, edges);
MinimapView.IsVisible = _currentSettings.ShowMinimap;
```

**Handle settings changes** — when settings are saved and `ShowMinimap` changed:
```csharp
// In OnOpenSettings, after dialog closes:
if (window.SavedSettings != null)
{
    _currentSettings = window.SavedSettings;
    MinimapView.IsVisible = _currentSettings.ShowMinimap;
    StatsText.Text = "Settings saved";
}
```

---

## Testing Checklist

1. Scan a large project. The minimap should appear in the bottom-right with a small overview.
2. Pan/zoom the main canvas — the yellow viewport rectangle on the minimap should move correspondingly.
3. Click on the minimap — the main canvas should jump to center on that location.
4. Go to Settings, uncheck "Show minimap" — the minimap should disappear immediately.
5. Check "Show minimap" again — it should reappear.
6. After dragging nodes (Step 05), the minimap should update to reflect new positions.
7. Verify the minimap renders correctly even with 0 nodes (empty state).
