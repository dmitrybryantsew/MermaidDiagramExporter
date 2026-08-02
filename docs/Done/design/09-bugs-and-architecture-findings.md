# 09 — Bugs and Architecture Findings (Design Mode)

> **Status**: Written against the actual shipped code on `feature/design-mode`
> after context menus were added (commit `2d04e65`). All references verified
> against the source.

This document captures real defects found by reading the shipped code, not
the plan. The plan and the shipped code have **drifted** in several places.
Each bug below is reproducible from the code as it stands today and ties
to a specific decision already documented in `docs/design/05`,
`docs/design/07-ui-integration-plan.md`, or `docs/design/08`.

The companion doc `10-inspector-panel-and-relation-ux.md` covers the
related UI redesign.

---

## Summary table

| ID | Severity | Title | Status |
|----|----------|-------|--------|
| BUG-1 | **Critical** | Layout overrides DesignClass.X/Y on every mutation | Unfixed |
| BUG-2 | High | "Add Class Here" bypasses undo + auto-save-dirty | Unfixed |
| BUG-3 | Medium | `ShowAt(this)` doesn't anchor to cursor | Unfixed |
| GAP-1 | High | No inspector panel; `DesignCanvasController.SelectionChanged` has no subscriber | Unfixed |
| GAP-2 | Medium | `Select()` always clears-and-replaces; multi-select is silently broken | Unfixed |

**Recommended fix order**: BUG-1 → BUG-3 → BUG-2 → GAP-1 → GAP-2.
BUG-1 and BUG-3 are entangled (both contribute to the "spawns at a fixed
spot" perception). BUG-2 must be fixed before GAP-1 because the inspector
panel will replicate the same anti-pattern if we build it now.

---

## BUG-1 (critical) — Layout overrides DesignClass.X/Y on every mutation

### Symptom

User reports: "spawns at the bottom." New classes added by clicking the
canvas appear at a fixed location (the bottom of the canvas) regardless
of where the user clicked. Dragging fights the layout engine: the moment
the user releases the mouse, the class snaps back to where the layout
algorithm put it.

### Root cause

`MainWindow.axaml.cs:122-132` — `RenderDesignModeGraph()`:

```csharp
private void RenderDesignModeGraph()
{
    if (_designGraph == null) return;

    var typeGraph = DesignExporter.ToTypeGraph(_designGraph);
    GraphCanvasView.SetDesignGraph(_designGraph);
    var (nodes, edges) = _layoutEngine.Layout(typeGraph);  // ← HERE
    GraphCanvasView.SetGraph(nodes, edges);
    MinimapView.SetGraph(nodes, edges);
    StatsText.Text = $"Design: {_designGraph.Classes.Count} classes, {_designGraph.Edges.Count} edges";
}
```

Every mutation calls this. `_layoutEngine.Layout(typeGraph)` runs the
cluster-as-supernode layered layout, which **overwrites `DesignClass.X/Y`**
because `LayoutEngine.Layout` writes to `LayoutResult.NodeBounds` and the
resulting `GraphNode.X/Y/Width/Height` come from that.

The same path is hit by `OnDesignGraphMutated` (line 138-142) which fires
on every `DesignCanvasController` mutation. So:

- Adding a class → layout dumps it wherever the algorithm chooses
- Dragging a class → drag updates `DesignClass.X/Y`, then `GraphMutated`
  fires, then `RenderDesignModeGraph` runs the layout again, overwriting
  the drag with the layout's position
- Resizing a class → same problem

### Why the planning docs already predicted this

- `docs/design/05-data-model-and-persistence.md` §"Why store X/Y/Width/Height on classes?"
  explicitly says: *"The user can manually position classes. These positions
  are part of the design intent. The layout engine can override them when
  the user clicks 'Apply Layout', but Design Mode respects manual positions."*
- `docs/design/08-risks-and-decisions.md` D6: *"Design Mode positions are
  authoritative — the layout engine does not run unless the user
  explicitly clicks 'Apply Layout'."*
- `docs/design/07-ui-integration-plan.md` W1 Risks: *"Coordinate mismatch:
  Design Mode uses `DesignClass.X/Y` as authoritative. The layout engine
  writes to `LayoutResult.NodeBounds`. When switching modes, we need to
  read from `DesignClass.X/Y` and feed those positions to the layout
  engine so it doesn't re-layout from scratch. Solution: skip the layout
  engine in Design Mode and build `LayoutResult` directly from
  `DesignClass.X/Y/Width/Height`."*

The planning docs predicted this three times. The shipped code did the
opposite of all three.

### Fix

`RenderDesignModeGraph` must build `LayoutResult` directly from
`DesignClass.X/Y/Width/Height` in Design Mode. The layout engine runs
**only** when the user explicitly clicks "Apply Layout" (not yet
implemented — see doc 10).

Sketch:

```csharp
private void RenderDesignModeGraph()
{
    if (_designGraph == null) return;
    GraphCanvasView.SetDesignGraph(_designGraph);

    // Build LayoutResult directly from DesignClass positions — no layout engine
    var layoutResult = BuildLayoutResultFromDesignGraph(_designGraph);
    var (nodes, edges) = ConvertLayoutResultToGraphNodes(layoutResult);
    GraphCanvasView.SetGraph(nodes, edges);
    MinimapView.SetGraph(nodes, edges);
    StatsText.Text = $"Design: {_designGraph.Classes.Count} classes, {_designGraph.Edges.Count} edges";
}
```

`BuildLayoutResultFromDesignGraph` sets `NodeBounds[cls.Id] = new Rect(cls.X, cls.Y, cls.Width, cls.Height)` for every class, and `ClusterBounds` from the namespace groups. The existing `LayoutEngine.Layout` already does this kind of thing for Analyze Mode — Design Mode just needs a stripped-down version that skips the ranking/ordering.

### Acceptance criteria

- [ ] Clicking anywhere on the canvas creates a class at that position (within ±5px)
- [ ] Dragging a class leaves it where the user released the mouse
- [ ] Resizing a class leaves it at the new size
- [ ] Adding multiple classes puts them exactly where each click happened
- [ ] "Apply Layout" button (when implemented) still works for users who want auto-arrangement

---

## BUG-2 (high) — "Add Class Here" bypasses undo + auto-save-dirty

### Symptom

Right-clicking empty canvas and choosing "Add Class Here" creates a class
that **cannot be undone with Ctrl+Z**. Auto-save also doesn't fire
because the dirty flag isn't set.

### Root cause

`MainWindow.axaml.cs:325-337` — the right-click "Add Class Here" handler:

```csharp
var addItem = new Avalonia.Controls.MenuItem { Header = "Add Class Here" };
addItem.Click += (_, _) =>
{
    _designGraph.Classes.Add(new DesignClass    // ← direct mutation
    {
        Name = "NewClass",
        X = target.WorldPosition.X - 100f,
        Y = target.WorldPosition.Y - 30f,
        Width = 200f,
        Height = 60f
    });
    RenderDesignModeGraph();    // ← no dirty flag, no undo command
};
```

This bypasses:
1. **`DesignUndoManager`** — no `DesignCommands.AddClass` is created/pushed
2. **`OnDesignGraphMutated`** — never fires, so `_designIsDirty` stays false
3. **`TryAutoSave`** — never fires

### Why this matters

The user expects every mutation to be undoable. If they accidentally add
a class via right-click, Ctrl+Z does nothing — they have to manually
delete it. This silently breaks the undo system that M6 spent significant
effort building.

### Fix

Route through the same path as the canvas's click-to-add:

```csharp
addItem.Click += (_, _) =>
{
    if (_designGraph == null) return;
    var newClass = new DesignClass
    {
        Name = "NewClass",
        X = target.WorldPosition.X - 100f,
        Y = target.WorldPosition.Y - 30f,
        Width = 200f,
        Height = 60f
    };
    var cmd = new DesignCommands.AddClass(newClass);
    _designCanvasController.ExecuteCommand(cmd, _designGraph);
    // ExecuteCommand fires GraphMutated → OnDesignGraphMutated → dirty flag + auto-save
};
```

### Audit note

Every existing mutation path should be audited for this pattern. The
canvas's `HandlePointerPressed` → `AddClassAt` already goes through the
undo system (it uses `_designGraph.Classes.Add` directly but then fires
`GraphMutated`, which sets the dirty flag — though it does NOT push an
undo command). So even the canvas path has a partial bug: dirty flag
works, but undo doesn't.

The proper fix is to make every mutation go through `ExecuteCommand`
which handles all three (mutation, undo push, dirty flag, auto-save).

### Acceptance criteria

- [ ] Right-click "Add Class Here" → Ctrl+Z removes the class
- [ ] Right-click "Add Class Here" → auto-save fires within 30s
- [ ] Canvas click-to-add → Ctrl+Z removes the class (currently broken)
- [ ] All other mutations already working (add member, rename, etc.) still undo correctly

---

## BUG-3 (medium) — Context menu doesn't anchor to cursor

### Symptom

User reports: "always happens at a fixed spot." The context menu appears
in a consistent location regardless of where the user right-clicked.

### Root cause

`MainWindow.axaml.cs:342-343` — the menu show call:

```csharp
if (menu.Items.Count > 0)
    menu.ShowAt(this);    // ← anchors to MainWindow, not cursor
}
```

`MenuFlyout.ShowAt(control)` anchors the menu to the **control's
position**, not to the cursor. The menu appears at the same offset
relative to the MainWindow every time.

### Fix

Avalonia's `MenuFlyout` doesn't have a built-in "show at cursor"
method, but you can use a `Popup` with explicit placement:

```csharp
var popup = new Popup
{
    Placement = PlacementMode.Pointer,
    PlacementAnchor = PopupAnchor.TopLeft,
    Child = BuildMenuContent(menu)
};
popup.IsOpen = true;
```

Or, simpler: convert the `MenuFlyout` to a `ContextMenu` (which IS
cursor-anchored by default) and assign it to the canvas:

```csharp
var contextMenu = new ContextMenu();
foreach (var item in menu.Items) contextMenu.Items.Add(item);
GraphCanvasView.ContextMenu = contextMenu;
contextMenu.Open();
```

The second approach is cleaner but requires moving the menu construction
out of the event handler so it can be assigned to the canvas at startup.

### Acceptance criteria

- [ ] Right-clicking in different parts of the canvas shows the menu near the cursor
- [ ] Menu position tracks the cursor within ±10px

---

## GAP-1 (high) — No inspector panel; SelectionChanged has no subscriber

### Symptom

User reports: "no fields to connect." After clicking a class, there's
nowhere to see its members, change its kind, edit its namespace, or
manage its edges. The right-side inspector panel area exists in the
window layout but is empty.

### Root cause

`MainWindow.axaml.cs:72` subscribes to `GraphCanvasView.SelectionChanged`
(Analyze Mode canvas selection). But `DesignCanvasController.SelectionChanged`
has **no subscriber**:

```bash
$ grep -n "DesignCanvasController.SelectionChanged\|_designCanvasController.SelectionChanged" \
    src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs
(no matches)
```

The selection event fires every time the user clicks a class in Design
Mode, but nothing listens. The inspector panel (column 4 in MainWindow.axaml)
shows the static "Inspector" placeholder text and nothing else.

### Why this is more than a missing handler

The inspector panel is where Design Mode becomes a real authoring tool.
Without it:
- Can't see member list for the selected class
- Can't change ClassKind (Class/Interface/Enum/Struct/Static/Abstract)
- Can't change Namespace after creation
- Can't see Outgoing/Incoming relations
- Can't change edge kind after creation
- Can't bulk-edit multi-selection (also blocked by GAP-2)

This is the **direct cause** of "no fields to connect" — the user
expects to be able to add fields via the inspector, not just via the
context menu.

### Fix

See `10-inspector-panel-and-relation-ux.md` for the full redesign. The
fix has four parts:
1. Subscribe to `_designCanvasController.SelectionChanged` in MainWindow
2. Add a selection-driven inspector panel in the right column
3. Route all inspector edits through `ExecuteCommand` (not direct mutation)
4. Add the missing ClassKind/Namespace change operations to
   `DesignCanvasController` (the data model already supports them)

### Acceptance criteria

- [ ] Clicking a class shows its properties in the inspector
- [ ] Inspector edits go through the undo system (Ctrl+Z reverts)
- [ ] Inspector edits trigger auto-save
- [ ] No regression: clicking empty canvas still shows the summary state

---

## GAP-2 (medium) — `Select()` always clears-and-replaces; multi-select is silently broken

### Symptom

The data model uses `HashSet<string> _selectedClassIds` which clearly
supports multiple selected classes. But every code path that calls
`Select()` first does `_selectedClassIds.Clear()`, so multi-select is
impossible in practice.

### Root cause

`DesignCanvasController.cs:539-544`:

```csharp
private void Select(ClassRectangle rect)
{
    _selectedClassIds.Clear();      // ← always clears
    _selectedClassIds.Add(rect.ClassId);
    UpdateSelection();
}
```

There's no code path that adds to the selection without clearing first.
Shift+click (which would normally extend selection) is not handled.

### Why this matters before GAP-1

If we build the inspector panel for multi-select before fixing this,
the panel will show "Multi-selection" state but the actions will only
operate on the last-clicked class. That's worse than not having
multi-select at all.

### Fix

Add shift/ctrl-click handling to the pointer routing:

```csharp
private void Select(ClassRectangle rect, bool extendSelection = false)
{
    if (!extendSelection)
        _selectedClassIds.Clear();
    
    if (_selectedClassIds.Contains(rect.ClassId))
        _selectedClassIds.Remove(rect.ClassId); // toggle off
    else
        _selectedClassIds.Add(rect.ClassId);
    
    UpdateSelection();
}
```

Pass `extendSelection = (e.KeyModifiers & KeyModifiers.Shift) != 0`
from the pointer handler.

### Acceptance criteria

- [ ] Shift+click adds to selection
- [ ] Shift+click on already-selected class removes from selection
- [ ] Click without modifiers clears selection and selects the clicked class
- [ ] Inspector panel correctly handles multi-selection when this lands

---

## Recommended fix order

1. **BUG-1** — biggest user-visible symptom, blocks all other testing
2. **BUG-3** — small change, fixes the "fixed spot" perception that compounds BUG-1
3. **BUG-2** — must be fixed before any new mutation paths are added (the inspector will replicate this)
4. **GAP-1** — inspector panel redesign (see doc 10)
5. **GAP-2** — multi-select, after the inspector supports single-select properly

BUG-1 → BUG-3 → BUG-2 → GAP-1 → GAP-2 is the order that minimizes
rework. BUG-1 and BUG-3 are entangled because they both contribute to
"things appear in the wrong place." BUG-2 is a quick fix but must
precede GAP-1 because the inspector will add many new mutation paths
that need to go through the undo system correctly.

---

## Audit checklist (preventive)

After fixing these, audit every mutation path in `MainWindow.axaml.cs`
and `DesignCanvasController.cs` for:

- [ ] Goes through `ExecuteCommand` (not direct mutation)
- [ ] Fires `GraphMutated` (which sets dirty flag + triggers auto-save)
- [ ] Has a corresponding `DesignCommands.*` undo class
- [ ] Is documented in `docs/design/07-implementation-phases.md`

This audit should become a PR checklist for any future Design Mode work.
