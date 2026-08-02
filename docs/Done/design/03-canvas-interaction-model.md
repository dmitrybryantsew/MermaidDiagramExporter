# 03 — Canvas Interaction Model

## The principle

Design Mode interaction follows the **direct manipulation** paradigm: every
action is visible on screen, reversible, and discoverable. No modal dialogs
for routine actions. The toolbar is for actions that aren't spatial
(undo, save, load, export).

## Pointer gestures

### Single-click

| Target | Action |
|--------|--------|
| Empty canvas (Design Mode) | Place new class at click position, start inline name edit |
| Empty canvas (Analyze Mode) | Deselect |
| Class body | Select class |
| Class header | Select class |
| Member row | Select member |
| Edge | Select edge |
| Resize handle | (no-op on click; only responds to drag) |

### Double-click

| Target | Action |
|--------|--------|
| Class header | Enter inline name edit mode |
| Member name | Enter inline name edit mode |
| Member type column | Enter inline type edit mode |

### Drag

| Source → Target | Action |
|-----------------|--------|
| Class header → anywhere | Move class |
| Resize handle (bottom-right) | Resize class |
| Edge port (right side) → another class's edge port | Create edge between classes (type selector appears on release) |
| Empty canvas → empty canvas | Rubber-band select (lasso) |
| Member row → up/down within class | Reorder member |

### Right-click

Opens a context menu:
- Class: Rename, Delete, Duplicate, Add Member → (Field/Property/Method)
- Edge: Change Type (Association/Inheritance/Implements), Delete
- Member: Rename, Change Type/Visibility, Delete, Move Up, Move Down
- Empty canvas: Add Class, Paste (if clipboard has a class)

### Keyboard

| Key | Action |
|-----|--------|
| Delete | Delete selection |
| Backspace | Delete selection (same) |
| Escape | Cancel current operation (edge creation, drag, inline edit) |
| Enter | Confirm inline edit |
| Ctrl+Z | Undo |
| Ctrl+Shift+Z / Ctrl+Y | Redo |
| Ctrl+A | Select all |
| Ctrl+C | Copy selected class(es) to clipboard |
| Ctrl+V | Paste class(es) from clipboard |
| Ctrl+D | Duplicate selected class(es) |
| Ctrl+L | Apply layout (re-run layout engine) |
| Ctrl+S | Save design |
| Arrow keys | Nudge selected class by 1px (Shift+Arrow = 10px) |

## Selection model

```csharp
public sealed class DesignSelection
{
    public HashSet<string> SelectedClassIds { get; } = new();
    public HashSet<string> SelectedEdgeIds { get; } = new();
    public HashSet<string> SelectedMemberIds { get; } = new();  // "{classId}:{memberIndex}"

    public bool IsEmpty => /* all three sets empty */;
    public void Clear();
    public void SelectClass(string id, bool additive = false);
    public void SelectEdge(string id, bool additive = false);
    public void SelectMember(string id, bool additive = false);
}
```

Selection is rendered as:
- Selected class: thicker border (3px instead of 1px), light highlight color
- Selected member: highlighted row background
- Selected edge: thicker line, slightly darker color

Clicking without modifier clears the previous selection. Shift+click adds to
selection. Ctrl+click toggles.

## Edge creation flow (the trickiest interaction)

1. User hovers over a class — small circular ports appear on the left and right
   edges of the class rectangle.
2. User mousedowns on a port — a "rubber band" line follows the cursor.
3. User drags to another class's port — the line snaps to the target port when
   within snap distance (default 20px).
4. User releases — an edge selector pops up near the release point with three
   buttons: Association, Inheritance, Implements. Click one to confirm, or
   press Escape to cancel.
5. Edge is added to the graph with the selected type.

The edge type selector is a small floating popup, not a modal dialog. It
disappears after selection or when the user clicks elsewhere.

## Inline editing

When entering inline edit mode (double-click name/type):
- The text becomes editable in-place (TextBox overlay positioned over the
  existing text).
- All other interaction is disabled (no drag, no click-select).
- Enter confirms, Escape cancels.
- Validation: class names must be valid C# identifiers; member names must be
  valid C# identifiers; types must be valid C# type expressions.
- Invalid input reverts to the previous value on confirm.

## Rubber-band select (lasso)

1. User mousedowns on empty canvas.
2. Drags — a dashed rectangle follows the cursor.
3. Releases — all classes whose bounding boxes intersect the rectangle are
   added to the selection (replacing previous selection unless Shift held).

## Undo/redo scope

Every mutation goes through a single `Mutate(Action)` method that:
1. Captures the inverse action before applying.
2. Applies the action.
3. Pushes the inverse onto the undo stack.
4. Clears the redo stack.

Actions that are undoable:
- Add class
- Delete class (and all its edges)
- Move class
- Resize class
- Rename class
- Add member
- Delete member
- Rename member
- Change member type/visibility
- Reorder member
- Add edge
- Delete edge
- Change edge type
- Paste class(es)

Actions that are NOT undoable:
- Mode switch
- Save / Load (these are explicit user actions, not mutations)
- Apply layout (this is a derived operation, not a mutation)
- Undo / Redo themselves (obviously)

## Coordinate system

All positions are stored in **world coordinates** (the same coordinate system
as Analyze Mode's `LayoutNode.X/Y`). This means:
- A design saved to JSON can be loaded into Analyze Mode and laid out.
- Pan/zoom work identically.
- The layout engine can be applied to a Design Mode graph (just feed it
  through `GraphLayoutCoordinator.CreateLayout`).

The Design Mode toolbar's "Apply Layout" button runs the layout engine on the
current `DesignGraph`, producing a `LayoutResult` that re-positions the
classes. The user's manual positions are preserved in the `DesignGraph`; the
layout result is a separate concern.

## Risks

- **Edge port discovery**: the ports must be visible on hover but not clutter
  the UI when not hovering. Implementation: show ports only when the mouse is
  within the class bounding box + 20px margin.
- **Inline edit focus stealing**: when entering inline edit, the TextBox must
  receive focus immediately. Avalonia's `Focus()` call must be deferred until
  after the visual tree update.
- **Drag during inline edit**: must be impossible. Disable drag handlers while
  in inline edit mode.
