# MermaidDiagramExporter — Layout & Bug Fix Plan (Index)

This plan is split across multiple files so independent agents can pick up a
file each without losing context. **Read in this order.** Do not skip
`01-research-findings.md` even if you think you know how Mermaid/dagre works —
it pins down the exact mechanism this codebase needs to imitate, and the
later files refer back to its terminology (rank, order, nesting graph,
border nodes) without re-explaining it.

## Files in this plan

| File | Purpose | Risk if skipped |
|---|---|---|
| `01-research-findings.md` | What Mermaid/dagre actually does, with sources. Ground truth for all later algorithm work. | Agents reinvent or misremember dagre internals and produce a layout that looks "different but still not like Mermaid." |
| `02-bug-reset-layout.md` | Root-cause + fix for "Reset Layout doesn't regenerate original layout." | Quick win, isolated, no dependency on the algorithm work. Do this first — it's small and safe. |
| `03-bug-minimap.md` | Root-cause + fix for (a) zoom not updating minimap viewport rect, (b) save buttons overlapping minimap. | Also isolated and safe to do immediately, in parallel with `02`. |
| `04-algorithm-gap-analysis.md` | Side-by-side: what `LayeredLayoutEngine` currently does vs. what dagre does, file by file, with the specific structural reasons the current output looks worse. | This is the "why" that justifies the redesign in `05`–`08`. Skipping it means an agent might patch symptoms (tweak spacing constants) instead of fixing the actual rank/order model. |
| `05-target-architecture.md` | The new layout model to build: unified rank assignment across all real nodes (not cluster-supernodes), nesting-graph-style cluster containment, unified ordering with compound constraints, coordinate assignment. Defines new types/contracts. | This is the architectural backbone — get this wrong and every later step compounds the error. |
| `06-implementation-rank-and-nesting.md` | Step-by-step implementation of Phase 1: building the nesting graph, assigning ranks (network-simplex-style) over the *real* node graph, normalizing cluster span. | The single highest-risk phase. Most layout bugs in compound-graph reimplementations come from getting this wrong. |
| `07-implementation-ordering-and-borders.md` | Step-by-step implementation of Phase 2: per-rank ordering with compound constraints (subtree contiguity), border-node insertion, crossing reduction across cluster boundaries. | Second highest-risk phase — this is what makes "nodes inside namespaces, namespaces arranged sensibly" actually happen. |
| `08-implementation-coordinates-and-integration.md` | Step-by-step implementation of Phase 3: Brandes-Köpf-style (or simplified priority-based) coordinate assignment, cluster bounding-box derivation from contained nodes/border nodes, and wiring the new pipeline into `GraphLayoutCoordinator` behind a feature flag. | Without the feature flag / staged rollout described here, a mid-refactor state could ship and silently produce broken diagrams. |
| `09-validation-and-test-plan.md` | Concrete test graphs, golden-output strategy, and the specific invariants every phase must satisfy before moving to the next. | Without this, "looks right" is the only check, which is how the original `LayeredLayoutEngine` likely passed review despite being structurally wrong. |

## Ground rules for any agent working this plan

1. **Do not skip phases.** Phase 2 (ordering/borders) depends on Phase 1's
   rank assignment being correct over the *unified* node graph, not over
   cluster-supernodes. Phase 3 depends on Phase 2's per-rank order being
   final. If you "shortcut" by keeping the old cluster-supernode ranking and
   only patching ordering, you will reproduce the same class of bug this plan
   exists to fix.
2. **Keep the old engine alive behind a flag** (`LayoutOptions.UseCompoundLayoutEngine`
   or similar — see `08`) until the new engine passes the validation suite in
   `09`. Never delete `LayeredLayoutEngine.cs` until the replacement is
   validated; treat it as a fallback / reference implementation, and a basis
   for diffing behavior.
3. **Every geometric claim in this plan is sourced** from dagre's own
   documentation, wiki, and published implementation notes (cited in
   `01-research-findings.md`). If an implementing agent is unsure whether a
   detail is "how dagre does it" vs. "a reasonable guess," check `01` first —
   it explicitly separates "confirmed mechanism" from "inferred from how the
   pieces fit together."
4. **This is a C#/.NET codebase**, not JS. We are not embedding dagre or
   porting its source line-by-line. We are reimplementing the *algorithm*
   (network-simplex-style ranking, WMedian ordering with compound
   constraints, border-node cluster containment, BK-style coordinate
   assignment) using the existing `LayoutGraph`/`LayoutNode`/`LayoutCluster`
   data model in `Layout/LayoutModels.cs`, reusing what's reusable (e.g.
   `CrossingReductionService`'s median/transpose machinery) and replacing
   what's structurally wrong (cluster-as-supernode ranking).
5. **Bug fixes in `02` and `03` are independent of the algorithm work.** Do
   them first, ship them, then proceed to `04`–`09`.

## Current architecture quick-reference (so file paths below make sense)

```
src/MermaidDiagramExporter.Gui/
  GraphCanvas.cs                  — rendering + pan/zoom/drag input, owns _zoom/_panX/_panY
  MinimapControl.axaml.cs         — minimap rendering + viewport rectangle
  MainWindow.axaml.cs             — wires buttons (Reset Layout, Save .mmd, etc.), owns _manualOverrides
  LayoutEngine.cs                 — TypeGraph -> LayoutResult -> GraphNode/GraphEdge bridge
  Layout/
    GraphLayoutCoordinator.cs     — orchestrates: LayoutGraphFactory -> LayoutPipeline (prep passes)
                                     -> LayeredLayoutEngine (or SimpleColumnLayoutEngine) -> PostLayoutPipeline
                                     -> EdgeRoutingService
    LayeredLayoutEngine.cs        — THE FILE THAT NEEDS THE ALGORITHMIC REWRITE (see 04, 05)
    CrossingReductionService.cs   — WMedian + transpose crossing reduction (reusable, see 07)
    NamespaceClusterBuilder.cs    — builds LayoutCluster list from TypeGraph.Groups (namespaces)
    Passes/ClusterHierarchyPass.cs— resolves cluster parent/child nesting (namespace nesting)
    LayoutOptions.cs              — all spacing/sizing tunables
    Routing/EdgeRoutingService.cs — post-layout edge path computation
    Post/ClusterOverlapResolutionPass.cs, ClusterBoundsPolishPass.cs, ClusterTitleMarginPass.cs
  Persistence/
    ManualLayoutOverrides.cs      — per-node drag-delta storage
    TypeGraphCacheService.cs      — SaveManualOverrides/LoadManualOverrides (BUG SOURCE for 02)
```
