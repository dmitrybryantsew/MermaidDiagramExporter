# 09 — Bugs and Architecture Findings (Post-Implementation Review)

Reviewed against `csharp_project_context.txt` (actual shipped code), not just
the planning docs. Two user-reported symptoms were traced to root cause.
Both are real bugs, not perception issues, and one of them is a violation
of a documented architectural decision.

---

## BUG-1 (Critical): Layout engine runs on every mutation, overriding manual positions

**Symptom reported**: "right click → new class spawns at the bottom, not at
click location."

**Root cause**: `RenderDesignModeGraph()` in `MainWindow.axaml.cs` does this
on *every* call:

```csharp
var typeGraph = DesignExporter.ToTypeGraph(_designGraph);
GraphCanvasView.SetDesignGraph(_designGraph);
var (nodes, edges) = _layoutEngine.Layout(typeGraph);   // <-- runs the layout engine
GraphCanvasView.SetGraph(nodes, edges);
```

It is called from `OnDesignGraphMutated`, which fires after **every**
mutation (add class, move, delete, add member, add edge...). So:

1. User right-clicks → `AddClassAt` (or the context-menu handler) correctly
   sets `X = worldPos.X - 100, Y = worldPos.Y - 30` — the click position math
   is fine.
2. `GraphMutated` fires → `RenderDesignModeGraph()` runs.
3. `RenderDesignModeGraph` feeds the graph through `_layoutEngine.Layout(...)`,
   which **recomputes every node's position from scratch** using the layered
   layout algorithm. A new, mostly-disconnected node has no edges yet, so the
   layered/ranked layout algorithm places it in whatever rank/position the
   algorithm assigns to disconnected nodes — typically the last rank, i.e.
   visually "at the bottom."
4. The class's `DesignClass.X/Y` in the data model may even still be correct,
   but the **rendered** position is whatever the layout engine just computed,
   not what's stored. The user never sees their click position.
5. The same thing happens on every drag: the user drags a class, the drag
   updates `DesignClass.X/Y`, `GraphMutated` fires, the layout engine
   immediately recomputes and may move it again. Dragging is fighting the
   renderer.

**This directly contradicts decisions already made and written down:**

- Doc 05, "Position authority: Design Mode vs. `ManualLayoutOverrides`":
  > `DesignClass.X/Y/Width/Height` are the single source of truth for
  > position... Design Mode does **not** use `ManualLayoutOverrides`.
  > [Layout] is one undo step (the entire layout operation), not per-class.

- Doc 08, D6 ("Defer layout in Design Mode"):
  > User draws freely, layout runs on demand. Avoids jarring jumps when
  > adding multiple classes in quick succession.

- Doc 08, Q4 ("Should Design Mode auto-apply layout when adding a class?"):
  > Default assumption: No. New classes are added at the click position
  > with a default size. The user clicks "Apply Layout" (or Ctrl+L)...

- Doc 07-ui-integration-plan, W1 Risks (this was *predicted* and the fix was
  already specified):
  > Solution: skip the layout engine in Design Mode and build `LayoutResult`
  > directly from `DesignClass.X/Y/Width/Height`.

So this isn't a new design problem — it's an implementation that didn't
follow the already-written spec. The fix is exactly what W1's risk note
already says to do.

**Fix**

`RenderDesignModeGraph()` must build the rendered `LayoutResult`/`(nodes,
edges)` directly from `DesignClass.X/Y/Width/Height`, *not* by calling
`_layoutEngine.Layout(typeGraph)`. Something like:

```csharp
private void RenderDesignModeGraph()
{
    if (_designGraph == null) return;

    // Do NOT run the layout engine here. DesignClass.X/Y is authoritative
    // (doc 05). Build GraphNode/GraphEdge directly from stored positions.
    var (nodes, edges) = DesignGraphToCanvasProjector.Project(_designGraph);

    GraphCanvasView.SetDesignGraph(_designGraph);
    GraphCanvasView.SetGraph(nodes, edges);
    MinimapView.SetGraph(nodes, edges);
    StatsText.Text = $"Design: {_designGraph.Classes.Count} classes, {_designGraph.Edges.Count} edges";
}
```

The layout engine should be invoked **only** from an explicit "Apply Layout"
/ Ctrl+L handler, which writes the result back into `DesignClass.X/Y` as a
single undoable command (per doc 05 and doc 08 R10), then calls
`RenderDesignModeGraph()` to redraw from the now-updated positions:

```csharp
private void OnApplyLayout()
{
    if (_designGraph == null) return;
    var typeGraph = DesignExporter.ToTypeGraph(_designGraph);
    var (nodes, _) = _layoutEngine.Layout(typeGraph);
    _designUndoManager.Execute(new DesignCommands.ApplyLayout(nodes), _designGraph);
    RenderDesignModeGraph();
}
```

**Severity**: Critical. This breaks the core "direct manipulation" promise
of Design Mode (doc 03's whole premise) — nothing the user places or drags
stays where they put it. Every other Design Mode bug downstream of "things
don't go where I click/drag" traces back to this one root cause. Fix this
first; re-test everything else after, since some reported issues may
disappear once this is fixed.

---

## BUG-2 (High): "Add Class Here" context-menu handler bypasses the command/undo system

**Symptom**: Not directly reported, but found while investigating BUG-1, and
it explains why undo may behave inconsistently after using the right-click
menu specifically (as opposed to the toolbar "Add Class" button or
click-to-add on empty canvas).

**Root cause**: In `OnDesignContextMenuRequested`, the `EmptyCanvas` case
does this:

```csharp
addItem.Click += (_, _) =>
{
    _designGraph.Classes.Add(new DesignClass
    {
        Name = "NewClass",
        X = target.WorldPosition.X - 100f,
        Y = target.WorldPosition.Y - 30f,
        Width = 200f,
        Height = 60f
    });
    RenderDesignModeGraph();
};
```

This mutates `_designGraph.Classes` **directly**, instead of going through
`DesignCanvasController.AddClassAt` (which exists, does the same math, and
*also* selects the new class and fires `GraphMutated`) or
`DesignCommands.AddClass` (the undoable command used everywhere else, per
doc 07 M1/M6 and the 12 "concrete commands" mentioned in
07-ui-integration-plan's gap analysis).

Consequences:
- **Not undoable.** Ctrl+Z after "Add Class Here" will not remove it,
  because no command was pushed to `DesignUndoManager`. This contradicts
  doc 03's explicit list of undoable actions ("Add class" is first on the
  list) and doc 07 M6's acceptance criterion "Ctrl+Z undoes any operation
  (add, delete, move, rename, connect)."
- **Doesn't mark the graph dirty via the normal path** — it calls
  `RenderDesignModeGraph()` directly instead of going through
  `OnDesignGraphMutated`, so `_designIsDirty` is not set. Auto-save
  (`TryAutoSave()`) won't pick up this change, and the user could lose it
  on crash (doc 08 R8) without realizing the file is "dirty."
- **Doesn't select the new class.** Every other "add class" path selects
  the new class immediately (so the user can rename it inline). This one
  doesn't, so right-click-added classes are silently unselected, which is
  inconsistent with click-to-add and the toolbar button.
- Logic duplication: the `X - 100f, Y - 30f` centering math is duplicated
  here instead of reusing `DesignCanvasController.AddClassAt`. Doc 08 D2's
  stated rationale ("single source of truth... avoids duplication and
  drift") is violated by this one handler.

**Fix**: Replace the handler body with a call into the existing controller
method, e.g.:

```csharp
addItem.Click += (_, _) =>
{
    _designCanvasController.AddClassAt(_designGraph, target.WorldPosition);
    // AddClassAt already fires GraphMutated, which triggers
    // RenderDesignModeGraph + dirty flag + auto-save scheduling.
};
```

If `AddClassAt` is currently `private`, make it `internal`/`public` rather
than re-implementing it at the call site. Same applies to the `Delete` case
in the same switch, which works by re-synthesizing a fake pointer-press to
select-then-delete (`HandlePointerPressed(...)` then `HandleDeleteKey(...)`)
instead of a direct, intention-revealing `DeleteClass(classId)` method on
the controller — functionally OK today, but fragile: it depends on
`HandlePointerPressed`'s hit-testing re-deriving the same class from a
synthetic point, which silently breaks if hit-testing logic changes.

**Severity**: High. Silent undo-stack inconsistency is the kind of bug users
file as "undo is broken" without being able to reproduce reliably, because
it only manifests for one specific entry point (right-click empty canvas)
out of several that all do "add a class."

---

## BUG-3 (Medium): `MenuFlyout.ShowAt(this)` does not anchor to the cursor

**Root cause**: `menu.ShowAt(this)` in `OnDesignContextMenuRequested` passes
the canvas control as the placement target with no explicit point. Avalonia's
default `FlyoutBase.ShowAt(Control)` placement is relative to the *control's*
bounds (commonly bottom-anchored depending on theme/placement mode), not the
cursor position — even though `target.WorldPosition` (and the original
screen-space click point) are known and already threaded through
`DesignContextMenuRequested`.

This likely compounds the "spawns at the bottom" *perception* even after
BUG-1 is fixed: the context menu itself may visually appear pinned to a
fixed spot on the canvas rather than at the cursor, making it look like
"everything happens at the bottom" even once the class itself starts
appearing at the correct click position.

**Fix**: Capture the original screen-space point (not just world-space) when
the right-click fires, and show the flyout at that point explicitly:

```csharp
// In OnPointerPressed, when raising the event:
var rcScreenPos = e.GetPosition(this);
DesignContextMenuRequested?.Invoke(target, rcScreenPos);

// In OnDesignContextMenuRequested:
menu.ShowAt(this, new Point(screenPos.X, screenPos.Y));
```

(Exact API depends on the Avalonia version in use — `FlyoutBase.ShowAt`
overloads vary; some versions need a placement-target control plus a
`Popup.HorizontalOffset/VerticalOffset`, others take a point directly.)

**Severity**: Medium on its own; currently entangled with and partially
masked by BUG-1. Re-test after BUG-1 is fixed to see how much of the
original complaint remains.

---

## GAP-1 (High, design gap not just a bug): No inspector/properties panel for the selected class

**What's missing**: `DesignCanvasController.SelectionChanged` is a real,
firing event:

```csharp
public event EventHandler<DesignSelection>? SelectionChanged;
```

…but nothing in `MainWindow.axaml.cs` subscribes to it. The only consumer
of selection today is the canvas renderer (draws the selection ring) and the
right-click context menu (reads whatever was hit at the right-click point,
independently of `SelectionChanged`). There is **no side panel** that shows
"here's the class you've selected, here are its members, here's where you
edit them."

This matches your report exactly: today, the only way to touch a selected
class is (a) rename via double-click header, (b) right-click → Add Member
submenu (which adds a member with a default name — you still can't see or
edit existing members from anywhere except directly on the canvas row),
or (c) drag to move / drag a port to connect. There is no
read-at-a-glance, edit-in-place properties view.

This is a real product gap, not just a missing wire-up — see the design doc
(`10-inspector-panel-and-relation-ux.md`) for the proposed fix, since it
needs UI/UX design, not just a bug fix.

**Severity**: High from a usability standpoint — it's the #2 thing you flagged
unprompted. Doesn't block correctness, but blocks the stated success
criterion in doc 01: *"A user can produce a 10-class diagram from scratch in
under 5 minutes"* is hard to hit if editing members requires a right-click
sequence per member with no overview of what a class already contains.

---

## GAP-2 (Medium): Selection model is single-class only at the controller level in practice

Doc 03 specifies multi-select (Shift+click, Ctrl+click, rubber-band lasso)
and `DesignSelection` is typed as a list (`SelectedClassIds`). But
`DesignCanvasController.Select(ClassRectangle rect)` (the private helper
backing most selection paths) does:

```csharp
private void Select(ClassRectangle rect)
{
    _selectedClassIds.Clear();
    _selectedClassIds.Add(rect.ClassId);
    UpdateSelection();
}
```

This always clears and replaces — it's a single-select implementation
wearing a multi-select data shape. That's fine *if* the additive/toggle
paths (Shift+click, Ctrl+click) call a different method that doesn't clear
first — but it means the inspector panel design (doc 10) cannot assume
"selection" always means "one class"; it should handle 0, 1, and N
gracefully from day one, and the underlying single-select behavior should be
verified/fixed before multi-select-dependent UI (bulk member edits, bulk
delete) is built on top of it.

**Severity**: Medium. Doesn't block a single-class inspector panel (the
common case), but will bite as soon as someone wires up "Ctrl+click to
multi-select then bulk-delete," which the keyboard shortcut table already
promises (Ctrl+A "select all," Delete "delete selection" — plural).

---

## Recommended fix order

1. **BUG-1** (layout-on-every-mutation) — fix first. It's the root cause of
   the most visible symptom and several latent ones (dragging fights the
   renderer too, not just adding).
2. **BUG-3** (flyout anchoring) — quick fix, re-test the "spawns at the
   bottom" report after BUG-1 + BUG-3 together; it may be fully resolved.
3. **BUG-2** (undo bypass on right-click add) — quick, contained fix, do it
   while already in this file.
4. **GAP-1** (inspector panel) — the bigger piece of work; see doc 10 for the
   proposed design. This is what actually gets you "select a class, see and
   edit its members and connections on the right" as you described.
5. **GAP-2** (selection model audit) — do this *before* or *during* GAP-1's
   implementation, since the inspector panel is the first consumer that
   will expose whether multi-select actually works end-to-end.
