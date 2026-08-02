# 02 — Mode Toggle Architecture

## The principle

Analyze Mode and Design Mode are **two entry points to the same canvas**.
They share:
- The canvas (GraphCanvas + CanvasRenderer)
- The hit-testing service (HitTestService)
- The layout engines (LayeredLayoutEngine, CompoundLayeredLayoutEngine)
- The post-layout pipeline (PostLayoutPipeline)
- The edge routing service (EdgeRoutingService)
- The export pipeline (MermaidGraphExporter, PNG export, etc.)
- The save/load infrastructure (settings, cache)

They differ in:
- **How the graph gets built**: Analyze = Roslyn scan; Design = user drawing.
- **What tools are available**: Analyze = read-only navigation; Design =
  create/edit/delete/connect.
- **What the toolbar shows**: Analyze = scan/browse/focus; Design = add class/
  add edge/undo/redo/save.

## Mode state

A single `DesignModeController` (sibling to the existing `GraphLayoutCoordinator`)
owns:
- The current `Mode` enum (`Analyze | Design`)
- The current `DesignGraph` (only meaningful in Design Mode)
- The undo/redo stacks
- The dirty flag (unsaved changes)

```csharp
public enum AppMode { Analyze, Design }

public sealed class DesignModeController
{
    public AppMode CurrentMode { get; private set; } = AppMode.Analyze;
    public DesignGraph? CurrentDesign { get; private set; }
    public bool IsDirty { get; private set; }

    public event EventHandler<AppMode>? ModeChanged;
    public event EventHandler? GraphChanged;

    public void EnterDesignMode(DesignGraph? startingFrom = null);
    public void EnterAnalyzeMode();
    public void OnGraphMutated();  // marks dirty, fires GraphChanged
    public bool CanUndo { get; }
    public bool CanRedo { get; }
    public void Undo();
    public void Redo();
}
```

## Mode switcher UI

A toggle in the top-left of the sidebar (or in the existing nav button row):

```
┌─────────────────────────────────┐
│ [ Analyze | Design ]            │  ← segmented toggle
└─────────────────────────────────┘
```

Clicking switches modes. When switching Analyze → Design:
- If a graph is loaded, prompt: "Start blank" or "Start from current scan".
- If nothing is loaded, start blank.

When switching Design → Analyze:
- If dirty, prompt: "Discard unsaved changes?" or "Save first".
- If not dirty, switch immediately.

The toggle is always visible — you can always see which mode you're in.

## Toolbar architecture

The sidebar has different tool rows per mode. The cleanest approach:

```xml
<Grid>
  <StackPanel IsVisible="{Binding !IsDesignMode}">
    <!-- existing Analyze Mode controls: Folder picker, nav buttons, focus, etc. -->
  </StackPanel>
  <StackPanel IsVisible="{Binding IsDesignMode}">
    <!-- Design Mode controls: Add Class, Add Edge, Undo, Redo, Save, Load -->
  </StackPanel>
</Grid>
```

Both panels are always in the visual tree (no dynamic add/remove), just
visibility-toggled. This avoids layout thrash when switching modes.

## Canvas interaction differs by mode

The canvas itself doesn't know which mode it's in. Instead, the input handlers
in `GraphCanvas` consult the `DesignModeController.CurrentMode` to decide what
a click/drag means:

| Gesture | Analyze Mode | Design Mode |
|---------|--------------|-------------|
| Click on empty canvas | Pan (existing behavior — `GraphCanvas.OnPointerPressed` always starts a pan on empty-canvas left-click) | Add new class at click position |
| Click on class | Select (focus inspector) | Select (show edit handles) |
| Double-click class | Open in explorer | Edit name inline |
| Drag class header | Reposition (manual layout) | Reposition |
| Drag from edge port | Nothing | Start edge creation |
| Drag to another class | Nothing | Complete edge creation |
| Right-click | Context menu (focus, etc.) | Context menu (delete, rename, etc.) |
| Delete key | Nothing | Delete selection |
| Ctrl+Z | Nothing | Undo |

The `GraphCanvas` already handles drag-to-reposition (the existing
`StartNodeDrag` / `StartClusterDrag` paths). Design Mode reuses those for
moving classes. The new behaviors (add class, create edge, delete) are added
as additional input handlers gated by `DesignModeController.CurrentMode == Design`.

## What does NOT change

- `GraphCanvas.RenderNow()` rendering pipeline — unchanged. Still the
  single render entry point.
- `CanvasRenderer.DrawNodes(...)` — unchanged. Still paints class bodies.
- `HitTestService` — unchanged. Still used for Analyze Mode node hit-testing.
  (Design Mode uses a separate `DesignHitTestService` for sub-region
  hit-testing; see doc 04.)
- `LayoutEngine` and `GraphLayoutCoordinator` — unchanged. Design Mode
  produces a `TypeGraph` and feeds it through the same pipeline.
- The export pipeline — unchanged. Operates on `TypeGraph`.

## What DOES change (input handlers)

- `GraphCanvas` pointer handlers (`OnPointerPressed`, `OnPointerMoved`,
  `OnPointerReleased`) — extended with mode-aware branches. The canvas
  remains the single owner of pointer events; the mode toggle changes what
  those handlers *do*, not who owns them. See doc 04 for the hit-test
  routing.
- `CanvasRenderer` — extended with new draw methods for Design Mode
  affordances (selection ring, resize handle, edge ports, edge creation
  preview, lasso rectangle). The existing `DrawNodes` is unchanged and
  reused for both modes.
- `MainWindow.axaml` — mode toggle, conditional toolbars.
- `MainWindow.axaml.cs` — mode switch handlers, wire up
  `DesignCanvasController` and `DesignHitTestService`.

## What DOES change (new files)

- New file: `Design/DesignModeController.cs` — mode state, undo/redo
- New file: `Design/DesignGraph.cs` — editable graph model
- New file: `Design/DesignCanvasController.cs` — input handlers for add/edit
- New file: `Design/DesignHitTestService.cs` — sub-region hit-testing
  (header, body, resize handle, edge ports)
- New file: `Design/DesignSerialization.cs` — JSON save/load
- New file: `Design/DesignExporter.cs` — C# stub source generation
- New file: `Design/DesignValidator.cs` — validation (duplicate names,
  dangling edges, inheritance cycles)
- Modified file: `MainWindow.axaml` — mode toggle, conditional toolbars
- Modified file: `MainWindow.axaml.cs` — mode switch handlers, wire up
  DesignCanvasController
- Modified file: `GraphCanvas.cs` — pointer handlers get mode-aware branches
- Modified file: `CanvasRenderer.cs` — new draw methods for Design Mode
  affordances (selection ring, resize handle, edge ports, etc.)
- Modified file: `ProjectSettings.cs` — add `LastMode`, `RecentDesignFiles`

## Risks

- **Undo/redo across mode switches**: should undo in Design Mode while a
  scanned graph is loaded in Analyze Mode undo the scan? Decision: no —
  undo/redo is scoped to Design Mode only. Analyze Mode has no undo (it's
  read-only).
- **Layout engine choice in Design Mode**: when you add a class, should the
  layout re-run immediately (jarring) or be deferred until you click "Apply
  Layout"? Decision: deferred. The user draws freely; layout runs on demand
  (button or Ctrl+L).
- **Concurrent edits**: not a concern in single-user mode, but the data
  model must be safe against accidental re-entrancy (e.g. an undo firing
  while a drag is in progress).
