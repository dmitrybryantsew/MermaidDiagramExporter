# Step 10 — Cache `SKPaint` objects instead of recreating them every frame

## Problem (in plain language)

Every time the canvas redraws (which can be every frame during pan/zoom/
drag), the drawing code creates brand new `SKPaint` objects for every node
and every edge — via `using var paint = new SKPaint(...)`. With ~200 nodes,
that's 600+ allocations per frame, every frame, causing garbage collection
pressure and visible frame drops. `SKPaint` objects that represent a fixed
color/stroke-width combination should be created once and reused, not
recreated on every draw call.

This is described as the **highest-impact, lowest-effort fix** in the
review — do this one carefully and correctly, since it's the headline
performance win.

## What to find

- Class: `GraphCanvas`
- Methods: `DrawNodes()` and `DrawEdges()` (names per the review — confirm
  actual names by searching: `grep -n "void Draw" <path-to-GraphCanvas.cs>`
  once located via `grep -rln "class GraphCanvas" --include=*.cs .`)
- Inside these methods, find every occurrence of `new SKPaint` (search:
  `grep -n "new SKPaint" <path>`). For each one, note:
  - What `Color` it's constructed with (a fixed constant? something computed
    per-node, like a hover-highlight color that depends on state?).
  - What other properties are set (`StrokeWidth`, `Style`, `IsAntialias`,
    `TextSize` if it's used for text, etc.).
  - Whether it's disposed via `using` (it almost certainly is, per the
    review's description).

## Why this needs care, not just a mechanical find-replace

Not every `SKPaint` construction can simply be hoisted to a static field
unchanged. You need to sort each one into one of two buckets:

**Bucket 1 — Fixed configuration, safe to cache as a static/instance field.**
If the paint's color, stroke width, and style never change at runtime (e.g.
"the default node border color is always this exact gray"), it can become a
single reusable field created once.

**Bucket 2 — Depends on per-node/per-edge runtime state** (e.g. a node's
fill color changes when hovered/selected, or an edge's color depends on its
`Kind`). These cannot be a single static `SKPaint` — but they likely only
vary across a **small, finite set of states** (e.g. "default," "hovered,"
"selected," and maybe 3–4 edge `Kind` values). For these, create one cached
`SKPaint` **per distinct state value**, not one per node/edge instance. E.g.
if there are 4 possible edge `Kind` values, you need (at most) 4 cached
`SKPaint` objects for edges, looked up by kind — not a new one per edge.

## The fix

1. At the top of the `GraphCanvas` class (near other fields), declare
   reusable paint fields/dictionaries, e.g.:
   ```csharp
   private static readonly SKPaint NodeFillPaint = new()
   {
       Color = /* whatever the current default fill color constant is */,
       Style = SKPaintStyle.Fill,
       IsAntialias = true,
   };

   private static readonly SKPaint NodeBorderPaint = new()
   {
       Color = /* existing border color */,
       Style = SKPaintStyle.Stroke,
       StrokeWidth = /* existing stroke width */,
       IsAntialias = true,
   };

   // For state-dependent paints, key by the relevant enum/state:
   private static readonly Dictionary<EdgeKind, SKPaint> EdgePaintsByKind = new()
   {
       // populate using the actual EdgeKind enum values and their
       // corresponding current colors — read these from the existing
       // per-edge SKPaint construction logic, do not invent new colors.
   };
   ```
   Use the **actual** field/enum/color names from the real code — the
   snippet above is illustrative structure, not literal code to paste.
2. Replace each `using var paint = new SKPaint(...)` call site with a
   reference to the appropriate cached field or dictionary lookup, removing
   the `using` (since these paints are no longer owned/disposed per-draw —
   they live for the lifetime of the class/process).
3. For any paint whose color depends on **continuously-variable** runtime
   state (e.g. an opacity that fades smoothly, or a color that's
   interpolated, rather than a small finite set of discrete states) — this
   is the one legitimate case where you cannot fully eliminate per-frame
   construction. In that case, instead of `new SKPaint(...)` every time,
   create the paint object **once** and **mutate its properties** each frame
   (e.g. `paint.Color = computedColor;`) rather than allocating a new object.
   `SKPaint` properties are mutable — reuse the object, just update the
   field that actually varies.
4. Make sure none of the cached static paints are disposed anywhere (since
   `using` blocks are being removed). If `GraphCanvas` implements
   `IDisposable` and has a `Dispose()` method, check whether it currently
   disposes any of these — if it currently disposes per-call paints (it
   shouldn't, if they were all `using`-scoped per-draw), nothing changes
   there. If you introduce instance-level (not static) cached paints that
   should be disposed when the canvas itself is disposed, add that disposal
   to the existing `Dispose()` method — but prefer `static readonly` for
   anything that's truly fixed-forever, since canvas instances may be
   short-lived while these colors are not.

## Constraints

- Do not change any actual color, stroke width, or visual appearance — this
  is purely about reusing objects, not changing what gets drawn.
- Do not cache per-node-instance state in these paint objects (e.g. don't
  try to "cache by node ID" — that defeats the purpose and leaks memory as
  nodes come and go). Cache by **state/configuration**, not by **identity**.
- If you find a paint that's used for text rendering (e.g. node labels) with
  a `Typeface`/`SKFont` attached, be extra careful — fonts can also benefit
  from caching for the same reason, but confirm whether `SKFont`/`SKTypeface`
  objects are also being recreated per-call nearby, and apply the same
  caching principle to those too if so, since they have similar allocation
  cost.

## Verification

1. `dotnet build`.
2. `dotnet test`.
3. Since this is rendering code with no automated visual test (per the
   review's testing gaps section), do a careful manual code-read pass:
   confirm every `new SKPaint(...)` that used to be constructed inside the
   per-node/per-edge draw loop is now either (a) a cached field/dictionary
   lookup, or (b) a single pre-existing object whose properties are mutated,
   not reconstructed.
4. If you have any way to actually run the GUI in this environment, visually
   confirm node and edge colors look identical to before your change (no
   color shifted or disappeared). If you cannot run the GUI here, state
   clearly in your summary that this step's visual correctness was verified
   by code review only, not by running the app, so a human can do a final
   visual check later.

## Done when

- No `SKPaint` object is constructed inside the per-node or per-edge draw
  loop body itself; all such paints are pre-created and reused.
- Visual appearance is unchanged.
- Build and tests pass.
