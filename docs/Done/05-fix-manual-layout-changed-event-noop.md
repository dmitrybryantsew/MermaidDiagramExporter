# Step 05 — Stop `ManualLayoutChanged` firing when nothing actually moved

## Problem (in plain language)

When a user clicks and releases on a node without actually dragging it
anywhere, the event that signals "the user manually repositioned a node" —
`ManualLayoutChanged` — still fires, on every `PointerReleased`, even though
the node's position didn't change. Anything listening to this event (likely
something that marks the project as "dirty" / needing a settings save) will
think a change happened when it didn't. This causes unnecessary saves and
possibly unnecessary "unsaved changes" indicators in the UI.

## What to find

- `grep -rn "ManualLayoutChanged" --include=*.cs .`
- `grep -rn "PointerReleased" --include=*.cs . | grep -i graphcanvas`
  (or search inside the `GraphCanvas` file directly once found)
- Find the event declaration (`event EventHandler... ManualLayoutChanged` or
  similar) and every place it's raised (`ManualLayoutChanged?.Invoke(...)` or
  `ManualLayoutChanged(...)`).
- Find the `PointerReleased` handler in `GraphCanvas` (or wherever node
  dragging is implemented — confirm the actual class, it may not be
  `GraphCanvas` itself if dragging logic was extracted).

## The fix

You need to compare the node's position at drag-start to its position at
drag-release, and only raise `ManualLayoutChanged` if they differ.

1. Find where drag starts (likely a `PointerPressed` handler, or a
   `StartDrag` / similar method) and confirm whether the node's starting
   position is already being recorded somewhere (a field like
   `_dragStartPosition`, `_dragStartX`/`_dragStartY`, or similar). If it is,
   use that. If it is not currently recorded, you will need to add a field
   to store it at drag-start.
2. In the `PointerReleased` handler (or drag-end logic), before raising
   `ManualLayoutChanged`, compare the node's current position against the
   recorded drag-start position.
3. Only raise the event if the position actually changed. Use a small
   epsilon comparison for floating point positions rather than exact
   equality, e.g.:
   ```csharp
   const float MovedEpsilon = 0.5f; // pixels; adjust if the codebase already
                                     // has a similar epsilon constant elsewhere —
                                     // search for one (e.g. "0.01f" mentioned
                                     // in the review) and reuse it if it fits
                                     // this context, rather than introducing
                                     // a second unrelated epsilon constant.
   bool moved = Math.Abs(currentX - startX) > MovedEpsilon
             || Math.Abs(currentY - startY) > MovedEpsilon;
   if (moved)
   {
       // existing code that raises ManualLayoutChanged goes here, unchanged
   }
   ```
4. Do not remove or change any of the existing logic that actually persists
   the new position when it *did* move — only gate the event-raising (and
   any associated save) behind the "did it actually move" check.

## Constraints

- Do not change what happens when a real drag occurs — only add a guard for
  the no-op case.
- Do not change the event's signature or its existing subscribers.
- If you discover the codebase already has a similarly-named epsilon
  constant (the review mentions `0.01f` is used somewhere, possibly for a
  different purpose like layout convergence) — read its usage context
  first. Don't reuse it blindly if it's tuned for a different purpose (e.g.
  layout algorithm convergence vs. human pointer-drag tolerance); these are
  different concerns and may reasonably need different epsilon values.

## Verification

1. `dotnet build`.
2. `dotnet test` — run any existing drag/manual-layout related tests.
3. Manual reasoning check: trace through the code once with a "click,
   don't move, release" sequence and confirm `ManualLayoutChanged` does NOT
   fire. Then trace through a "click, move 10px, release" sequence and
   confirm it DOES fire. You can do this by reading the code path, you don't
   need to run the GUI.

## Done when

- `ManualLayoutChanged` only fires when the node's position genuinely
  changed between drag-start and drag-release.
- Build and tests pass.
