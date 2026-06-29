# 04 — Class Rectangle Component

## What it is

A `ClassRectangle` is a **plain data/state object** (not an Avalonia `Control`)
that holds the per-class interaction state and edit-handle geometry. It is
read by `DesignCanvasController` and `CanvasRenderer` to drive *additional
Skia draw calls* layered into `GraphCanvas`'s existing immediate-mode
rendering pipeline.

It is **not** a separate `Control` with its own render path. `GraphCanvas`
remains the single owner of canvas rendering; `ClassRectangle` is a
per-class bookkeeping object that the canvas controller and renderer
consult during pointer handling and draw.

## Why this shape (and not an Avalonia Control)

The existing rendering pipeline is **immediate-mode Skia into a single
bitmap**: `GraphCanvas.RenderNow()` creates one `SKSurface`, calls
`CanvasRenderer.DrawNodes(canvas, nodes, ...)` which paints every node
directly, then blits the result as a `WriteableBitmap`. There is no Avalonia
visual tree of per-node controls — nodes are pixels, not WPF/Avalonia
elements.

If `ClassRectangle` were an `Avalonia.Controls.Control`:
- It would need its own render method that draws into the *same* bitmap as
  `GraphCanvas` (or a separate per-class bitmap that gets composited, which
  the current pipeline has no concept of — `Panel.ZIndex` applies to the
  Avalonia visual tree, not to Skia bitmaps).
- It would need to coordinate pointer-event routing with `GraphCanvas`'s
  existing pointer handlers (panning, dragging, hit-testing). Two controls
  can't both own pointer routing over the same screen region without one
  deliberately not handling events (`e.Handled = false`) and careful
  PointerCapture coordination — a non-trivial detail doc 04 didn't address.
- It would need its own hit-tester finer-grained than `HitTestService.HitTest`
  (which only finds the topmost *node*, not sub-regions of a node).

These are all solvable, but they push the design toward "two parallel input
owners fighting over the same region" — fragile and hard to reason about.

The cleaner shape is: **`ClassRectangle` is a plain C# class** (not a Control)
that holds:
- The class's identity and current state
- The geometry of its edit handles (resize corner, edge ports)
- Hit-test sub-regions for those handles

`DesignCanvasController` (a plain C# class) consults `ClassRectangle` during
pointer handling, and `CanvasRenderer` (extended for Design Mode) draws the
edit affordances as extra Skia calls layered into the same bitmap.

The **one exception** is inline editing: a `TextBox` overlay for editing
class/member names. Skia can't host a live text-input widget. This is a
single, small Avalonia `TextBox` positioned in screen space on top of
`GraphCanvas` at the inline-edit location. It is the only real Avalonia
Control added by Design Mode.

## Class structure

```csharp
public sealed class ClassRectangle
{
    // Identity
    public string ClassId { get; }
    public DesignGraph Graph { get; }

    // Position (world coordinates, same coordinate system as LayoutNode.X/Y)
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }

    // Interaction state
    public bool IsSelected { get; set; }
    public bool IsDragging { get; set; }
    public bool IsResizing { get; set; }
    public bool IsInlineEditingName { get; set; }
    public int? InlineEditingMemberIndex { get; set; }

    // Edit handle geometry (computed from X/Y/Width/Height)
    public Rect ResizeHandleBounds { get; }   // bottom-right corner, 12x12px
    public Rect LeftPortBounds { get; }       // left-center edge, 10px circle hit area
    public Rect RightPortBounds { get; }      // right-center edge, 10px circle hit area

    // Hit-testing (sub-region within the class)
    public ClassRectangleHitTest HitTest(SKPoint worldPoint);
}

public enum ClassRectangleHitTest
{
    None,
    Body,
    Header,
    Member,
    ResizeHandle,
    LeftPort,
    RightPort
}
```

## Visual rendering

### Shared geometry constants

Design Mode introduces two new shared constants that are used by both
`CanvasRenderer` (for drawing interactive member rows) and `DesignHitTestService`
(for hit-testing which row the cursor is over):

```csharp
internal static class DesignGeometry
{
    /// <summary>Height of each member row in Design Mode. Distinct from
    /// `CanvasRenderer.NodeMemberHeight` (which is private to Analyze Mode's
    /// static rendering) because Design Mode rows include interactive
    /// affordances (visibility prefix, delete button, alternating background)
    /// that may need different vertical spacing than Analyze Mode's flat text.
    /// </summary>
    public const float MemberRowHeight = 18f;

    /// <summary>Height of the class header (class name + stereotype badge).
    /// </summary>
    public const float HeaderHeight = 24f;
}
```

These constants are the single source of truth for member-row geometry in
Design Mode. `CanvasRenderer` reads them when drawing interactive rows;
`DesignHitTestService` reads them when computing which row the cursor is
over. They must agree, or hit-testing will be off by one row.

### What `CanvasRenderer` already does (unchanged)

`CanvasRenderer.DrawNodes(canvas, nodes, ...)` already paints:
- Class body (rounded rectangle background)
- Header (class name + stereotype badge)
- Member rows (up to `MaxMembersShownPerNode = 6`, with `+Name : Type` formatting)

This is reused as-is for both Analyze Mode and Design Mode.

### What `CanvasRenderer` needs to add for Design Mode

Extended methods (called only when `DesignModeController.CurrentMode == Design`):

```csharp
public static class CanvasRenderer
{
    // Existing
    public static void DrawNodes(SKCanvas canvas, List<GraphNode> nodes, ViewportState vp);

    // New for Design Mode (only called when in Design Mode)
    public static void DrawSelectionRing(SKCanvas canvas, ClassRectangle rect);
    public static void DrawResizeHandle(SKCanvas canvas, ClassRectangle rect);
    public static void DrawEdgePorts(SKCanvas canvas, ClassRectangle rect);
    public static void DrawEdgeCreationPreview(SKCanvas canvas, SKPoint from, SKPoint to);
    public static void DrawLassoRectangle(SKCanvas canvas, Rect bounds);
}
```

These are all Skia draw calls layered on top of the existing node rendering.
They are no-ops in Analyze Mode (the controller checks mode before calling
them).

### Member row rendering in Design Mode

`CanvasRenderer.DrawNodes` currently renders members as a flat truncated list.
For Design Mode, we need per-member hit regions and interactive affordances
(visibility prefix click target, × delete button). This is a real extension
to `CanvasRenderer`, not reuse:

```csharp
// New method for Design Mode member rendering
public static void DrawMemberRowInteractive(
    SKCanvas canvas,
    float x, float y, float width,
    DesignMember member,
    int memberIndex,
    bool isSelected,
    bool isHovered)
{
    // Alternating row background for readability
    if (memberIndex % 2 == 0)
        canvas.DrawRect(x, y, width, DesignGeometry.MemberRowHeight, alternatingBgPaint);

    // Visibility prefix (clickable: cycles through + / - / # / ~)
    DrawVisibilityPrefix(canvas, x, y, member.Visibility);

    // Name + type (double-clickable to edit)
    DrawMemberName(canvas, x + 20, y, member.Name);
    DrawMemberType(canvas, x + width - getTypeWidth(member.TypeName), y, member.TypeName);

    // × delete button (only on hover or selection)
    if (isHovered || isSelected)
        DrawDeleteButton(canvas, x + width - 12, y);
}
```

The rendering of member rows in Design Mode is **different** from Analyze
Mode (interactive affordances vs. flat text). Both go through `CanvasRenderer`,
but Design Mode uses a different draw method. This is documented as a real
extension to `CanvasRenderer`, not reuse.

## Edit handles

### Resize handle

Bottom-right corner, 12x12px triangle. Cursor changes to `SizeAll` on hover.
Dragging resizes the class. Min/max bounds enforced (min 180x42, max 600x1000).
Undoable.

### Edge ports

Left and right edges, at vertical center, 10px diameter hit area. Cursor
changes to `Cross` on hover. Dragging from a port starts edge creation.

Ports are only visible when:
- The class is selected, OR
- The mouse is within 20px of the class bounding box

When invisible, ports don't intercept pointer events (so the underlying
`GraphCanvas` can pan/zoom normally).

## Selection rendering

When `IsSelected = true`:
- Border width increases from 1px to 3px (drawn by `DrawSelectionRing`)
- Border color changes from default to accent
- Resize handle and edge ports become visible
- A subtle drop shadow appears (optional, for visual polish)

## Inline editing — the one real Avalonia Control

When `IsInlineEditingName = true`:
- A `TextBox` overlay is positioned in screen space on top of `GraphCanvas`
  at the inline-edit location (computed from class X/Y/Width + pan/zoom)
- The TextBox is pre-filled with the current name, all text selected
- The TextBox receives focus immediately (deferred via `Dispatcher.UIThread.Post`
  to wait for visual tree update)
- On Enter: validate (C# identifier rules), commit, exit edit mode
- On Escape: revert, exit edit mode
- On blur (clicking elsewhere): commit if valid, revert if invalid

This is the **only** place Design Mode introduces a real Avalonia `Control`.
It is a single, small `TextBox` positioned in screen space — not a per-class
control hierarchy.

## Input handling — pointer routing

`GraphCanvas` keeps ownership of all pointer events on the canvas. The mode
toggle changes what those handlers *do*, not who owns them:

```csharp
// In GraphCanvas.OnPointerPressed (modified for Design Mode support)
protected override void OnPointerPressed(PointerPressedEventArgs e)
{
    base.OnPointerPressed(e);
    var pos = e.GetPosition(this);
    var worldPos = ScreenToWorld(pos);

    if (DesignModeController.CurrentMode == AppMode.Design)
    {
        // Design Mode: hit-test sub-regions first. EVERY branch in this block
        // ends with `return` (or falls through to the final `return` after
        // Select). This is critical — without the early returns, control
        // would fall through into the Analyze Mode pan-start branch below
        // and start a pan on top of the Design Mode action (add class, start
        // drag, etc.), producing duplicate/broken behavior.
        var hit = DesignHitTest(worldPos);
        if (hit.Kind == ClassRectangleHitTest.ResizeHandle) { StartResize(hit.Rectangle); return; }
        if (hit.Kind == ClassRectangleHitTest.LeftPort || hit.Kind == ClassRectangleHitTest.RightPort) { StartEdgeCreation(hit); return; }
        if (hit.Kind == ClassRectangleHitTest.Header) { StartDrag(hit.Rectangle); return; }
        if (hit.Kind == ClassRectangleHitTest.None) { AddClassAt(worldPos); return; }
        // Body / Member: select
        Select(hit);
        return;
    }

    // Analyze Mode: existing behavior (pan, drag, select).
    // This branch is ONLY reached when CurrentMode != Design. The early
    // returns above guarantee that Design Mode never falls through here.
    if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) { /* pan or drag */ }
    // ...
}
```

This is a real edit to `GraphCanvas`, contradicting the earlier "what does
NOT change" list. Updated in doc 02: `GraphCanvas` pointer handlers DO change
(but rendering pipeline does not).

## Hit-testing — sub-regions

`HitTestService.HitTest(SKPoint, List<GraphNode>)` finds the topmost *node*
only. Design Mode needs finer-grained hit-testing (header vs. body vs.
resize-handle vs. port). This is a new component:

```csharp
internal static class DesignHitTestService
{
    public static DesignHitResult HitTest(
        SKPoint worldPos,
        List<ClassRectangle> rectangles)
    {
        // Reverse iterate (topmost first)
        for (int i = rectangles.Count - 1; i >= 0; i--)
        {
            var rect = rectangles[i];
            if (!worldPos.X.Between(rect.X, rect.X + rect.Width)) continue;
            if (!worldPos.Y.Between(rect.Y, rect.Y + rect.Height)) continue;

            // Check sub-regions in priority order
            if (rect.ResizeHandleBounds.Contains(worldPos)) return DesignHitResult.ResizeHandle(rect);
            if (rect.RightPortBounds.Contains(worldPos)) return DesignHitResult.RightPort(rect);
            if (rect.LeftPortBounds.Contains(worldPos)) return DesignHitResult.LeftPort(rect);

            // Header is top HeaderHeight pixels of class
            if (worldPos.Y < rect.Y + DesignGeometry.HeaderHeight) return DesignHitResult.Header(rect);

            // Member row hit (compute which member based on Y)
            int memberIndex = (int)((worldPos.Y - rect.Y - DesignGeometry.HeaderHeight) / DesignGeometry.MemberRowHeight);
            if (memberIndex < rect.Graph.Classes.First(c => c.Id == rect.ClassId).Members.Count)
                return DesignHitResult.Member(rect, memberIndex);

            return DesignHitResult.Body(rect);
        }
        return DesignHitResult.None();
    }
}
```

This is O(n) linear scan like `HitTestService`. For 50–100 classes this is
fine. Spatial pruning is a future optimization, not a current capability.

## Why this split

Two reasons:
1. **Reuse what works**: `CanvasRenderer.DrawNodes` already paints classes
   correctly. We don't want to duplicate that rendering logic. The
   interactive affordances are layered on top.
2. **Single source of truth for visual appearance**: the visual appearance
   of a class must be identical in both modes. Drawing it twice would drift
   over time. So `CanvasRenderer` is the single renderer; Design Mode adds
   new draw methods to it, not a parallel renderer.

## Relationship to existing code

- `GraphCanvas.RenderNow()` — unchanged. Still the single render entry point.
- `CanvasRenderer.DrawNodes(...)` — unchanged. Still paints class bodies.
- `CanvasRenderer` (new methods) — extended with Design Mode draw methods.
- `HitTestService` — unchanged. Still used for Analyze Mode node hit-testing.
- `DesignHitTestService` — new. Used for Design Mode sub-region hit-testing.
- `GraphCanvas` pointer handlers — extended with mode-aware branches.
- `LayoutEngine` and `GraphLayoutCoordinator` — unchanged. Design Mode
  produces a `TypeGraph` and feeds it through the same pipeline.
- The export pipeline — unchanged. Operates on `TypeGraph`.

## Risks

- **Pointer event coordination**: `GraphCanvas` owns all canvas pointer
  events. Mode-aware branches must be added to its existing handlers
  (`OnPointerPressed`, `OnPointerMoved`, `OnPointerReleased`). New pointer
  handlers in a separate control would conflict. Mitigation: single owner,
  mode-switched behavior.
- **Inline edit TextBox focus**: must receive focus immediately after
  visual tree update. Use `Dispatcher.UIThread.Post(() => textBox.Focus())`
  to defer focus.
- **Performance with many classes**: `DesignHitTestService` is O(n) linear
  scan, same as `HitTestService`. Acceptable for 50–100 classes. Spatial
  pruning is a future optimization, not a current capability.
- **Drag during inline edit**: must be impossible. Disable drag-start branches
  while `IsInlineEditingName == true`.
