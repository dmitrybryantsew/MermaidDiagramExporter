# 10 — Inspector Panel and Class-Relation UX

## Problem statement

Today, selecting a class on the Design Mode canvas does almost nothing.
`DesignCanvasController.SelectionChanged` fires but has no UI consumer.
The only ways to act on a class are:

- Double-click header → inline rename (name only).
- Right-click → context menu → Add Member submenu (blind add, no visibility
  into existing members) or Delete.
- Drag header → move. Drag port → connect.

There is no way to:
- See a selected class's full member list in one place.
- Edit an existing member's name/type/visibility without hunting for the
  exact pixel row on a possibly-small canvas rectangle.
- See or edit a class's outgoing/incoming relations (edges) without
  visually tracing lines on the canvas.
- Reorder members without a (currently undiscoverable) drag gesture.

This doc proposes a **right-side inspector panel**, the standard pattern
for this class of tool (every UML/diagram editor — draw.io, Lucidchart,
StarUML, Visual Paradigm, Mermaid Live's class-diagram mode — uses some
form of "select shape on canvas → properties panel on the side").

This is additive: the canvas keeps working exactly as it does today
(inline rename, drag-to-move, port-to-connect all stay). The panel is a
second, complementary way to do the same mutations, better suited to
precise editing of things that are awkward to hit-test on a small rectangle
(member rows, edge endpoints, visibility flags).

---

## Layout

```
┌───────────────────┬─────────────────────────────────────┬──────────────────┐
│  Design Mode       │                                     │  Inspector       │
│  toolbar           │              Canvas                 │  (selection-     │
│  (existing,        │     (existing, unchanged)           │   driven)        │
│  left sidebar)     │                                     │                  │
│                    │                                     │                  │
│  New / Open / Save │                                     │  [Nothing        │
│  Undo / Redo       │                                     │   selected:      │
│  Add Class         │                                     │   hint text]     │
│  Add Edge          │                                     │                  │
│  Apply Layout      │                                     │  [Class          │
│  Export...         │                                     │   selected:      │
│                    │                                     │   see below]     │
└───────────────────┴─────────────────────────────────────┴──────────────────┘
```

The inspector is a new third column, shown only in Design Mode (mirrors how
the existing left sidebar already swaps content by mode per doc 02). It's
collapsible (a thin vertical strip with a `<` / `>` toggle) so it doesn't
eat canvas space when the user just wants to draw freely — default state:
**open**, since the inspector is the primary editing surface this doc adds.

Width: ~280–320px fixed (not resizable in v1 — resizable splitters are a
nice-to-have, not core to the feature).

---

## Inspector states

### State A — Nothing selected (or multi-select, see State D)

Shows a short hint and aggregate stats, so the panel isn't just dead space:

```
┌──────────────────────────┐
│  No selection             │
│                           │
│  Click a class to inspect │
│  and edit it here.        │
│                           │
│  ─────────────────────   │
│  Design summary           │
│  12 classes                │
│  18 edges                  │
│  3 unnamed classes ⚠      │
└──────────────────────────┘
```

The "unnamed classes" warning is a cheap, high-value addition: surfaces
classes still named "NewClass"/"NewClass1"/etc. so the user doesn't forget
to rename one before export. Click it → selects + scrolls to that class.

### State B — Single class selected (the main case)

```
┌──────────────────────────────┐
│  ◀ Back            [Delete]  │
│                               │
│  Name                        │
│  ┌───────────────────────┐   │
│  │ Animal                 │   │   ← editable text field, same
│  └───────────────────────┘   │     validation as inline canvas edit
│                               │
│  Kind          Namespace      │
│  [Class ▾]     [MyApp.Core ▾] │   ← dropdowns; Namespace is free-text
│                               │     combo (existing names + "new...")
│  Stereotype (optional)        │
│  ┌───────────────────────┐   │
│  │                        │   │
│  └───────────────────────┘   │
│                               │
│  ── Members ──────  [+ Add ▾]│   ← dropdown: Field/Property/Method/
│                               │     Constructor/Event
│  ┌───────────────────────┐   │
│  │ ⠿ + Name : string      │ ⋮│   ← drag handle, visibility cycle icon,
│  │ ⠿ + Age  : int         │ ⋮│     name, type, row menu (⋮ = rename/
│  │ ⠿ # _id  : Guid        │ ⋮│     delete/move up/down — same actions
│  │ ⠿ + GetName(): string  │ ⋮│     as today's right-click, just visible
│  └───────────────────────┘   │     and reachable without hunting
│                               │
│  ── Relations ────────────── │
│  Outgoing                    │
│  → Dog : Inheritance         │   ← click → selects the OTHER class +
│  → Pet : Aggregation     [×] │     pans/zooms canvas to it. [×] deletes
│                               │     the edge. Click the edge-kind word →
│  Incoming                    │     inline dropdown to change kind.
│  ← Zoo : Association     [×] │
│                               │
│  [+ Connect to...]            │   ← opens a searchable list of other
│                               │     classes; picking one starts edge
│                               │     creation (same flow as port-drag,
│                               │     just keyboard/list-driven instead
│                               │     of a precise mouse drag)
└──────────────────────────────┘
```

Key design choices and why:

- **Member rows are full inspector rows, not canvas hit-targets.** Each row
  has its own click targets for name (text field), type (text field),
  visibility (icon button that cycles `+`/`-`/`#`/`~`, matching doc 03's
  cycling behavior but now with a real clickable target instead of a tiny
  canvas-rendered glyph), and a `⋮` overflow menu for delete/move. This
  solves "you can't really edit members of selected classes" directly —
  editing happens in a list with normal-sized targets, not on a diagram
  rectangle that might be 60px tall with 4 members crammed in it.
- **Drag handle (`⠿`) for reorder** replaces the currently-undiscoverable
  "drag member row up/down within the class" canvas gesture (doc 03 lists
  it but it's not visually hinted anywhere). A list with explicit drag
  handles is a well-understood affordance; reuse whatever drag-reorder
  pattern the rest of the app already has, if any (check if `ResultsList`
  or similar existing list controls already support reorder — reuse, don't
  reinvent).
- **Relations section is the actual fix for "we need fields to connect."**
  Today, connecting two classes requires finding the tiny port circle on
  the canvas and dragging precisely to another tiny port circle (doc 03,
  doc 08 R11 — explicitly flagged as a risk for small classes). The
  inspector's "Outgoing / Incoming" lists plus "+ Connect to..." give a
  non-spatial way to create and audit relationships:
  - You can see *all* of a class's relationships in one place (impossible
    on canvas once a diagram has any density — edges cross and overlap).
  - You can create a relationship by typing a class name instead of
    pixel-precise dragging — valuable for trackpads, small screens, or
    just classes that are far apart on a large canvas.
  - You can change an edge's kind via a dropdown instead of right-click →
    "Change Type" → reopening a menu.
- **Kind / Namespace dropdowns** surface fields that today can *only* be set
  by editing the underlying JSON or not at all from the UI (scanning the
  plan docs, there's no documented UI path to set `ClassKind` — Interface
  vs Class vs Enum vs Struct vs StaticClass vs AbstractClass — or
  `Namespace` post-creation. This is a real functional gap: the data model
  (doc 05) supports 6 `ClassKind` values and per-class namespaces, but
  nothing in the UI plan exposes changing them after creation.). This
  panel is the natural place to add that missing control surface.

### State C — Single member selected (optional refinement, can ship after B)

When a member row in the inspector is focused/clicked (not just the parent
class), the panel could show a focused sub-view:

```
┌──────────────────────────┐
│  ◀ Animal                │
│                           │
│  Member: Name             │
│  Kind     [Property ▾]    │
│  Name     [Name        ]  │
│  Type     [string      ]  │
│  Visibility [Public ▾]    │
│                           │
│  Parameters (methods only)│
│  (hidden for Property)    │
└──────────────────────────┘
```

This is a refinement, not required for the v1 panel — inline editing of
name/type directly in the row (State B) covers the common case. Add this
only if user testing shows the inline row fields feel too cramped for
methods with multiple parameters.

### State D — Multiple classes selected

```
┌──────────────────────────┐
│  3 classes selected       │
│                           │
│  [Delete All]              │
│  [Align ▾]   [Distribute ▾]│   ← optional, future: align left/top/
│                           │     center, distribute horizontally —
│                           │     only add if cheap; not required for v1
│                           │
│  Namespace (bulk set)      │
│  [MyApp.Core ▾]  [Apply]   │
└──────────────────────────┘
```

Bulk operations are a nice-to-have layered on top of GAP-2's fix (the
selection model needs to genuinely support multi-select first — see doc 09
GAP-2). Don't build this until GAP-2 is verified; ship State A/B/(C) first.

---

## Data flow / wiring

1. `MainWindow.axaml.cs` subscribes to `DesignCanvasController.SelectionChanged`
   (currently unsubscribed — this is the one-line gap that made the whole
   feature invisible):

   ```csharp
   _designCanvasController.SelectionChanged += OnDesignSelectionChanged;
   ```

2. `OnDesignSelectionChanged(DesignSelection selection)`:
   - 0 classes selected → show State A, recompute summary stats.
   - 1 class selected → look up the `DesignClass` by ID in `_designGraph`,
     populate State B's fields, populate Relations from
     `_designGraph.Edges.Where(e => e.FromClassId == id || e.ToClassId == id)`.
   - >1 selected → State D.

3. **Every inspector field edit must go through the same command/undo path
   as canvas edits** — do not write directly to `DesignClass` properties
   from the panel's event handlers. This is the same mistake as BUG-2 in
   doc 09 (the right-click "Add Class Here" handler that bypassed
   `DesignCommands`); don't repeat it here. Concretely:

   ```csharp
   // Inspector "Name" field LostFocus / Enter:
   _designUndoManager.Execute(
       new DesignCommands.RenameClass(classId, newName), _designGraph);
   RenderDesignModeGraph();   // and refresh the inspector's own fields
   ```

   If `DesignCommands.RenameClass` (or equivalent) doesn't exist yet for a
   given field (e.g. there may be no existing command for "change
   ClassKind" or "change Namespace" since the UI never exposed them before
   — see State B notes above), **add the command class first**, following
   the existing 12-command pattern, rather than mutating the model inline
   from the inspector's code-behind.

4. **Inspector ↔ canvas selection must stay in sync both directions.**
   Clicking a relation row in the inspector ("→ Dog : Inheritance") should
   select `Dog` on the canvas (and ideally pan/center it into view if it's
   off-screen — reuse whatever pan-to-node logic Analyze Mode's "focus"
   feature already has, per `FocusedGraphNavigationController` /
   `FocusNavigator.cs` in the existing codebase — don't write a second
   pan-to-element implementation).

5. **Inspector must re-render on `GraphMutated`, not just `SelectionChanged`.**
   If the user adds a member via the canvas's right-click menu while the
   inspector is open and showing that same class, the inspector's member
   list must refresh too, or it'll show stale data. Cheapest correct fix:
   inspector refresh is driven by both events, and re-reads the current
   selection's class fresh from `_designGraph` each time rather than caching
   a snapshot.

---

## Sequencing relative to doc 09's bugs

Build order matters here:

1. Fix doc 09 BUG-1 first (layout-on-every-mutation). The inspector will
   show a class's X/Y-derived... actually the inspector as designed above
   doesn't surface X/Y directly (position is a canvas concern, not an
   inspector field — keep it that way, don't add X/Y number fields to the
   panel, it invites manual pixel-fiddling that fights with drag). But
   *visually verifying* "did my edit do the right thing" while testing the
   inspector is much harder if classes are still jumping to layout-engine
   positions on every mutation. Fix BUG-1 before building/testing this.
2. Fix doc 09 GAP-2 (verify multi-select actually clears/adds correctly)
   before building State D (bulk operations). State A/B can be built and
   shipped independently of GAP-2.
3. Build the inspector itself: State A → State B (core) → wire relations →
   State D (only if GAP-2 is confirmed solid) → State C (optional).

## Out of scope for this doc (explicitly deferred)

- Resizable splitter between canvas and inspector (fixed width is fine for
  v1; matches doc 08's general "don't over-build v1" posture).
- Align/Distribute tools in State D (sketched above as an idea, not a
  commitment).
- A second, separate pan-to-element implementation — reuse Analyze Mode's
  focus/navigation code (see step 4 above) rather than writing a new one.
- Validation/error display inline in the inspector (e.g. "duplicate class
  name") — for v1, reuse the existing `DesignValidator` + export-time error
  dialog (doc 06); inline live validation in the panel is a future
  enhancement once the panel itself is proven useful.
