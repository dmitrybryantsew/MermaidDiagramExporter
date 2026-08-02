# 00-INDEX — Design Mode Planning

This plan adds a **Design Mode** to MermaidDiagramExporter: a visual authoring
tool where you draw class rectangles and their relationships directly on the
canvas, then export the result as Mermaid diagrams, JSON, or stub source code.

## The reverse process

The current app does **code → diagram**: scan a C# project, lay it out,
visualize it. Design Mode does **diagram → spec**: start from a blank canvas
(or a scanned graph), draw classes, link them, export.

## Documents

| # | File | Purpose |
|---|------|---------|
| 01 | `01-vision-and-scope.md` | What Design Mode is, who it's for, what it produces, explicit non-goals |
| 02 | `02-mode-toggle-architecture.md` | How Analyze vs Design mode coexist — shared canvas, separate toolbars, mode switcher |
| 03 | `03-canvas-interaction-model.md` | Click-to-add, drag-to-move, drag-from-port-to-create-edge, selection model |
| 04 | `04-class-rectangle-component.md` | The editable ClassRectangle control — header, member rows, resize handles, member editor |
| 05 | `05-data-model-and-persistence.md` | DesignGraph → JSON serialization, save/load, merge with scanned graphs |
| 06 | `06-export-pipeline.md` | Design graph → Mermaid, JSON, C# stub source code |
| 07 | `07-implementation-phases.md` | Ordered phases for building it (M0 scaffold → M6 polish), each independently shippable |
| 08 | `08-risks-and-decisions.md` | Open questions, risks, decisions deferred to implementation time |

## Ground rules (from 00-INDEX of the previous plan, still apply)

1. **Prompt caching must not break.** Design Mode shares the canvas with Analyze
   Mode — the system prompt, toolset, and per-conversation context must remain
   stable across mode switches. Mode-specific UI is loaded per-window, not
   per-conversation.

2. **The core is a narrow waist; capability lives at the edges.** The existing
   `LayoutResult` contract is the stable seam between layout engines and
   rendering. Design Mode produces the same `LayoutResult` shape — it just
   builds the input graph differently (from user drawing instead of Roslyn scan).

3. **Behavior contracts over snapshots.** Tests assert invariants (e.g. "every
   class has at least one member", "every edge connects two existing classes"),
   not exact pixel positions or specific JSON formats.

4. **E2E validation, not just green unit mocks.** Design Mode must be exercised
   end-to-end against temp files; the export pipeline must produce parseable
   Mermaid and compilable C# stubs (even if minimal).

5. **Cache-, alternation-, and invariant-safe.** The mode toggle must not
   invalidate the cached layout; switching modes mid-session must not corrupt
   the graph state.

6. **Contributor credit preserved.** New files go under their own authorship;
   no rewriting of existing code without a stated reason.

7. **What does NOT change (from the previous plan, still in force):**
   - `LayoutResult`, `LayoutNode`, `LayoutEdge`, `LayoutCluster` —
     zero changes. Design Mode produces the same output shape.
   - `LayoutOptions` — no new required fields. Mode selection is a
     separate UI concern, not a layout option.
   - `GraphLayoutCoordinator`, both layout engines, all post-layout passes —
     unchanged.
   - Rendering pipeline (`CanvasRenderer`, `GraphCanvas`, `MinimapControl`,
     `HitTestService`) — unchanged. Design Mode reuses them.
