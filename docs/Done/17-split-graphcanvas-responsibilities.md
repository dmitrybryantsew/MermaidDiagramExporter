# Step 17 — Split `GraphCanvas` responsibilities (extract, don't rewrite)

## Problem (in plain language)

`GraphCanvas` currently handles several distinct jobs at once: rendering
(drawing nodes/edges/clusters), input handling (pan, zoom, drag), hit-testing
(figuring out what's under the mouse), search-result highlighting, and
edge-visibility filtering. At roughly 500 lines, it's still readable today,
but it's grown to do too much, making it harder to test pieces of it in
isolation and harder to reason about changes (e.g. Steps 10–12 already
required touching rendering-adjacent logic inside this same large class).

## Why this step must be done as **extraction**, not a rewrite

The instruction below is deliberately structured as "move existing code into
a new home, with minimal changes to the code itself" — not "redesign how
this works." A less-capable model attempting to redesign input handling or
rendering while also splitting files is very likely to introduce subtle
bugs. Pure extraction (cut code from one place, paste into a new class,
wire it back together with the same behavior) is much safer and is exactly
what's being asked for here.

## What to find

- `grep -rln "class GraphCanvas" --include=*.cs .` — open the full file and
  read it top to bottom once, end to end, before changing anything. You
  need a complete mental model of what's in there before extracting pieces.
- As you read, categorize every method and field into one of:
  1. **Rendering** — anything that draws to an `SKCanvas` (the `DrawNodes`,
     `DrawEdges`, cluster background drawing, and now the cached-picture
     logic from Steps 10–12).
  2. **Input handling** — `PointerPressed`, `PointerMoved`, `PointerReleased`,
     `PointerWheelChanged` handlers; pan/zoom/drag state fields (`_zoom`,
     `_panX`, `_panY`, drag-tracking fields from Step 05/12).
  3. **Hit-testing** — logic that converts a pointer position into "which
     node/edge is under this point," if such logic exists as identifiable
     methods (it might currently be inlined inside the pointer event
     handlers rather than being separate methods — note this if so, since
     it affects how cleanly it can be extracted).
  4. **Search highlighting** — whatever tracks "the current search result
     set" and adjusts rendering/visibility based on it.
  5. **Edge visibility filtering** — whatever decides which edges are shown
     vs. hidden (e.g. based on focus mode, or a user toggle).

## The fix

Create new classes for **rendering** and **hit-testing** first, since the
review specifically names `CanvasRenderer` and `HitTestService` as
suggestions, and these tend to be the cleanest to extract (input handling is
more tightly coupled to Avalonia's control lifecycle and is reasonable to
leave in `GraphCanvas` itself, or extract later as a follow-up if this step
goes well — don't feel obligated to extract all three named classes in one
pass if `CanvasInputController` turns out to be much more tangled than
expected; partial progress with verified correctness beats a complete but
broken split).

### 17a. Extract `CanvasRenderer`
1. Create a new class `CanvasRenderer` in a new file `CanvasRenderer.cs` in
   the same project/folder as `GraphCanvas`.
2. Move every rendering-only method (category 1 above) into this new class,
   as closely to verbatim as possible.
3. Any field that rendering methods read but don't belong exclusively to
   rendering (e.g. `_zoom`, `_panX`, `_panY`, the node/edge collections
   themselves) needs to be passed into `CanvasRenderer`'s methods as
   parameters, or `CanvasRenderer` needs a constructor that takes a
   reference to whatever owns that state (e.g. a reference back to
   `GraphCanvas`, or — cleaner — a small data-holding parameter object
   bundling "current viewport state" together, if one doesn't already
   exist). Choose whichever requires the least restructuring of the
   existing field access patterns, since minimal change is the priority
   here, not the most "elegant" possible design.
4. In `GraphCanvas`, replace the moved method bodies with calls into a
   `CanvasRenderer` instance (constructed once, stored as a field).
5. `dotnet build`. Fix every compile error this produces one at a time —
   there will likely be several, since moved methods may reference private
   fields of `GraphCanvas` that need to become accessible (passed as
   parameters, or exposed via `internal`/public properties) to the new
   class. Resolve each by passing data explicitly rather than widening
   access more than necessary.
6. `dotnet test`.

### 17b. Extract `HitTestService` (only if hit-testing exists as identifiable, separable logic)
1. If you found in your initial read-through that hit-testing logic is
   cleanly separable (i.e. it's already somewhat self-contained, e.g. a
   method like `GraphNode? FindNodeAt(Point position)`), extract it the
   same way as 17a: new file, move the method(s), pass in whatever state
   (node positions, current zoom/pan for coordinate transforms) is needed
   as parameters.
2. If hit-testing is deeply inlined inside pointer event handlers with no
   clean method boundary, do NOT force an extraction in this step — note
   this in your summary as a finding (e.g. "hit-testing logic is currently
   inline within PointerPressed/PointerMoved and would require behavior
   changes, not just relocation, to extract cleanly — recommend as a
   separate follow-up step with more design input") rather than attempting
   a risky inline-logic extraction blind.
3. `dotnet build`. `dotnet test`.

## Constraints

- Do not change rendering or hit-testing *behavior* — every extracted
  method should produce identical output to before, just from a different
  class.
- Do not extract input handling (`PointerPressed`/`PointerMoved`/
  `PointerReleased`) in this step — leave `CanvasInputController` as a
  possible future step; input handling is the most state-entangled of the
  three and riskiest to split blind.
- If at any point you find that extracting a piece requires also changing
  its behavior (not just its location) to make the split work, STOP and
  report this rather than proceeding — that's a sign the boundary you
  picked doesn't cleanly exist in the current code, and forcing it through
  risks a real bug, not just a refactor.

## Verification

1. `dotnet build` after each of 17a and 17b.
2. `dotnet test` after each.
3. If you can run the GUI, visually confirm rendering and click-to-select
   (hit-testing) behavior is unchanged after the split. If you cannot run
   the GUI, state this limitation explicitly.

## Done when

- `CanvasRenderer` exists and owns rendering logic previously inline in
  `GraphCanvas`.
- `HitTestService` exists and owns hit-testing logic, **if and only if**
  that logic was cleanly separable without behavior changes; otherwise this
  part is explicitly deferred and reported, not forced.
- `GraphCanvas` is smaller and delegates to these new classes.
- No behavior changed.
- Build and tests pass.
