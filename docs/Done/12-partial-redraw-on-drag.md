# Step 12 — Implement partial redraw during node drag

## Problem (in plain language)

While a user is dragging a node, the entire canvas (all 200+ nodes and
edges) is redrawn on every pointer-move event, even though only the dragged
node (and the edges connected to it) actually moved. There is a comment
already in the codebase acknowledging the intended optimization
("During drag: Only re-render the dragged node/cluster") but it was never
implemented. For large graphs, this makes dragging feel sluggish.

**Do this step after Step 11.** Step 11 already separated "static content"
(cached picture) from "everything else" (direct draw calls). This step
builds on that separation: during an active drag, we want to draw the
cached static picture (unchanged, cheap) plus only the dragged node and its
connected edges (in their live, moving position) on top — not redraw every
other node/edge from scratch.

## What to find

- The existing comment mentioned in the review: search
  `grep -rn "Only re-render the dragged" --include=*.cs .` (or similar
  wording — the exact comment text may not match exactly, search loosely
  with `grep -rn "dragged node" --include=*.cs .` and
  `grep -rn "dragged cluster" --include=*.cs .` if the first search comes up
  empty).
- The `PointerMoved` handler in `GraphCanvas` (or wherever drag-move logic
  lives) — find where the dragged node's position is updated during a drag.
- The main render method again (same one touched in Step 11).
- A way to know "which node IDs are connected to the dragged node by an
  edge" — search for existing adjacency lookups (e.g. something in
  `TypeGraph` or `SymbolIndex` that already answers "what edges touch node
  X" in O(1) or O(degree) time, per the review's mention of inverted
  indices in `SymbolIndex`). Reuse an existing lookup rather than writing a
  new linear scan over all edges if one already exists.

## The fix

1. Add a field tracking the currently-dragged node (if one doesn't already
   exist — there's likely already something like `_draggedNode` or
   `GraphNode? _hoveredNode` mentioned in the review for hover; confirm
   whether an equivalent drag-tracking field already exists before adding a
   new one).
2. In the main render method, branch behavior based on whether a drag is
   currently active:
   ```csharp
   // Always draw the cached static picture first (from Step 11), with the
   // current pan/zoom transform applied, exactly as before.
   canvas.DrawPicture(_staticContentPicture);

   if (_draggedNode != null)
   {
       // Drag in progress: draw only the dragged node at its live position,
       // plus only the edges connected to it, on top of the static picture.
       // The dragged node itself, at its OLD position, must NOT be visible —
       // since it's part of the static picture which was recorded before
       // the drag started. Confirm: does the static picture recording (Step 11)
       // get marked dirty at drag START (excluding the dragged node from the
       // recording) or does it still contain the dragged node at its
       // pre-drag position, requiring you to paint over/mask it?
       //
       // The cleaner approach: when a drag STARTS, mark static content dirty
       // so it gets re-recorded WITHOUT the dragged node (and without its
       // connected edges) included — then for the duration of the drag,
       // the static picture genuinely excludes the moving element, and you
       // simply draw the moving element fresh on top each frame with no
       // overlap/masking concerns. When the drag ENDS, mark static content
       // dirty again so the next recording includes the node back at its
       // new final position.
       DrawNode(canvas, _draggedNode, _draggedNode.LiveDragPosition);
       foreach (var edge in GetEdgesConnectedTo(_draggedNode.Id)) // use existing adjacency lookup
       {
           DrawEdge(canvas, edge, /* live position for the dragged endpoint */);
       }
   }
   else
   {
       // No drag in progress: draw whatever non-static dynamic content
       // normally needs drawing each frame regardless of drag (e.g. hover
       // highlight, selection outline) — this is whatever the canvas already
       // draws on top of static content today, outside of drag scenarios.
       // Do not change this branch's behavior in this step.
   }
   ```
   The code above is illustrative structure — adapt names, method
   signatures, and existing helper methods (`DrawNode`, `DrawEdge`,
   `GetEdgesConnectedTo`) to whatever actually exists in the real codebase.
   If `DrawNode`/`DrawEdge` as single-item draw methods don't currently
   exist (i.e. drawing is only ever done in bulk inside `DrawNodes`/
   `DrawEdges` loops), you will need to extract a single-item version from
   the existing loop body — do this as a pure extraction (same logic, just
   callable for one item at a time) without changing what it draws.
3. Make sure the "drag start" and "drag end" transitions correctly toggle
   `_staticContentDirty` (from Step 11) so the static picture is rebuilt
   to exclude the dragged node at drag-start, and rebuilt again to include
   it at its final position at drag-end.

## Constraints

- Cluster drag (per the review, "Namespace Cluster Drag" moves multiple
  nodes at once) needs the same treatment but for a *set* of nodes, not
  just one. If cluster drag shares the same drag-handling code path as
  single-node drag, generalize your fix to handle "the set of currently
  dragged node IDs" rather than a single node ID. If cluster drag is
  handled by entirely separate code, you may need a near-identical but
  separate fix there — check `StartClusterDrag` (mentioned in the review)
  and trace whether it reuses or duplicates the single-node drag rendering
  path before deciding.
- Do not change drag input handling itself (how positions are computed from
  pointer movement) — only how the *rendering* responds to an in-progress
  drag.
- This is the most visually-sensitive step so far. After this change, a node
  being dragged should look continuous and correct — no flicker, no
  "ghost" of the node left behind at its old position, no missing edges
  during the drag. If you cannot run the GUI to confirm this visually,
  say so explicitly in your summary rather than asserting it looks correct.

## Verification

1. `dotnet build`.
2. `dotnet test`.
3. Manual code-read: trace the full lifecycle of one drag (press → move →
   move → move → release) through the render method and confirm at each
   stage exactly what is and isn't being redrawn, and that nothing is drawn
   twice or left stale.
4. If you can run the GUI, drag a node across a graph with 50+ nodes and
   visually confirm correctness and that it feels smoother than before. If
   you cannot run the GUI here, state this limitation clearly.

## Done when

- During an active node (or cluster) drag, only the dragged element(s) and
  their connected edges are redrawn each frame; everything else comes from
  the cached static picture.
- No visual artifacts (ghosting, missing edges, flicker) are introduced.
- Build and tests pass.
