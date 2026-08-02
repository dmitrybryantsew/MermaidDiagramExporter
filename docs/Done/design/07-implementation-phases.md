# 07 — Implementation Phases

## The principle

Design Mode is a **large feature**. To ship it incrementally and keep the
codebase working at every commit, we break it into seven phases (M0–M6).
Each phase:
- Is **independently shippable** — the app builds, tests pass, and the
  feature is usable in some form after each phase.
- Adds **user-visible value** — no phase is purely refactoring.
- Has **clear acceptance criteria** — what tests/checks must pass to call
  it done.
- Builds on the **previous phase** — no big-bang integration at the end.

## Phase overview

| Phase | Name | Duration estimate | User-visible outcome |
|-------|------|-------------------|----------------------|
| M0 | Scaffold | 1 day | Mode toggle exists, switching does nothing yet |
| M1 | Data model + persistence | 1–2 days | Can save/load empty `.dgraph.json` files |
| M2 | Class rectangle interaction | 2–3 days | Can add/move/delete classes on canvas |
| M3 | Member editing | 1–2 days | Can add/edit/delete members within classes |
| M4 | Edge creation | 1–2 days | Can connect classes with association/inheritance/implements edges |
| M5 | Export pipeline | 1–2 days | Can export to Mermaid, JSON, C# stub |
| M6 | Polish + undo/redo | 2–3 days | Undo/redo, keyboard shortcuts, context menus, validation |

Total: ~10–15 days of focused work.

---

## M0 — Scaffold

**Goal**: Establish the mode toggle infrastructure without any actual Design
Mode functionality. The toggle exists, switching modes works (rearranges the
toolbar), but Design Mode shows a placeholder.

### Tasks

1. Create `Design/DesignModeController.cs` — owns `AppMode` enum, `CurrentMode`
   property, `ModeChanged` event, and a placeholder `DesignGraph` field (empty
   for now).
3. Modify `MainWindow.axaml` — add mode toggle (segmented control) at the top
   of the sidebar. Use `IsVisible` bindings to switch between Analyze and
   Design toolbars.
4. Modify `MainWindow.axaml.cs` — wire up the toggle's `SelectionChanged` event
   to call `_designModeController.EnterDesignMode()` / `EnterAnalyzeMode()`.
5. Add a placeholder Design Mode toolbar with a single "Coming soon" label.

### Acceptance criteria

- [ ] App builds, all 114 existing tests pass.
- [ ] Mode toggle is visible in the sidebar.
- [ ] Clicking the toggle switches between Analyze and Design toolbars.
- [ ] Design Mode shows the placeholder.
- [ ] No regression in Analyze Mode behavior.

### New files

- `src/MermaidDiagramExporter.Gui/Design/DesignModeController.cs`

### Modified files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`

---

## M1 — Data model + persistence

**Goal**: The `DesignGraph` data model exists, can be saved to JSON, and can
be loaded back. No UI yet — just the model and serialization.

### Tasks

1. Create `Design/DesignGraph.cs` — the full data model (per doc 05).
2. Create `Design/DesignSerialization.cs` — JSON save/load with `System.Text.Json`.
3. Create `Design/DesignValidator.cs` — basic validation (duplicate names,
   dangling edges).
4. Write unit tests for save/load round-trip:
   - Empty graph → save → load → identical
   - Graph with classes/edges → save → load → identical
   - Invalid version → throws
5. Write unit tests for validation:
   - Duplicate class names → error
   - Edge with missing endpoint → error
   - Self-edge on inheritance → error

### Acceptance criteria

- [ ] App builds, all M0 + new tests pass.
- [ ] `DesignSerialization.Save` + `Load` round-trips identically.
- [ ] `DesignValidator` catches all documented error cases.
- [ ] JSON files are valid (parse with `JsonDocument.Parse`).

### New files

- `src/MermaidDiagramExporter.Gui/Design/DesignGraph.cs`
- `src/MermaidDiagramExporter.Gui/Design/DesignSerialization.cs`
- `src/MermaidDiagramExporter.Gui/Design/DesignValidator.cs`
- `tests/MermaidDiagramExporter.Tests/DesignSerializationTests.cs`
- `tests/MermaidDiagramExporter.Tests/DesignValidatorTests.cs`

---

## M2 — Class rectangle interaction

**Goal**: In Design Mode, you can click on empty canvas to add a class, drag
classes around, and delete them. No member editing yet, no edges.

### Tasks

1. Create `Design/DesignCanvasController.cs` — handles pointer events on the
   canvas in Design Mode.
2. Create `Design/DesignClassRectangle.cs` — the interaction wrapper (per doc 04).
3. Modify `MainWindow.axaml.cs` — wire `DesignCanvasController` to the canvas
   in Design Mode.
4. Implement click-to-add: click on empty canvas → new class with default name.
5. Implement drag-to-move: drag class header → position updates.
6. Implement delete: select + Delete key → remove class.
7. Implement inline name edit: double-click header → TextBox overlay.
8. Implement selection: click to select, Shift+click to multi-select.
9. Write unit tests for `DesignCanvasController`:
   - Click on empty canvas adds a class
   - Drag updates position
   - Delete removes the class

### Acceptance criteria

- [ ] App builds, all M1 + new tests pass.
- [ ] In Design Mode, click on empty canvas → new class appears.
- [ ] Drag a class → it moves.
- [ ] Delete key removes selected class.
- [ ] Double-click header → inline name edit works.
- [ ] Manual positions persist immediately (no "Apply Layout" needed).

### New files

- `src/MermaidDiagramExporter.Gui/Design/DesignCanvasController.cs`
- `src/MermaidDiagramExporter.Gui/Design/DesignClassRectangle.cs`

### Modified files

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`
- `src/MermaidDiagramExporter.Gui/GraphCanvas.cs` — pointer handlers get
  mode-aware branches (click-to-add, drag-to-move, delete-key handling)
- `src/MermaidDiagramExporter.Gui/CanvasRenderer.cs` — new draw methods for
  selection ring, resize handle, edge ports (if not deferred to M3/M4)

---

## M3 — Member editing

**Goal**: Within a class, you can add/edit/delete members (fields,
properties, methods).

### Tasks

1. Add member row rendering to `DesignClassRectangle` (or extend
   `CanvasRenderer.DrawNodes` to handle Design Mode member display).
2. Implement "+" button at bottom of class → popup with Field/Property/Method
   choices.
3. Implement inline edit on member name and type (double-click).
4. Implement member delete (× button on row).
5. Implement member reorder (drag up/down).
6. Implement visibility change (click on +/−/−/#/~ prefix to cycle).
7. Write unit tests for member operations.

### Acceptance criteria

- [ ] App builds, all M2 + new tests pass.
- [ ] Can add Field, Property, Method to a class.
- [ ] Can rename member inline.
- [ ] Can change member type inline.
- [ ] Can delete member.
- [ ] Can reorder members.

### Modified files

- `src/MermaidDiagramExporter.Gui/Design/DesignClassRectangle.cs`
- `src/MermaidDiagramExporter.Gui/CanvasRenderer.cs` (extend for member rows)

---

## M4 — Edge creation

**Goal**: Connect classes with association/inheritance/implements edges.

### Tasks

1. Add edge port rendering to `DesignClassRectangle` (small circles on left/
   right edges, visible on hover).
2. Implement drag-from-port-to-port for edge creation.
3. Add edge type selector popup (Association/Inheritance/Implements).
4. Implement edge selection and delete.
5. Implement edge type change (right-click → Change Type).
6. Write unit tests for edge operations.

### Acceptance criteria

- [ ] App builds, all M3 + new tests pass.
- [ ] Drag from one class's port to another → edge appears.
- [ ] Edge type selector shows three options.
- [ ] Can delete selected edge.
- [ ] Can change edge type after creation.

### Modified files

- `src/MermaidDiagramExporter.Gui/Design/DesignClassRectangle.cs`
- `src/MermaidDiagramExporter.Gui/Design/DesignCanvasController.cs`

---

## M5 — Export pipeline

**Goal**: Export the `DesignGraph` to Mermaid, JSON, and C# stub source.

### Tasks

1. Create `Design/DesignExporter.cs` — main export class.
2. Implement `ToMermaid` — delegates to `MermaidGraphExporter` via
   `DesignGraph → TypeGraph` conversion.
3. Implement `ToCSharpStub` — generates minimal C# class declarations.
4. Implement `DesignGraph.ToTypeGraph` conversion.
5. Implement `DesignValidator` integration in export (warn on errors).
6. Add "Export C# Stub..." and "Export JSON..." buttons to Design Mode toolbar.
7. Write unit tests for all export formats.

### Acceptance criteria

- [ ] App builds, all M4 + new tests pass.
- [ ] Export to Mermaid produces valid output that renders on mermaid.ai.
- [ ] Export to JSON round-trips identically.
- [ ] Export to C# stub compiles in a fresh .NET project (validated by
   Roslyn parse test).
- [ ] Validation errors surface in a dialog before export.

### New files

- `src/MermaidDiagramExporter.Gui/Design/DesignExporter.cs`
- `tests/MermaidDiagramExporter.Tests/DesignExporterTests.cs`

---

## M6 — Polish + undo/redo

**Goal**: Production-quality UX. Undo/redo, keyboard shortcuts, context menus,
validation warnings, auto-save.

### Tasks

1. Implement undo/redo stack in `DesignModeController`.
2. Implement all keyboard shortcuts (Ctrl+Z, Ctrl+Y, Delete, Ctrl+A, Ctrl+C,
   Ctrl+V, Ctrl+D, Ctrl+L, Ctrl+S, arrow keys).
3. Implement right-click context menus for class/edge/member.
4. Implement auto-save (every 30 seconds to temp file).
5. Implement recovery on startup if autosave exists.
6. Implement recent files list in File menu.
7. Write integration tests for full workflows (add class → add member → add
   edge → export → undo → redo).

### Acceptance criteria

- [ ] App builds, all M5 + new tests pass.
- [ ] Ctrl+Z undoes any operation (add, delete, move, rename, connect).
- [ ] Ctrl+Y / Ctrl+Shift+Z redoes.
- [ ] All keyboard shortcuts work.
- [ ] Right-click context menus appear with correct options.
- [ ] Auto-save creates a file every 30 seconds if dirty.
- [ ] On startup, if autosave exists, offer to recover.
- [ ] Recent files list shows last 10 opened files.

### Modified files

- `src/MermaidDiagramExporter.Gui/Design/DesignModeController.cs` (extend)
- `src/MermaidDiagramExporter.Gui/Design/DesignCanvasController.cs` (extend)
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` (add shortcuts)

---

## Risk: phases taking longer than estimated

If a phase takes longer than estimated:
1. **Don't skip acceptance criteria** — they're the definition of done.
2. **Don't merge phases** — each phase is independently shippable.
3. **Cut scope, not quality** — e.g. in M3, if member reorder is hard,
   ship without it and add it later.
4. **Ship the partial feature** — M2 without M3 is still useful (you can
   draw classes, just can't add members yet).

## Risk: existing tests break

If a phase breaks existing Analyze Mode tests:
1. **Stop and investigate** — don't push a broken state.
2. The architecture (shared canvas, mode toggle) is designed to keep
   Analyze Mode unaffected. If tests break, the architecture has a leak.
3. Fix the leak before continuing.

## Risk: scope creep

If a phase reveals new requirements:
1. **Document them** — add to `08-risks-and-decisions.md`.
2. **Defer to a future plan** — don't expand the current phase.
3. **Ship the planned scope first** — polish the foundation before adding
   features.
