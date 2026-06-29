# UI/UX Design Proposal for Design Mode

## 1. How similar apps behave (reference baseline)

Before proposing, here's the interaction contract that mature class-diagram tools converge on. Your app should match this contract because users carry muscle memory from these tools.

**StarUML / Visual Paradigm / Enterprise Architect (UML tools):**
- Left-side **toolbox** with one entry per element type: Class, Interface, Enum, Struct, and per relationship type (Inheritance, Implements, Association, Dependency, Aggregation, Composition).
- **Tool-first creation:** click a tool → click on canvas → element of that type appears. Double-click a tool to "lock" it (sticky mode) so you can stamp multiple. Press `Esc` to return to the Select tool.
- **Default tool is Select.** Clicking empty canvas with Select does *nothing destructive* — it either clears selection or starts a rubber-band marquee.
- **Connections** are also tools: pick "Inheritance" tool → click source class → click target class → edge appears. Drag-from-edge-port is an *accelerator*, not the only path.
- Right-side **Inspector** reflects the current selection: nothing / single class / single edge / multi-select.

**draw.io / diagrams.net / Lucidchart (general diagramming):**
- Click empty canvas = clear selection.
- Drag empty canvas = marquee select.
- Double-click empty canvas = quick-add popup near cursor.
- Hover over a shape = connection ports appear (4 or 8 dots).
- Drag a port onto another shape = create connector.
- Selected shape shows resize handles + a clear border color change.
- Right-click = context menu appropriate to what's under the cursor.

**IntelliJ / Rider class diagram:**
- Right-drag or middle-drag = pan.
- Scroll wheel = zoom (Ctrl+scroll = finer).
- Shift+drag = marquee.
- F2 = rename, Delete = remove, arrow keys = nudge by 1px (Shift = 10px).

The pattern across *all* of them: **Select is the default tool, creation is explicit and intentional, selection has obvious visual feedback, and there are at least two ways to do any common action (mouse + keyboard).**

---

## 2. Core design principles for your app

1. **Nothing destructive on a plain left-click of empty canvas.** This is the rule your current code breaks and it's the single most important fix. A stray click should never create a class.
2. **Selection is always visible.** Orange border (you already have the color), resize handles, and port affordances must render for the selected class.
3. **Tool-based creation.** Users pick *what* they want to create, then *where*. The toolbar's "Add Class" button should arm a tool, not fire-and-forget.
4. **Multiple paths to every action.** Mouse, keyboard shortcut, context menu, inspector button — at least two for the top 20 actions.
5. **Sticky modes are explicit.** Double-click a tool to lock it. Single-click arms it for one use.
6. **No silent mode changes.** When the user enters edge-drag mode, the cursor and status bar should make it obvious.
7. **The grid is the user's friend.** Snap-to-grid by default, toggleable, with visible grid lines at higher zoom levels.

---

## 3. Proposed UI layout

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  Menu Bar: File Edit View Insert Tool Help                                    │
├────────┬────────────────────────────────────────────────────────────┬────────┤
│        │  Toolbar: [Select][Class][Interface][Enum][Struct][Abs][Static]      │
│ Toolbox│           [│ Inherit][├ Implement][→ Assoc][⤍ Dep][◇ Aggr][◆ Comp]   │
│        │  ── Align:[⊣][⊣][⊥][⊤][═][│]  Distribute:[⇆][⇅]  Grid:[#] Snap:[{}] │
│ - Cls  ├────────────────────────────────────────────────────────────┤        │
│ - Intf │                                                                │ Inspct │
│ - Enum │                                                                │        │
│ - Strct│              CANVAS (with grid + namespaces)                  │ (right│
│ - Abs  │                                                                │  side) │
│ - Statc│                                                                │        │
│ ─────  │                                                                │        │
│ - Nspc │                                                                │        │
│ - Note │                                                                │        │
│ ─────  │                                                                │        │
│ Edge   │                                                                │        │
│ types  │                                                                │        │
│ ├ Inh  │                                                                │        │
│ ├ Imp  │                                                                │        │
│ ├ Asc  │                                                                │        │
│ ├ Dep  │                                                                │        │
│ ├ Agg  │                                                                │        │
│ └ Cmp  │                                                                │        │
├────────┴────────────────────────────────────────────────────────────┴────────┤
│  Status: 12 classes · 8 edges · Tool: Select · Snap: On · Zoom: 100%          │
└──────────────────────────────────────────────────────────────────────────────┘
```

Left toolbox = element creation tools. Top toolbar = relationship tools + alignment ops. Right panel = inspector. Bottom status bar = current state (vital for tool-mode UIs).

---

## 4. Tool model (replaces "left-click creates class")

### Tools
| Tool | Shortcut | Behavior |
|---|---|---|
| Select | `V` (default) | Click selects, drag moves, marquee-selects, drag-from-port creates edge |
| Class | `C` | Click on canvas creates a Class at cursor (centered). Single-use unless locked. |
| Interface | `I` | Same, for Interface |
| Enum | `E` | Same, for Enum |
| Struct | `S` | Same, for Struct |
| Abstract Class | `A` | Same, for AbstractClass |
| Static Class | `T` | Same, for StaticClass |
| Namespace | `N` | Click-drag on canvas creates a sized namespace box |
| Note / Comment | (none) | Free-floating annotation |
| Edge: Inheritance | `H` | Click source → click target. Sticky until Esc. |
| Edge: Implements | `M` | Same |
| Edge: Association | `L` | Same |
| Edge: Dependency | `D` | Same |
| Edge: Aggregation | `G` | Same |
| Edge: Composition | `O` | Same |
| Pan | hold `Space` | Cursor becomes hand, drag pans |

### Tool arming rules
- **Single-click a tool button** → arms for one use, then reverts to Select.
- **Double-click a tool button** → sticky mode: tool stays armed after each use until `Esc` or another tool.
- **Keyboard shortcut** → behaves like single-click arm (one use). Holding `Shift` while pressing the shortcut = sticky.
- The status bar always shows the current armed tool: `Tool: Class (sticky)` or `Tool: Select`.
- Cursor changes to a crosshair when an element tool is armed.

### This solves your "left-click creates class" bug
Plain left-click on empty canvas with Select tool = **clears selection** (or starts marquee if dragged). It never creates. To create, the user must explicitly arm a tool — exactly the StarUML contract.

---

## 5. Selection model (fixes "selecting a class won't highlight it")

### Mouse interactions (Select tool active)
| Action | Result |
|---|---|
| Click empty canvas | Clear selection |
| Click class | Select (replaces previous) |
| Shift+click class | Toggle in multi-selection |
| Ctrl+click class | Toggle in multi-selection (alias) |
| Drag empty canvas | Rubber-band marquee (selects on release) |
| Shift+drag empty canvas | Additive marquee |
| Click+drag class | Move (with snap-to-grid) |
| Click+drag in marquee over multiple | Move all selected together |
| Click edge | Select edge |
| Double-click class header | Inline rename |
| Double-click member | Inline edit member |
| Right-click | Context menu (see §8) |
| Hover class | Show 4 connection ports + resize cursor on corners |

### Visual selection states (rendering requirements)
These must all be distinct so the user always knows what's selected:

- **Hovered:** subtle 1px blue border (`#60A0E0`), no handles.
- **Selected (single class):** 3px orange border (`#FF8C00`), resize handle on bottom-right corner (12×12 square), connection ports visible on all 4 sides (10px circles) — currently your code has ports only hit-tested but you should also *draw* them when selected.
- **Selected (multi):** 2px orange border on each, no handles (handles only for single selection).
- **Selected (edge):** edge drawn 1px thicker + brighter; both endpoint classes get a thin orange border; midpoint handle for label editing.
- **Inline-editing class name:** TextBox overlay (you already have this) + the class gets a yellow focus ring.
- **Drag-in-progress:** dragged class rendered at 80% opacity on top of static picture (your `DrawSingleNode` already does this); other selected classes also move with it.
- **Edge-create drag:** rubber-band line from source port to cursor; target class under cursor highlights with green border (`#40B070`) to confirm valid drop target.

Your current `ClassRectangle.IsSelected` is set correctly in `BuildRectangles` — the bug is purely in the renderer. The `CanvasRenderer.DrawNodes` path is for Analyze Mode `GraphNode`s; **Design Mode goes through a different path** that isn't reading `IsSelected`. Fix: route Design Mode rendering through a Design-aware draw method that consults `ClassRectangle.IsSelected`, `IsDragging`, `IsResizing`, and draws the handles/ports.

---

## 6. Connection creation (fixes "selected classes not connected with Edge button")

Three equivalent methods, all of which should work:

### Method A: Edge tool (mouse-driven, primary)
1. User clicks "Association" in the edge toolbox (or presses `L`).
2. Status bar: `Tool: Association — click source class`.
3. User clicks source class → status: `…click target class`. Source class gets a pulsing border.
4. User clicks target class → edge created. Tool reverts to Select (or stays if sticky).
5. `Esc` cancels mid-flow.

### Method B: Drag from port (already implemented, fix the bugs)
1. With Select tool, hover a class → 4 ports appear.
2. Drag from a port → rubber-band line follows cursor.
3. Release on another class's port (or body — be lenient) → edge created.
4. **Default edge type** = whatever is currently selected in the toolbar's edge-type dropdown. So the user picks the type first, then drags.

### Method C: Keyboard (the user mentioned wanting this)
1. Select class A (click).
2. Press `L` (or whichever edge-type shortcut) → enters "edge mode with pre-selected source".
3. Status: `Edge: Association — A selected as source. Click target or press Esc.`
4. Click class B → edge A→B created.
5. Or: select A, hold `Shift`, click B → creates edge with current default type in one step. This is the fastest path for power users.

### Method D: Inspector "Add relation" button
In the inspector when class A is selected, a `+ Add relation to…` button opens a class-picker dropdown. Useful for large diagrams where the target is off-screen.

### The right-side "Edge" button's proper role
Currently the button just shows a hint. It should:
- Be a **dropdown** of edge types (Inheritance, Implements, Association, Dependency, Aggregation, Composition).
- Selecting one **arms the Edge tool with that type** (same as clicking the type in the left toolbox).
- If a class is already selected when the user picks a type, that class becomes the source and the status bar prompts for the target.

---

## 7. Keyboard shortcuts (comprehensive map)

### Tools
| Key | Tool |
|---|---|
| `V` | Select / Move (default) |
| `C` | Class |
| `I` | Interface |
| `E` | Enum |
| `S` | Struct |
| `A` | Abstract Class |
| `T` | Static Class |
| `N` | Namespace |
| `H` | Inheritance edge |
| `M` | iMplements edge |
| `L` | Association edge |
| `D` | Dependency edge |
| `G` | Aggregation edge |
| `O` | Composition edge |

### Editing
| Key | Action |
|---|---|
| `Delete` / `Backspace` | Delete selected |
| `F2` / `Enter` | Rename selected class |
| `Ctrl+Z` | Undo |
| `Ctrl+Y` / `Ctrl+Shift+Z` | Redo |
| `Ctrl+C` | Copy |
| `Ctrl+V` | Paste (at cursor, or offset) |
| `Ctrl+D` | Duplicate (offset by 20,20) |
| `Ctrl+A` | Select all |
| `Esc` | Cancel current action / exit tool / deselect |
| `Tab` | Cycle selection to next class (alphabetical) |
| `Shift+Tab` | Cycle backward |

### Navigation
| Key | Action |
|---|---|
| `Space` (hold) | Pan tool |
| `F` | Fit to screen |
| `0` | Reset zoom to 100% |
| `+` / `=` | Zoom in |
| `-` | Zoom out |
| `Arrow keys` | Nudge selected by 1 grid unit |
| `Shift+Arrow` | Nudge by 10 units |

### File
| Key | Action |
|---|---|
| `Ctrl+N` | New design |
| `Ctrl+O` | Open design |
| `Ctrl+S` | Save |
| `Ctrl+Shift+S` | Save As |
| `Ctrl+E` then `M` | Export Mermaid |
| `Ctrl+E` then `C` | Export C# stub |
| `Ctrl+E` then `J` | Export JSON |

### Member editing (within a selected class, via inspector)
| Key | Action |
|---|---|
| `Alt+F` | Add Field |
| `Alt+P` | Add Property |
| `Alt+M` | Add Method |
| `Alt+C` | Add Constructor |
| `Alt+E` | Add Event |
| `Ctrl+Up` | Move member up |
| `Ctrl+Down` | Move member down |

### View
| Key | Action |
|---|---|
| `G` (when nothing selected, otherwise reserved for Aggregation edge) | Toggle grid |
| `Shift+S` | Toggle snap-to-grid |
| `Ctrl+Shift+H` | Toggle minimap |
| `Ctrl+Shift+I` | Toggle inspector |

> ⚠️ There's a conflict: `G` for grid vs Aggregation edge. Resolve by reserving `G` for Aggregation and using `` ` `` (backtick) for grid toggle, or `Ctrl+'`. I'd recommend `Ctrl+'` for grid.

---

## 8. Context menus (right-click)

### Empty canvas
- **Add Class Here** →
- **Add Interface Here** →
- **Add Enum Here** →
- **Add Struct Here** →
- **Add Namespace Here** →
- ──
- Paste (if clipboard non-empty)
- ──
- Select All
- Fit to Screen

### Class
- Rename (`F2`)
- Change Kind → submenu (Class / Interface / Enum / Struct / Abstract / Static)
- Change Namespace → submenu of existing namespaces + "New…"
- Add Member → submenu (Field / Property / Method / Constructor / Event)
- ──
- Duplicate (`Ctrl+D`)
- Copy (`Ctrl+C`)
- Delete (`Del`)
- ──
- Connect To → submenu of edge types (arms edge tool with this class as source)
- ──
- Bring to Front / Send to Back

### Member
- Edit Name
- Edit Type
- Cycle Visibility (`+` → `-` → `#` → `~`)
- ──
- Move Up (`Ctrl+↑`)
- Move Down (`Ctrl+↓`)
- ──
- Delete

### Edge
- Change Type → submenu
- Edit Label
- Reverse Direction (swap From/To)
- ──
- Jump to Source
- Jump to Target
- ──
- Delete

### Namespace
- Rename
- Change Parent Namespace → submenu
- ──
- Add Class Inside
- ──
- Collapse / Expand (hide members, show count badge)
- Delete (keep members / delete members)

---

## 9. Inspector panel (right side)

Four states, already in your `DesignInspectorViewModel`:

### Empty (nothing selected)
```
Design Overview
─────────────────
Classes:    12
Edges:       8
Namespaces:  3
Warnings:    ⚠ 2 unnamed classes

[Add Class]  [Add Namespace]
```

### Single class selected
```
┌─ Class ────────────────────┐
│ Name:     [Customer      ] │
│ Kind:     [Class       ▾]  │
│ Namespace:[App.Models   ▾]  │
│ Stereotype:[           ]   │
├────────────────────────────┤
│ Members (5)        [+ Add] │
│  + Id : Guid               │
│  + Name : string           │
│  + Orders : List<Order>    │
│  + GetTotal() : decimal    │
│  - Validate() : bool       │
├────────────────────────────┤
│ Outgoing Relations (2)     │
│  → Order     (Association) │
│  → Address   (Aggregation) │
│  [jump] [change type] [×]  │
├────────────────────────────┤
│ Incoming Relations (1)     │
│  ← User      (Association) │
│  [jump] [change type] [×]  │
└────────────────────────────┘
```

The `+ Add` button is a dropdown: Field / Property / Method / Constructor / Event. Each member row has hover-revealed buttons: edit, move up, move down, delete, cycle visibility.

### Single edge selected
```
┌─ Relation ─────────────────┐
│ Type: [Association    ▾]   │
│ Label:[places        ]     │
├────────────────────────────┤
│ From: Customer  [→ jump]   │
│ To:   Order      [→ jump]  │
├────────────────────────────┤
│ [Reverse Direction]        │
│ [Delete]                   │
└────────────────────────────┘
```

### Multi-select
```
┌─ Multi-Select ─────────────┐
│ 4 classes selected         │
│ ─────────                  │
│ Customer                   │
│ Order                      │
│ Product                    │
│ LineItem                   │
├────────────────────────────┤
│ Align: [⊣][⊥][⊤][═][│][⊥]  │
│ Distribute: [⇆][⇅]         │
│ Same Size: [W][H][both]    │
├────────────────────────────┤
│ Group into Namespace…      │
│ Copy / Duplicate / Delete  │
└────────────────────────────┘
```

The multi-select alignment operations are *huge* for productivity on a 2D grid.

---

## 10. Namespaces

A namespace is a **container element** the user can draw on the canvas:

### Creation
- Pick Namespace tool (`N`) → click-drag on canvas to size → creates a labeled box (rounded rect, title bar at top).
- Or: right-click empty → "Add Namespace Here".

### Membership
- Drag a class *into* a namespace → class's `Namespace` property updates to the namespace's name.
- Drag a class *out* → namespace cleared (or set to "(global)").
- Classes inside a namespace visually appear within its bounds; the namespace's bounds auto-expand to contain children.
- Multi-select classes + right-click → "Group into Namespace…" → creates a namespace around them.

### Nesting
- Namespaces can be nested (your `DesignNamespace.ParentNamespaceId` already supports this).
- Drag a namespace into another namespace → parent reassigns.
- Display: nested namespaces get indented titles and progressively lighter backgrounds.

### Layout
- Moving a namespace moves all its member classes (the cluster-drag behavior you already have for Analyze Mode namespaces should apply in Design Mode too).
- Resizing a namespace resizes the container only; classes inside don't resize but the namespace auto-grows to contain them.

### Collapse/Expand
- Click the chevron in the namespace title bar → collapse to just the title bar with a `N classes` badge.
- Collapsed namespaces hide their members from rendering but keep them in the model.
- Edges to collapsed members route to the namespace boundary.

---

## 11. Grid, snapping, and alignment

### Grid
- Visible grid dots every 20px (toggle with `Ctrl+'`).
- Major grid lines every 100px.
- Grid visibility auto-fades at low zoom (< 0.3) to avoid clutter.

### Snapping
- Snap-to-grid ON by default (`Shift+S` to toggle).
- When dragging a class, it snaps to the nearest grid intersection on release.
- Resize also snaps.

### Smart guides (alignment helpers)
During drag, when a class's edge aligns with another class's edge (left, right, top, bottom, center H, center V), draw a temporary magenta guide line. Snaps to alignment within 5px threshold. This is the single most useful feature for arranging a 2D grid of classes — users will love it.

### Alignment operations (multi-select)
Available in the inspector and toolbar:
- Align Left / Right / Center H / Top / Bottom / Center V
- Distribute Horizontally / Vertically (equal spacing)
- Same Width / Same Height / Same Size

These dramatically speed up tidying a diagram.

---

## 12. Pan and zoom

| Input | Action |
|---|---|
| Scroll wheel | Zoom toward cursor |
| Ctrl+scroll | Finer zoom |
| Shift+scroll | Pan horizontally |
| Scroll (no Ctrl) | Pan vertically (or zoom — pick one; I recommend zoom-toward-cursor as default, matching Figma/Excalidraw) |
| Middle-drag | Pan |
| Right-drag | Pan (only when not over a class; otherwise right-drag could be reserved for context menu on release-without-drag) |
| `Space`+drag | Pan |
| Pinch (trackpad) | Zoom |
| `F` | Fit to screen |
| `0` | Reset to 100% |
| `+`/`-` | Zoom in/out |

Recommendation: match Figma/Excalidraw — scroll wheel = zoom, Shift+scroll = horizontal pan, middle-drag or Space+drag = pan. This is what users under 40 expect in 2024.

---

## 13. Specific fixes for your reported bugs

### Bug: "Left click anywhere creates a new class"
**Fix:** In `DesignCanvasController.HandlePointerPressed`, the `ClassRectangleHitTest.None` case currently calls `AddClassAt`. Change this to:
1. If a creation tool is armed (Class/Interface/Enum/etc.) → create that type at the click position, then disarm (unless sticky).
2. If Select tool is armed → clear selection (or start marquee if drag follows).

Add a `CurrentTool` property to `DesignCanvasController`:

```csharp
public DesignTool CurrentTool { get; set; } = DesignTool.Select;
public bool IsToolSticky { get; set; }
```

The `AddClassAt` call moves into a new method `CreateAt(DesignTool tool, SKPoint worldPos)` that switches on tool to create Class/Interface/Enum/Struct/etc.

### Bug: "Selecting a class won't highlight it"
**Fix:** The render path for Design Mode must consult `ClassRectangle.IsSelected`. Looking at your code, `RenderDesignModeGraph` builds a `LayoutResult` and calls `_layoutEngine.LayoutFromLayoutResult` which produces `GraphNode`s — but `GraphNode` has no `IsSelected` field and `CanvasRenderer.DrawNodes` keys off `vp.SelectedNode` (a single `GraphNode?`).

Two options:
- **(A)** Pass the Design selection set into `ViewportState` (change `SelectedNode` to `SelectedNodes` as a set, or add a `SelectedNodeIds` set). Update `CanvasRenderer.DrawNodes` to highlight any node whose Id is in the set.
- **(B)** Render Design Mode through a separate `DrawDesignClassRectangles` method that takes `IReadOnlyList<ClassRectangle>` directly and draws the selection/handles/ports from `ClassRectangle` state. Don't go through the Analyze-Mode `GraphNode` path at all.

Option B is cleaner long-term — Design Mode has richer per-rectangle state (handles, ports, inline-editing) that `GraphNode` doesn't model. Recommend B.

### Bug: "Selected classes not connected with right side Edge button"
**Fix:** Make the Edge button a dropdown. On selecting a type:
1. If exactly one class is selected → arm Edge tool with that class as source. Status bar: `Edge: <type> — <ClassName> is source. Click target.`
2. If zero or multiple selected → arm Edge tool with no pre-selected source. Status bar: `Edge: <type> — click source class.`

This satisfies the user's request: "select class, press key, [create edge]". The "press key" path: select class → press `L` → click target.

---

## 14. Status bar (essential for tool-mode UIs)

A tool-mode UI is unusable without a status bar telling the user what state they're in. Add a bottom status bar:

```
Tool: Class (sticky)  |  Selected: Customer  |  12 classes, 8 edges  |  Snap: On  |  Zoom: 100%  |  ⚠ 2 unnamed
```

When mid-action:
```
Edge: Association — Customer is source. Click target class. (Esc to cancel)
```

When hovering:
```
Hover: Order (App.Models)  |  3 relations
```

This is the cheapest UX win in the whole app. It eliminates the "what mode am I in?" confusion that plagues tool-based editors.

---

## 15. Additional features similar apps have (consider for roadmap)

| Feature | Priority | Why |
|---|---|---|
| **Copy/paste** (incl. cross-diagram) | High | Fundamental; users expect Ctrl+C/V |
| **Duplicate** (Ctrl+D) | High | Faster than copy+paste for in-place clones |
| **Undo/redo** (you have it) | ✅ | Keep |
| **Multi-select alignment** | High | Critical for tidy 2D layouts |
| **Smart guides** | High | Killer feature for arrangement |
| **Snap to grid** | High | Prevents messy layouts |
| **Minimap** (you have it) | ✅ | Keep |
| **Search/filter** | Medium | For large diagrams, find by name |
| **Layers** | Low | Most class diagrams don't need it |
| **Export to SVG/PNG** | Medium | For docs |
| **Templates** (pre-built GoF patterns) | Low | Nice-to-have |
| **Auto-layout** button | Medium | Run layout engine on Design Mode graph for quick tidy |
| **Quick-add via typing** ("/class Customer" command palette) | Low | Power-user feature |
| **Validate diagram** (you have `DesignValidator`) | ✅ | Surface warnings in inspector |
| **Find usages** (which edges reference a class) | Medium | Useful for refactoring |
| **Comments/notes** attached to classes | Low | Documentation |
| **Color tags** for classes (e.g., "domain", "infra") | Medium | Visual grouping beyond namespaces |

---

## 16. Recommended implementation order

1. **Stop creating class on empty click.** Replace with Select-tool default. (1–2 hours)
2. **Add tool state to `DesignCanvasController`.** Wire toolbar buttons to set it. (2–3 hours)
3. **Fix selection rendering.** Route Design Mode through a method that reads `ClassRectangle.IsSelected`. (2–4 hours)
4. **Add status bar.** Show current tool + selection + counts. (1 hour)
5. **Add keyboard shortcuts** for tools (V, C, I, E, S, A, T, N, H, M, L, D, G, O). (1 hour)
6. **Fix edge-creation flow** with the three methods (tool, drag, select+key). (3–4 hours)
7. **Add marquee selection.** Drag empty canvas with Select tool. (2 hours)
8. **Add multi-select alignment** operations. (3 hours)
9. **Add grid + snapping.** (3 hours)
10. **Add smart guides.** (4 hours — geometry is fiddly)
11. **Add namespaces as containers** with drag-into membership. (4–6 hours)
12. **Polish context menus.** (2 hours)

Items 1–5 are the critical path. After those, the app is usable. Items 6–11 make it pleasant.

---

## 17. Summary of the contract

In one sentence: **Selection is the default, creation is explicit, every selection has obvious visual feedback, every common action has at least two paths (mouse + keyboard), and the status bar always tells the user what mode they're in.** Match StarUML for the toolbox model, match Figma for pan/zoom feel, match draw.io for the right-click context menu richness. Fix the three reported bugs by (1) introducing a tool state, (2) routing Design rendering through `ClassRectangle.IsSelected`, and (3) making the Edge button a dropdown that arms the edge tool with an optional pre-selected source.