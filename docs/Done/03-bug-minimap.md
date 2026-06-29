# 03 — Bug Fix: Minimap Viewport Rectangle Doesn't Update on Zoom; Save Buttons Overlap Minimap

There are two unrelated bugs bundled in this report. Treat them as two
separate fixes; they touch different files and have nothing to do with each
other beyond both involving the minimap visually.

---

## Bug 3A — Mouse-wheel zoom doesn't update the minimap's viewport rectangle (but panning does)

### Status: root cause fully identified from source. Precise, low-risk fix.

### Symptom (as reported)

Panning the main canvas correctly moves the yellow viewport rectangle on the
minimap. Zooming (scroll wheel) changes the actual view, but the minimap's
viewport rectangle does not resize/reposition to reflect the new zoom level
— it stays where it was.

### Root cause

`GraphCanvas.cs` has **three** places that change `_zoom`/`_panX`/`_panY`,
and a `NotifyViewportChanged()` helper that fires the `ViewportChanged` event
which `MainWindow.OnViewportChanged` forwards to
`MinimapView.UpdateViewport(...)`:

```csharp
private void NotifyViewportChanged()
{
    ViewportChanged?.Invoke(_zoom, _panX, _panY, (float)Bounds.Width, (float)Bounds.Height);
}
```

Checking all three call sites that mutate zoom/pan:

| Method | Mutates `_zoom`/`_panX`/`_panY` | Calls `NotifyViewportChanged()`? |
|---|---|---|
| `FitToScreen()` | yes (via `RecalculateLayout`) | ✅ yes (explicit call at the end) |
| `ZoomBy(float factor)` (the **button-driven** zoom-in/zoom-out controls, if present in the toolbar) | yes | ✅ yes (explicit call at the end) |
| `OnPointerWheelChanged(PointerWheelEventArgs e)` (the **mouse-scroll-wheel** zoom handler) | yes | ❌ **no — missing** |
| pan-drag handling in `OnPointerMoved`/pointer-move branch (around the `_panX += dx; _panY += dy;` lines) | yes | ✅ yes (explicit call) |

`OnPointerWheelChanged` is:

```csharp
protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
{
    base.OnPointerWheelChanged(e);
    float zoomDelta = (float)(e.Delta.Y * 0.12f);
    float newZoom = Math.Clamp(_zoom * (1 + zoomDelta), 0.05f, 5.0f);

    var cursorPos = e.GetPosition(this);
    float worldX = (float)(cursorPos.X - _panX) / _zoom;
    float worldY = (float)(cursorPos.Y - _panY) / _zoom;
    _panX = (float)cursorPos.X - worldX * newZoom;
    _panY = (float)cursorPos.Y - worldY * newZoom;
    _zoom = newZoom;
    e.Handled = true;
    Invalidate();   // <-- redraws the main canvas, but never tells the minimap anything changed
}
```

This explains the exact reported behavior precisely: **panning works**
(pan-drag handler calls `NotifyViewportChanged()`), **zoom buttons would work
if used** (`ZoomBy` calls it), but **scroll-wheel zoom does not** (this
handler is missing the call). Since scroll wheel is almost certainly how
most users zoom day-to-day, it reads as "zoom never updates the minimap."

### The fix

Add the missing notification call. One line:

```csharp
protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
{
    base.OnPointerWheelChanged(e);
    float zoomDelta = (float)(e.Delta.Y * 0.12f);
    float newZoom = Math.Clamp(_zoom * (1 + zoomDelta), 0.05f, 5.0f);

    var cursorPos = e.GetPosition(this);
    float worldX = (float)(cursorPos.X - _panX) / _zoom;
    float worldY = (float)(cursorPos.Y - _panY) / _zoom;
    _panX = (float)cursorPos.X - worldX * newZoom;
    _panY = (float)cursorPos.Y - worldY * newZoom;
    _zoom = newZoom;
    e.Handled = true;
    NotifyViewportChanged();   // <-- ADD THIS LINE
    Invalidate();
}
```

### Why this is the whole fix (no other hidden cause)

`MinimapControl.UpdateViewport`/`UpdateViewportRect` math (in
`MinimapControl.axaml.cs`) is correct and self-contained — it takes whatever
`zoom`/`panX`/`panY`/`viewW`/`viewH` it's given and computes the rectangle
correctly from `_graphBounds`. It has no zoom-specific special-casing that
could be separately broken. The bug is purely "the event never fires for
this one interaction," not "the event fires with wrong data." Confirm this
during implementation by temporarily adding a `Console.WriteLine`/debug log
inside `NotifyViewportChanged` and inside `MinimapControl.UpdateViewport`
before applying the fix, to see the call simply never happens on scroll —
then remove the temporary logging once confirmed and fixed.

### Verification steps

1. Load any project with a few nodes spread out enough to need scrolling at
   100% zoom.
2. Scroll-wheel zoom in/out repeatedly. Confirm the yellow rectangle on the
   minimap resizes (smaller rectangle = more zoomed in) in real time as you
   scroll, without needing to pan afterward to "wake it up."
3. Confirm pan still works (regression check — should be unaffected, it
   already worked).
4. If toolbar zoom +/- buttons exist and call `ZoomBy`, confirm those still
   work too (regression check — they already called `NotifyViewportChanged`,
   shouldn't change, but worth a quick re-confirm since you're editing the
   same file).
5. Confirm clicking on the minimap still correctly jumps the main canvas
   pan (`OnMinimapPointerPressed` → `ViewportJumpRequested` →
   `OnMinimapViewportJump` → `GraphCanvasView.SetPan`) — unrelated code
   path, but cheap to re-verify since it's adjacent.

---

## Bug 3B — Save buttons (Save .mmd / Save .md / Copy Mermaid / Open Live Editor) overlap the minimap

### Status: cause identified at the architecture level; exact fix requires editing `MainWindow.axaml`, which was **not included** in the provided code dump (only `.axaml.cs` code-behind files were included — the dump tool that generated `csharp_project_context.txt` appears to skip `.axaml` markup files). The instructions below are precise about *what* to change; the implementing agent must open the real `MainWindow.axaml` in the actual repo to apply them, since this plan's author could not view its current content.

### Symptom (as reported, confirmed visually in the provided screenshot)

In the screenshot, the "Save .mmd" / "Save .md" / "Copy Mermaid" / "Open Live
Editor" buttons render as an orange/red block sitting directly on top of the
bottom-right corner of the minimap, visually overlapping it.

### Root cause (architectural — confirmed from code-behind, markup not available)

Searching all code-behind (`.cs`) files for any runtime repositioning logic
for either the minimap or these save buttons turns up **nothing** — there is
no C# code that sets `Canvas.Left`/`Canvas.Top`/`Margin`/`HorizontalAlignment`
for `MinimapView` or for the save-button panel at runtime. (Compare this to
`MinimapControl.axaml.cs`'s `UpdateViewportRect`, which *does* programmatically
position the viewport-rectangle indicator **inside** the minimap control —
that part is fine and unrelated.) Since nothing in code-behind moves these
elements, their screen position is determined **entirely by static XAML
layout** in `MainWindow.axaml` — most likely both are anchored to the same
corner (e.g. both `HorizontalAlignment="Right" VerticalAlignment="Bottom"`)
inside the same parent `Grid`/`Canvas`/`Panel` without one of them having a
`Margin` offset large enough to clear the other, and/or both are children of
a `Grid` with overlapping `Grid.Row`/`Grid.Column`/`Grid.RowSpan` placement
that was fine before the minimap was added (or before the button row grew to
include more buttons) but was never revisited.

### The fix (apply directly in `MainWindow.axaml`)

Since the actual current markup isn't available to this plan's author, give
the implementing agent the **exact layout outcome to target** rather than a
literal diff, and have them adapt it to whatever container structure
actually exists:

1. **Open `MainWindow.axaml`** and locate the container that holds
   `MinimapView` and the container that holds the export button row (Save
   PNG / Save .mmd / Save .md / Copy Mermaid / Open Live Editor — likely
   named something like `ExportButtonsPanel` or similar, find by searching
   for `x:Name="MinimapView"` and the names of buttons whose `Click` handlers
   are `OnSaveMmd`/`OnSaveMd`/`OnCopyMermaid`/`OnOpenLiveEditor` per
   `MainWindow.axaml.cs`).
2. **Pick non-overlapping corners.** The minimap should own one bottom
   corner (it currently appears bottom-right in the screenshot); move the
   export button row to **bottom-left**, or to a **top corner**, or stack it
   **above** the minimap with enough margin to clear the minimap's full
   height — any of these is acceptable, but bottom-left is the conventional
   choice (matches most map/diagram tools: minimap bottom-right, action
   buttons elsewhere) and is the recommended default absent other UI
   constraints.
3. If both must stay in the same general area (e.g. design constraints
   require both bottom-right), then instead **stack them vertically with
   explicit spacing**: wrap both in a single `StackPanel` (`Orientation="Vertical"`)
   anchored to the same corner, with the button row *above* the minimap and
   a `Margin="0,0,0,8"` (or similar) on the button row to create a visible
   gap — this guarantees no overlap regardless of either element's exact
   pixel size, which is more robust than hand-tuning two independent
   absolute margins to "just barely" not overlap (that approach breaks again
   the next time someone adds a button to the row or resizes the minimap).
4. **Set `ZIndex` defensively even after fixing the layout.** Give the
   export button panel a higher `ZIndex` than the minimap (e.g.
   `ZIndex="10"` on the button panel, default/`0` on the minimap) so that if
   a future change to window size, button count, or minimap size
   re-introduces a small overlap, the buttons remain clickable (on top)
   rather than the minimap silently eating click events meant for a button
   underneath it. This is a defensive measure, not a substitute for fixing
   the actual layout per steps 2–3.
5. **Re-test at multiple window sizes**, including the smallest size the
   window allows to be resized to (check `MainWindow.axaml`'s
   `MinWidth`/`MinHeight` or `SizeToContent` settings) — overlap bugs like
   this often only appear at certain aspect ratios, so confirming the fix at
   default size only is not sufficient.

### Why this can't be fully specified without the actual XAML

This plan's author worked from the C# code-behind dump only (per the
`csharp_project_context.txt` provided); `.axaml` markup files were not part
of that dump (confirmed: grepping the dump for any `.axaml` file path other
than `.axaml.cs` returns nothing). Producing a literal before/after XAML diff
here would mean guessing at container names, existing `Grid.Row`/`Grid.Column`
definitions, and existing `Margin` values — guessing wrong would waste the
implementing agent's time more than it would save. The instructions above
are deliberately precise about the *target outcome and the robust technique*
(corner reassignment, or stacked panel with explicit gap, plus defensive
`ZIndex`) so that whoever has the actual file open can apply them directly
in under a few minutes.

### Verification steps

1. Visually confirm no overlap at default window size.
2. Resize the window smaller (down to its minimum allowed size) and larger;
   confirm no overlap appears at any size in between — drag-resize slowly
   and watch both elements, don't just check the two extremes.
3. Click each of the previously-obscured buttons (Save .mmd, Save .md, Copy
   Mermaid, Open Live Editor) and confirm each correctly triggers its
   handler (`OnSaveMmd`, `OnSaveMd`, the copy handler, `OnOpenLiveEditor`)
   rather than the click being intercepted by the minimap's
   `OnMinimapPointerPressed` (which would incorrectly pan the main view
   instead of saving/copying).
4. Click on the minimap itself near where the buttons used to overlap it;
   confirm it still correctly jumps the main view's pan (regression check
   for the `ZIndex`/repositioning change not having accidentally made the
   minimap unclickable in that region).
