# 07 — Design Mode UI Integration Plan

## The gap

**Engine complete (M0–M6)**: 268 tests passing, all core logic implemented:
- `DesignModeController` — mode toggle state
- `DesignGraph` + `DesignSerialization` + `DesignValidator` — data model
- `DesignCanvasController` — add/select/delete/move/resize/edit members/edges
- `DesignExporter` — ToMermaid / ToCSharpStub / ToTypeGraph
- `DesignUndoManager` + 12 concrete commands — undo/redo
- `DesignRecentFiles` — MRU list

**UI not wired**: The Design Mode placeholder still shows "Coming soon". The engine runs but nothing is connected to it. The user sees an empty page.

## What needs to happen

Three layers of integration:

1. **Canvas rendering** — `GraphCanvas` must render Design Mode classes (via `CanvasRenderer`) and respond to Design Mode pointer events (already wired in M2/M4, but needs `DesignGraph` → `GraphNode`/`GraphEdge` conversion to feed the existing renderer).
2. **Sidebar toolbar** — Replace the "Coming soon" placeholder with real Design Mode controls (Add Class, Add Edge, Undo, Redo, Save, Load, Export).
3. **Keyboard shortcuts** — Ctrl+Z, Ctrl+Y, Delete, Ctrl+A, Ctrl+C, Ctrl+V, Ctrl+D, Ctrl+L, Ctrl+S, Escape (cancel edge creation).

## Phases

| Phase | Name | Scope |
|-------|------|-------|
| W1 | Canvas rendering | Feed Design Mode graph into existing canvas renderer |
| W2 | Sidebar toolbar | Replace placeholder with real Design Mode controls |
| W3 | Keyboard shortcuts | Wire Ctrl+Z/Y, Delete, Escape, Ctrl+L |
| W4 | File operations | New/Open/Save/Save As buttons + file pickers |
| W5 | Export buttons | Export Mermaid / C# Stub / JSON buttons |
| W6 | Polish | Context menus, auto-save, recent files menu |

Each phase is independently shippable. Total estimate: 4–6 days of focused work.

---

## W1 — Canvas rendering (the biggest gap)

**Problem**: `GraphCanvas.RenderNow()` calls `CanvasRenderer.DrawNodes(canvas, nodes, ...)` where `nodes` is `List<GraphNode>`. Design Mode has `List<DesignClass>` — these are different types with different fields.

**Solution**: Convert `DesignGraph` → `LayoutResult` (via the existing `DesignExporter.ToTypeGraph` + `GraphLayoutCoordinator.CreateLayout`) and feed that into the canvas. The canvas doesn't need to know it's Design Mode — it just renders `GraphNode`s like always.

### Tasks

1. In `MainWindow.OnDesignModeClick`, after switching mode:
   - Create a `DesignCanvasController` if not already created
   - Convert `DesignGraph` → `LayoutResult` via `DesignExporter.ToTypeGraph` + `GraphLayoutCoordinator.CreateLayout`
   - Call `GraphCanvas.SetGraph(nodes, edges)` with the converted data
2. Subscribe to `DesignCanvasController.GraphMutated` — when it fires, re-convert and re-set the graph
3. Subscribe to `DesignCanvasController.SelectionChanged` — update the inspector panel
4. On mode switch back to Analyze, restore the Analyze Mode graph

### Acceptance criteria

- [ ] Switching to Design Mode shows the classes from the current design
- [ ] Clicking empty canvas adds a class (visible immediately)
- [ ] Dragging a class moves it (visible immediately)
- [ ] Deleting a class removes it (visible immediately)
- [ ] Switching back to Analyze Mode restores the scanned graph
- [ ] No regression in Analyze Mode rendering

### Key files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` — mode switch handler
- `src/MermaidDiagramExporter.Gui/GraphCanvas.cs` — already has `SetGraph(List<GraphNode>, List<GraphEdge>)`

### Risks

- **Coordinate mismatch**: Design Mode uses `DesignClass.X/Y` as authoritative (per doc 05). The layout engine writes to `LayoutResult.NodeBounds`. When switching modes, we need to read from `DesignClass.X/Y` and feed those positions to the layout engine so it doesn't re-layout from scratch. Solution: skip the layout engine in Design Mode and build `LayoutResult` directly from `DesignClass.X/Y/Width/Height`.
- **Performance**: re-converting on every mutation could be slow for large designs. Solution: debounce or batch.

---

## W2 — Sidebar toolbar

**Problem**: The Design Mode panel shows "Coming soon" placeholder text.

**Solution**: Replace with real controls matching the Analyze Mode toolbar pattern.

### Tasks

1. Replace `DesignModePanel` content in `MainWindow.axaml` with:
   - **New** button — creates a fresh empty design
   - **Open...** button — file picker for `.dgraph.json`
   - **Save** / **Save As...** buttons
   - **Undo** / **Redo** buttons (disabled when stacks empty)
   - **Add Class** button — adds a class at canvas center
   - **Add Edge** button — toggles edge-creation mode
   - **Export Mermaid** / **Export C# Stub** / **Export JSON** buttons
   - **Apply Layout** button — runs the layout engine on the design
2. Wire all buttons in `MainWindow.axaml.cs`
3. Add a "Design tools" section header

### Acceptance criteria

- [ ] All buttons present and clickable
- [ ] Undo/Redo enable/disable based on stack state
- [ ] Add Class adds a class at canvas center
- [ ] Export buttons produce files in the chosen location

### Key files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml` — toolbar XAML
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` — button handlers

---

## W3 — Keyboard shortcuts

**Problem**: No keyboard support for Design Mode actions.

**Solution**: Add `KeyBindings` or override `OnKeyDown` in `MainWindow`.

### Tasks

1. Ctrl+Z / Ctrl+Y — undo/redo (via `DesignCanvasController.Undo`/`Redo`)
2. Delete / Backspace — delete selected classes (via `HandleDeleteKey`)
3. Escape — cancel edge creation (via `CancelEdgeCreation`)
4. Ctrl+L — apply layout (run layout engine on current design)
5. Ctrl+S — save (if current file known, else Save As)
6. Ctrl+A — select all
7. Ctrl+C / Ctrl+V — copy/paste (deferred — not in W3 scope, can be added later)
8. Disable shortcuts during inline edit (so typing isn't intercepted)

### Acceptance criteria

- [ ] Ctrl+Z undoes the last mutation
- [ ] Ctrl+Y redoes
- [ ] Delete removes selected class
- [ ] Escape cancels in-progress edge creation
- [ ] Ctrl+L re-runs layout
- [ ] Ctrl+S saves
- [ ] Shortcuts don't fire during inline edit

### Key files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` — `OnKeyDown` override

---

## W4 — File operations

**Problem**: No way to save or load designs from disk.

**Solution**: File pickers using Avalonia's `StorageProvider` (already used in Analyze Mode for folder picking).

### Tasks

1. **New** — clear current design, start blank
2. **Open...** — file picker, load `.dgraph.json` via `DesignSerialization.Load`
3. **Save** — save to current file path (or prompt if untitled)
4. **Save As...** — file picker, save with new name
5. Track current file path in `DesignModeController.CurrentFilePath`
6. Add to `DesignRecentFiles` on save/open
7. Prompt to save on mode switch if dirty

### Acceptance criteria

- [ ] New clears the design
- [ ] Open loads a `.dgraph.json` file
- [ ] Save writes to the current file
- [ ] Save As prompts for a new file name
- [ ] Recent files list updates on save/open
- [ ] Unsaved changes prompt on mode switch

### Key files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` — file operation handlers
- `src/MermaidDiagramExporter.Gui/Design/DesignSerialization.cs` — already implemented in M1

---

## W5 — Export buttons

**Problem**: No way to export designs to Mermaid, C#, or JSON.

**Solution**: Buttons in Design Mode toolbar that call `DesignExporter` methods and write to files.

### Tasks

1. **Export Mermaid** — save `.mmd` file
2. **Export C# Stub** — save `.cs` file
3. **Export JSON** — save `.dgraph.json` file (same as Save As, but always prompts)
4. Each button uses Avalonia's `StorageProvider.SaveFilePickerAsync`
5. Show success message in status bar

### Acceptance criteria

- [ ] Export Mermaid produces valid `.mmd` that renders on mermaid.ai
- [ ] Export C# Stub produces compilable `.cs`
- [ ] Export JSON round-trips identically
- [ ] Success message shown after export

### Key files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` — export button handlers
- `src/MermaidDiagramExporter.Gui/Design/DesignExporter.cs` — already implemented in M5

---

## W6 — Polish

**Problem**: No context menus, no auto-save, no recent files in UI.

**Solution**: Add the remaining UX features per doc 07 M6 acceptance criteria.

### Tasks

1. **Right-click context menus** on canvas:
   - Class: Rename, Delete, Duplicate, Add Member → (Field/Property/Method/Constructor/Event)
   - Edge: Change Type, Delete
   - Member: Rename, Change Type/Visibility, Delete, Move Up/Down
   - Empty canvas: Add Class, Paste
2. **Auto-save** every 30 seconds if dirty, to `%TEMP%/.mermaid-diagram-exporter/autosave-{guid}.dgraph.json`
3. **Recovery on startup** if autosave exists
4. **Recent files menu** in File menu (max 10)
5. **Inline edit TextBox** overlay for class/member names (the one real Avalonia Control)

### Acceptance criteria

- [ ] Right-click context menus work for class/edge/member/canvas
- [ ] Auto-save creates a file every 30 seconds if dirty
- [ ] On startup, if autosave exists, offer to recover
- [ ] Recent files menu shows last 10 opened files

### Key files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` — context menus, auto-save
- `src/MermaidDiagramExporter.Gui/Design/DesignRecentFiles.cs` — already implemented in M6

---

## Recommendation

**Start with W1** — it's the biggest gap (canvas doesn't render anything). Without W1, the other phases are invisible to the user.

**W2 + W4 together** — sidebar toolbar and file operations are tightly coupled (the toolbar needs the file operations to be useful).

**W3 + W5** — keyboard shortcuts and export buttons are independent and can be done in any order.

**W6 last** — polish is incremental and can ship as separate small commits.

## Estimated timeline

| Phase | Days |
|-------|------|
| W1 | 1–2 |
| W2 | 1 |
| W3 | 0.5 |
| W4 | 1 |
| W5 | 0.5 |
| W6 | 1–2 |
| **Total** | **5–8 days** |

## Open questions

1. **Where should the Design Mode toolbar live?** Sidebar (like Analyze Mode) or bottom panel? Sidebar is consistent but adds vertical scroll. Bottom panel is more canvas-focused but breaks the existing pattern.
2. **Should Design Mode have its own minimap?** Doc 08 Q1 says no for v1. Stick with that.
3. **Should we add tabs for multiple open designs?** Doc 08 Q3 says no for v1. Stick with that.
4. **How should we handle the empty state?** When the user enters Design Mode with no design, show a "Click to add a class" hint on the canvas? Or just let them click and discover?
