# Step 11 — Cache static visual content with `SKPictureRecorder`

## Problem (in plain language)

Every redraw currently re-draws everything from scratch: every node, every
edge, every namespace background — even the parts of the scene that haven't
changed since the last frame (e.g. while only panning, or while only one
node is being dragged). `SKPictureRecorder` lets you record a sequence of
drawing commands once into an `SKPicture`, then "replay" that recorded
picture very cheaply on subsequent frames, instead of re-issuing all the
original draw calls. For content that rarely changes (namespace background
rectangles, edges between nodes that haven't moved), this can cut per-frame
work dramatically.

**Do this step after Step 10** (paint caching) — having stable, cached paint
objects makes the recorded picture's draw calls cheaper and avoids
recording calls that reference soon-to-be-replaced paint instances.

## What to find

- Class: `GraphCanvas`.
- The main render/draw entry point — likely something like `OnPaintSurface`,
  `Render`, or similar (search: `grep -n "SKCanvas" <path-to-GraphCanvas.cs>`
  to find where an `SKCanvas` parameter is received, which marks the
  top-level render method).
- The methods that draw "static" content: namespace background rectangles
  (search for whatever draws namespace cluster backgrounds — possibly inside
  `DrawNodes` itself, or a separate method like `DrawClusterBackgrounds`),
  and edges between nodes that are **not currently being dragged**.
- Confirm what triggers a redraw currently (search for `InvalidateVisual()`
  calls within `GraphCanvas` and its callers) — you need to know every
  reason a redraw happens, so you know when the cached picture must be
  invalidated/rebuilt vs. when it can be safely reused.

## The fix

1. Add a field to hold a cached recorded picture and a flag/version number
   indicating whether it's stale:
   ```csharp
   private SKPicture? _staticContentPicture;
   private bool _staticContentDirty = true;
   ```
2. Identify exactly which draw calls represent "static" content for a given
   frame — i.e. content that does not depend on transient interaction state
   like current drag position, current hover, or current pan/zoom (pan/zoom
   should be applied as a canvas transform around the replay, not baked
   into the recording — see below).
3. Write a method that records this static content once:
   ```csharp
   private SKPicture RecordStaticContent()
   {
       using var recorder = new SKPictureRecorder();
       // The recording bounds should be the full content bounds (e.g. the
       // graph's full extent), not the current viewport — since pan/zoom
       // will be applied at replay time via canvas transform, not by
       // re-recording.
       var canvas = recorder.BeginRecording(fullContentBounds);
       // existing draw calls for static content (cluster backgrounds, edges
       // between non-dragged nodes) go here, using the cached paints from
       // Step 10
       return recorder.EndRecording();
   }
   ```
4. In the main render method, replace direct draw calls for static content
   with:
   ```csharp
   if (_staticContentDirty || _staticContentPicture == null)
   {
       _staticContentPicture?.Dispose();
       _staticContentPicture = RecordStaticContent();
       _staticContentDirty = false;
   }
   canvas.DrawPicture(_staticContentPicture);
   ```
   Apply pan/zoom as an `SKMatrix`/canvas transform (`canvas.Translate(...)`,
   `canvas.Scale(...)`, or `canvas.SetMatrix(...)`) **before** calling
   `DrawPicture`, exactly as the existing code already applies pan/zoom
   before its other draw calls — find that existing transform logic and
   make sure the recorded picture is drawn within the same transformed
   context, not before/after it incorrectly.
5. Mark `_staticContentDirty = true` whenever something that affects static
   content actually changes: a node is added/removed, a node's position
   changes (other than transient drag preview — see Step 12, which will
   refine this further), an edge is added/removed, cluster membership
   changes, or theme/color settings change. Find every place in the
   codebase that currently triggers `InvalidateVisual()` for these reasons
   and add `_staticContentDirty = true` alongside each one. Do NOT mark it
   dirty for pure pan/zoom/hover changes — those should use the cached
   picture as-is with just a different transform applied.
6. Dispose `_staticContentPicture` in `GraphCanvas`'s existing `Dispose()`
   method (if one exists — if `GraphCanvas` doesn't currently implement
   `IDisposable`, you'll need to add it, and make sure it's actually called
   from wherever `GraphCanvas` instances are torn down, e.g. when a project
   is closed or the window closes).

## Constraints

- This step deliberately does NOT yet handle the "only re-render the
  dragged node" optimization — that's Step 12. This step only handles
  content that's static *between drag operations*, not what happens *during*
  a drag (during a drag, for now, the dragged node/edges can still be drawn
  on top of the cached static picture as a normal direct draw call — that
  part doesn't change yet).
- Be conservative about what counts as "static." If you're unsure whether
  something changes often enough to disqualify it from the cached picture,
  leave it out of the cached picture and keep drawing it directly — a
  correctness-over-performance bias is correct here. An incorrectly-cached
  picture that shows stale content is a worse bug than slightly
  under-optimized performance.
- Do not change visual output. After this step, a static screenshot of the
  canvas before and after should be pixel-identical (only frame timing
  should improve).

## Verification

1. `dotnet build`.
2. `dotnet test`.
3. Manual code-read: trace every code path that calls `InvalidateVisual()`
   on the canvas and confirm each one either (a) correctly sets
   `_staticContentDirty = true` if it affects static content, or (b)
   correctly does NOT set it if it's a transient/transform-only change
   (pan, zoom, hover).
4. As with Step 10, note in your summary whether you were able to actually
   run the GUI to visually confirm no regression, or whether this was
   verified by code review only.

## Done when

- Static visual content (cluster backgrounds, unmoved edges) is recorded
  once into an `SKPicture` and replayed on subsequent frames rather than
  redrawn from scratch.
- The cache is correctly invalidated whenever underlying graph structure
  actually changes.
- Visual output is unchanged.
- Build and tests pass.
