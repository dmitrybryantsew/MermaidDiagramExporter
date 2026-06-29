//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\00-INDEX.md
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

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\00-INDEX.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\01-research-findings.md
# 01 — Research Findings: How Mermaid Actually Lays Out Nodes and Namespaces

## 0. Scope of this document

This file is the single source of truth for "what does Mermaid do" used
throughout this plan. Every other file references concepts defined here
(rank, order, nesting graph, border node, network simplex, WMedian,
Brandes-Köpf) without re-deriving them. Read it fully before touching code.

Each claim below is tagged:
- **[CONFIRMED]** — stated directly in dagre's own docs/wiki/changelog, or
  visible in real (quoted) dagre source.
- **[INFERRED]** — not directly stated, but follows necessarily from
  confirmed mechanisms; flagged so implementers know it's reasoning, not a
  citation.

## 1. Which engine, and why it matters

Mermaid's flowchart/class-diagram renderer (which is what your exporter's
output most resembles — boxes connected by typed edges, grouped into
namespace subgraphs) defaults to the **dagre** layout engine; an alternative
**ELK** (Eclipse Layout Kernel) engine is available for very large graphs but
is not the default and is a different codebase/algorithm family entirely.
**[CONFIRMED]** — Mermaid's docs state Dagre is "the classic layout
algorithm that has been used in Mermaid for a long time... ideal for most
diagrams," with ELK as the opt-in alternative for large/complex diagrams.

This matters because **the visual style the user is trying to match (image 1
in the conversation) is dagre's style**, not ELK's. The rest of this document
describes dagre specifically.

## 2. The high-level pipeline (top-level call sequence)

dagre's `layout.js` runs, in order **[CONFIRMED — this is dagre's actual
source, quoted from its own repo via search index]**:

```
makeSpaceForEdgeLabels(g)
removeSelfEdges(g)
acyclic.run(g)                 // 1. Cycle removal (feedback-arc-set, greedy or DFS)
nestingGraph.run(g)            // 2. Build nesting graph for compound/cluster structure
rank(util.asNonCompoundGraph(g)) // 3. Rank assignment over the FLATTENED graph
injectEdgeLabelProxies(g)
removeEmptyRanks(g)
nestingGraph.cleanup(g)        // 4. Remove nesting-graph scaffolding, keep its rank effects
normalizeRanks(g)
assignRankMinMax(g)
removeSelfEdges.undo(g)
// ... edge normalization (long edges -> dummy node chains) ...
// ... order(g) ...             // 5. Per-rank ordering (crossing minimization)
// ... insertSelfEdges(g) ...
// ... addBorderSegments(g) ... // 6. Per-subgraph border node insertion
// ... position(g) ...          // 7. Coordinate assignment (Brandes-Köpf-derived)
// ... positionSelfEdges(g) ...
// ... removeBorderNodes(g) ...
// ... normalizeEdgeLabels... ...
// ... denormalize(g) ...       // 8. Reverse edge normalization (re-merge dummy chains)
// ... fixupEdgeLabelCoords(g) ...
// ... undo acyclic reversal ...
// ... translateGraph(g) ...    // shift to (0,0)-based, add margins
// ... assignNodeIntersects(g)...
// ... reversePointsForReversedEdges(g) ...
```

The critical structural fact for this project: **steps 2–7 all operate on a
single graph that contains every real node AND the cluster (subgraph)
hierarchy as compound-node metadata** — clusters are never "solved
separately and then placed like a single big node." Ranking, ordering, and
positioning all happen once, globally, with the cluster hierarchy folded in
as *constraints* on that single global solve.

This is the #1 structural difference from your current `LayeredLayoutEngine`
(see `04-algorithm-gap-analysis.md` for the line-by-line comparison) — your
engine ranks **clusters as supernodes first**, then separately lays out each
cluster's internal nodes in isolation. Dagre never does this.

## 3. Step 1 — Cycle removal (`acyclic`)

**[CONFIRMED]** dagre uses a feedback-arc-set technique to make the graph
acyclic before ranking; edges that would create cycles are temporarily
reversed (tracked so they can be un-reversed and redrawn correctly later).
The default acyclicer is a DFS-based approach; a "greedy" heuristic is also
selectable.

**Relevance to this project:** inheritance/implements/association edges in a
C# codebase are not guaranteed to be acyclic at the namespace or even the
type level (e.g. mutual interface references, or association cycles between
two services). The current engine's `AssignClusterRanks`/`AssignLocalNodeRanks`
methods use a **longest-path relaxation with a bounded iteration count**
(`component.Count * 2` iterations) instead of explicit cycle detection. This
mostly works for the rank-pushing direction but does not handle the
"reverse for ranking, draw correctly" trick — cyclic edges in the current
code just get less leverage on rank rather than being explicitly reversed
and restored. **[INFERRED]** this is acceptable to keep as-is for now (the
longest-path relaxation degrades gracefully on cycles, it just doesn't
guarantee minimum edge length / feedback-arc-set optimality), but if cyclic
namespace graphs produce visibly bad layouts during validation (`09`), the
fix is to add explicit cycle detection (Tarjan SCC or DFS back-edge
detection) and reverse those edges for ranking purposes only, restoring
direction for rendering. Flag this as a stretch goal, not a blocker.

## 4. Step 2 — The Nesting Graph (the mechanism that makes clusters work)

This is **the most important mechanism in this entire plan.** It is what
makes namespaces (a) stay visually contiguous, (b) get a sensible bounding
box, and (c) not distort the rank assignment of nodes that aren't in any
cluster.

**[CONFIRMED — mechanism]:** dagre's wiki states the clustering
implementation "derives extensively from Sander, 'Layout of Compound
Directed Graphs,'" and crossing reduction for clustered graphs derives from
Forster's papers on crossing reduction in layered compound graphs. The
nesting graph technique itself, concretely, from dagre's own source
(`addBorderNode` — quoted from the real implementation found via search):

```js
function addBorderNode(g, prop, prefix, sg, sgNode, rank) {
  var label = { width: 0, height: 0, rank: rank, borderType: prop },
      prev = sgNode[prop][rank - 1],
      curr = util.addDummyNode(g, "border", label, prefix);
  sgNode[prop][rank] = curr;
  g.setParent(curr, sg);
  if (prev) {
    g.setEdge(prev, curr, { weight: 1 });
  }
}
```

**What this means concretely:**

For every subgraph (cluster) `sg`, and for every rank that subgraph spans,
dagre creates a **dummy "border" node** at that rank, parented to `sg`. These
border-rank dummy nodes for the same subgraph are then **chained together
with edges of weight 1** from one rank to the next (`prev -> curr`). This
chain of border dummies running through every rank the cluster occupies is
what:

1. **Forces the cluster's nodes to be ranked contiguously.** Because the
   border-node chain spans every rank from the cluster's first rank to its
   last, and all of the cluster's real children are pulled toward this
   chain (via the nesting graph's auxiliary edges — see below), the ranker
   cannot "interleave" a non-member node's rank inside the cluster's rank
   span without the border chain creating slack/cost that the
   network-simplex ranker will resolve by tightening the cluster instead.
2. **Gives the cluster a left/right or top/bottom skeleton to attach a
   bounding box to**, independent of which individual nodes happen to be at
   the cluster's rank extremes — the border node chain itself literally
   marks the cluster's vertical (or horizontal, depending on `rankdir`)
   extent, rank by rank.
3. The nesting graph additionally adds **very high-weight edges from each
   subgraph's "root" border down to its first/last real children**, which is
   what biases the *ranker* (not just the orderer) to keep a subgraph's
   contents from spreading across far-apart ranks relative to nodes outside
   the subgraph. This is the part of the mechanism that is *inferred* from
   how nesting graphs are described in the Sander paper lineage and from the
   general purpose of a "nesting graph" in compound graph layout — exact
   edge weights are an implementation detail of dagre's source not fully
   quoted here. **[INFERRED — mechanism direction confirmed, exact weight
   values not independently verified from source in this research pass]**.

**Practical translation for this codebase (non-JS, no literal nesting graph
required):** you do not need to port dagre's `nesting-graph.js` class for
class; you need to reproduce its *effect*:

- Every cluster, once ranked, must occupy a **contiguous range of ranks**
  with no foreign (non-member, non-descendant) real node placed at a rank
  strictly between the cluster's min and max rank **in the same part of the
  ordering "under" that cluster**. (Nodes from sibling clusters can be at the
  same ranks — that's normal, side-by-side namespaces — but the *subtree*
  contiguity in the per-rank order is the actual constraint, detailed in
  step 5 below.)
- A cluster's bounding box is derived from **the union of the positions of
  all its (possibly transitively, for nested namespaces) contained real
  nodes, plus the border padding**, computed *after* coordinate assignment,
  not estimated up front by a separate "build cluster layout in isolation"
  step the way `LayeredLayoutEngine.BuildClusterLayout` currently does.

This file's job is just to establish the *target mental model*; the literal
implementation steps are in `06-implementation-rank-and-nesting.md` and
`07-implementation-ordering-and-borders.md`.

## 5. Step 3 — Rank assignment (`rank`)

**[CONFIRMED]** dagre supports three rankers, selectable via the `ranker`
option:
- `network-simplex` (default) — minimizes the total weighted edge length
  (sum over edges of `weight * (rank(target) - rank(source))`) subject to
  every edge's minimum length constraint, using the network simplex method
  from Gansner et al., "A Technique for Drawing Directed Graphs." This is the
  paper dagre's wiki calls the essential starting reference for the whole
  field.
- `tight-tree` — builds a maximal tight spanning tree first (an
  optimization/heuristic step toward network-simplex's optimum, reducing the
  number of "long" edges) without doing full simplex iterations.
- `longest-path` — simple DFS-based longest-path ranking; fast, but produces
  more/longer edges spanning multiple ranks than the other two.

Crucially: **[CONFIRMED]** rank is run on `util.asNonCompoundGraph(g)` — i.e.
ranking happens on a graph where the compound (parent/child cluster)
structure has been temporarily flattened to a plain graph **augmented with
the nesting-graph's border nodes and high-weight edges**, but every original
real node is still present and individually ranked. **Clusters are never
collapsed into a single node for ranking purposes** — this is the opposite
of what `LayeredLayoutEngine.AssignClusterRanks` does (it literally builds a
`ClusterMetric` per cluster and ranks *clusters*, only ranking individual
nodes afterward, locally, within a cluster that's already been positioned).

**Your current engine ports a reasonable-on-its-own algorithm
(longest-path-style relaxation) but applies it at the wrong granularity**
(cluster-level first, node-level second, instead of node-level globally with
cluster constraints folded in). This explains the screenshot symptom: nodes
end up positioned according to *which cluster they're in* far more strongly
than *what they're actually connected to*, which is why cross-namespace
edges in image 1 look tangled — the rank of a node is barely influenced by
its real neighbors outside its own cluster.

**Recommendation for this codebase [INFERRED — engineering judgment, not a
dagre citation]:** implement a simplified network-simplex-equivalent
sufficient for this use case — see `06-implementation-rank-and-nesting.md`
for the exact algorithm to implement, which is a longest-path initial
feasible tree + cut-value-based iterative improvement (the same approach
Gansner et al. describe), but the key fix that matters most is **scope**:
run it over **all real nodes plus nesting-graph border dummies in one global
pass**, not over a cluster-supernode graph followed by isolated per-cluster
sub-passes.

## 6. Step 4 — Edge normalization

**[CONFIRMED]** Any edge spanning more than one rank gets broken into a
chain of unit-length dummy nodes, one per intermediate rank, so that every
edge in the ranked/ordered graph connects adjacent ranks only. This is
standard Sugiyama-style layered drawing and is **already partially present**
in this codebase's concepts (`LayoutNodeRole.SelfLoopHelper`,
`ClusterInboundAnchor`/`ClusterOutboundAnchor` anchors suggest awareness of
the dummy-node pattern), but is not applied generally to *all* long edges
today — only to specific cases (self-loops, cluster boundary anchors). This
matters for both ordering quality and for edge routing (`EdgeRoutingService`)
because without dummy nodes occupying space at intermediate ranks, the
crossing-reduction step has no way to "see" an edge passing through a rank
it doesn't terminate at, so it can't avoid routing it through unrelated
nodes.

## 7. Step 5 — Ordering (`order`) — crossing minimization with compound constraints

**[CONFIRMED]** dagre's ordering phase:
1. Builds an **initial order** per rank via DFS from the graph's sources
   (a deterministic, "as close to insertion order as the DFS allows"
   starting point).
2. Runs **iterative sweeps** (default 4 full passes, alternating downward
   and upward through the ranks) where each rank's node order is recomputed
   by sorting nodes according to the **median (or barycenter, "weighted
   mean") position of their neighbors in the adjacent already-fixed rank**
   — this is literally called the WMedian heuristic in the dagre source
   (function names like `resolveConflicts`, `sort`, with `barycenter`,
   `weight` fields), and your project's own
   `CrossingReductionService.BuildMetric`/`ReorderRow` already implements
   this exact technique (median-first, barycenter tiebreak) — **this part of
   your codebase is already correct and dagre-equivalent at the
   algorithm-choice level.**
3. After each sweep, a **transpose pass** does local adjacent-pair swaps
   wherever swapping reduces crossing count against both neighboring ranks —
   your `CrossingReductionService.ApplyTransposePass` already does exactly
   this.
4. **The part your codebase is missing:** dagre's ordering is **compound-graph
   aware**. When a rank contains nodes that belong to different subgraphs
   (clusters), the WMedian/barycenter computation and the final sort must
   respect **subgraph contiguity** — all of cluster A's nodes-at-this-rank
   must end up adjacent to each other in the final order, and ditto for
   cluster B, even if the raw barycenter values would have interleaved them.
   dagre's `sortSubgraph`/`resolveConflicts`/`expandSubgraphs` functions
   (named in the quoted source found in this research pass — see the
   `dagre.` documentation quote in section 4) compute a barycenter **for the
   subgraph as a whole** (treating a whole subgraph as a single sortable
   unit at this stage, recursively, for nested subgraphs) and only expand
   back into individual member nodes' relative order *after* the subgraphs
   themselves have been ordered relative to each other and to non-member
   nodes at that rank. This two-level sort (sort subgraphs-as-units, then
   expand and recursively sort within each subgraph) is the exact "secret
   sauce" for why Mermaid's namespace boxes don't have their member nodes
   scattered/interleaved with another namespace's nodes at the same visual
   row.

This is detailed step-by-step in `07-implementation-ordering-and-borders.md`.

## 8. Step 6 — Border segments (`addBorderSegments`)

**[CONFIRMED — mechanism, from the quoted `findType2Conflicts` /
`addBorderNode` source above]:** Separately from the nesting-graph's
rank-spanning border chain (step 2, used during ranking), dagre adds
**left/right border dummy nodes at every rank a subgraph spans**, used
during ordering and positioning to give the subgraph a literal left-edge and
right-edge node at each rank that the crossing-counting and coordinate
assignment code can reason about directly, and which become the actual
geometric edges of the drawn cluster rectangle. This is conceptually similar
to what `ClusterBoundaryEdgeNormalizer.cs` and
`Layout/Routing/ClusterBoundaryClipper.cs` in your codebase are *trying* to
approximate after the fact — but dagre builds these borders **before**
ordering/positioning so they participate in (and constrain) those phases,
rather than clipping edges post-hoc against a bounding box computed from
already-placed nodes.

## 9. Step 7 — Coordinate assignment (`position`)

**[CONFIRMED]** dagre's horizontal (cross-rank) coordinate assignment is
derived from Brandes & Köpf, "Fast and Simple Horizontal Coordinate
Assignment," with dagre's own stated modification: "we made some adjustments
to get tighter graphs when node and edge sizes vary greatly." The algorithm,
per the Brandes-Köpf paper (confirmed via the paper's own erratum, fetched
above) works in three repeated phases for all 4 combinations of
vertical/horizontal alignment direction (to later average/median the 4
results and reduce directional bias):
1. **Vertical alignment** — each node gets aligned to a neighbor in the
   adjacent rank when possible (preferring to align long chains of dummy
   nodes straight, since a straight dummy chain = a straight edge segment).
2. **Horizontal compaction** — nodes in the same "block" (aligned chain)
   are assigned one shared x-coordinate, computed via a longest-path-style
   compaction so blocks pack as tightly as spacing constraints allow.
3. The four directional results (down/left, down/right, up/left, up/right)
   are combined, typically by taking the median or a balanced
   average, to avoid bias toward one side.

Rank (the perpendicular axis — vertical position if `rankdir=LR`,
horizontal if `rankdir=TB`) is simply `rank index * (max node size at that
rank + rankSep)`, cumulative.

**Recommendation for this codebase [INFERRED — engineering judgment]:**
full 4-direction Brandes-Köpf is high implementation cost for the visual
benefit it adds over a simpler approach. A **priority/median-based
single-pass coordinate assignment** (align each node to the median x of its
neighbors in adjacent ranks, resolve overlaps by pushing in priority order —
higher-degree nodes have higher priority and push lower-degree neighbors
out of the way) gets ~80% of the visual quality at a fraction of the
complexity, and is a defensible, well-known simplification used by several
dagre-inspired reimplementations. This is specified precisely in
`08-implementation-coordinates-and-integration.md`. Treat full 4-direction
BK as a stretch goal once the simplified version is validated.

## 10. Step 8 — Cluster geometry (how namespace boxes get their final rectangle)

**[INFERRED from confirmed mechanism]:** once every real node and every
border dummy node has a final (x, y) and size, a cluster's rectangle is
simply the bounding box of:
- every border dummy node belonging to that cluster (left/right/top/bottom
  border chains from step 6), **and**
- every real node transitively contained in that cluster (including nodes
  inside nested child clusters/namespaces),

padded outward by a fixed margin for the title bar and edge clearance. This
is, structurally, close to what `Layout/Post/ClusterBoundsPolishPass.cs` and
`ClusterTitleMarginPass.cs` already do in this codebase — **the post-layout
geometry-polishing passes are conceptually fine and largely reusable**; what
feeds them (the node positions themselves) is what needs to change.

## 11. Direct implications — what to change vs. what to keep

| Existing component | Verdict | Why |
|---|---|---|
| `CrossingReductionService` (WMedian + transpose) | **Keep, extend** | Already matches dagre's ordering heuristic at the algorithm-choice level (confirmed §7). Needs extension for compound/subgraph-aware sorting (§7.4). |
| `LayeredLayoutEngine.AssignClusterRanks` + `BuildClusterMetrics` (cluster-as-supernode ranking) | **Replace** | Wrong granularity — dagre never ranks clusters as atomic units (§5). This is the root cause of namespace-driven (rather than connectivity-driven) node placement. |
| `LayeredLayoutEngine.AssignLocalNodeRanks` (per-cluster local ranking) | **Replace, generalize** | The *technique* (longest-path relaxation) is reusable, but it must run once globally over all real nodes + border dummies, not once per cluster in isolation (§5, §6 implementation file). |
| `NamespaceClusterBuilder`, `ClusterHierarchyPass` (cluster/namespace hierarchy extraction) | **Keep** | This is just data modeling (which node belongs to which namespace, and namespace nesting) — orthogonal to the ranking/ordering algorithm itself. |
| `Layout/Post/*` (ClusterBoundsPolishPass, ClusterOverlapResolutionPass, ClusterTitleMarginPass) | **Keep, adapt inputs** | Conceptually aligned with §10's bounding-box-from-contents approach; may need minor adaptation once the upstream node positions come from the new engine, but the *passes themselves* don't need a rewrite. |
| `Routing/EdgeRoutingService`, `ClusterBoundaryClipper` | **Keep, revisit after** | Edge routing quality will improve "for free" once dummy-node-based edge normalization (§6) gives it real intermediate points to route through, rather than having to guess a path between two cluster boxes post-hoc. Revisit in `09` validation, not a primary target of this plan. |

## 12. Sources consulted

- Mermaid official docs — "Diagram Syntax" / layout algorithm selection
  (`layout: dagre` vs `layout: elk`): https://mermaid.js.org/intro/syntax-reference.html
- Mermaid official docs — Flowchart syntax, renderer selection:
  https://docs.mermaidchart.com/mermaid-oss/syntax/flowchart.html
- dagre GitHub repository (official): https://github.com/dagrejs/dagre
- dagre Wiki (official, includes algorithm references and bibliography):
  https://github.com/dagrejs/dagre/wiki
- dagre `npm` package README (multiple historical versions, consistent
  algorithm description): https://www.npmjs.com/package/dagre
- dagre source, `lib/layout.js` (`runLayout` step sequence, quoted directly):
  https://github.com/dagrejs/dagre/blob/master/lib/layout.js
- dagre source excerpts (`addBorderNode`, `findType2Conflicts`,
  `resolveConflicts`/`sort`/`expandSubgraphs`), quoted via secondary index:
  https://npmdoc.github.io/node-npmdoc-dagre/build/apidoc.html
- DeepWiki technical breakdown of dagre internals (network simplex,
  Brandes-Köpf, barycenter ordering — cross-checked against official docs
  above): https://deepwiki.com/dagrejs/dagre
- DeepWiki technical breakdown of Mermaid's layout engine selection and
  Dagre-wrapper recursive rendering for nested subgraphs:
  https://deepwiki.com/mermaid-js/mermaid/2.3-layout-engines
- Brandes & Köpf erratum (confirms the 3-phase, 4-direction algorithm
  structure directly from the paper's own authors):
  https://arxiv.org/pdf/2008.01252
- `dagre-rs` (Rust port) README — useful as an independent cross-check that
  the "27-step pipeline," "network simplex," "barycenter," and
  "Brandes-Koepf 4-direction sweep" descriptions are consistent across
  independent re-implementations, not just dagre's own marketing copy:
  https://github.com/kookyleo/dagre-rs

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\01-research-findings.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\02-bug-reset-layout.md
# 02 — Bug Fix: "Reset Layout" Does Not Restore the Original Generated Layout

## Status: root cause fully identified from source. This is a precise, low-risk fix.

## Symptom (as reported)

User moves nodes manually, then clicks "Reset Layout." Expectation: the
layout returns to whatever the algorithm would generate fresh (as if no
manual edits had ever happened). Actual: the layout does not return to the
freshly-generated arrangement.

## Root cause

There are **two cooperating bugs**, both in
`src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs` and
`src/MermaidDiagramExporter.Gui/Persistence/TypeGraphCacheService.cs`.

### Bug A — `SaveManualOverrides` silently no-ops when there's nothing to save

`TypeGraphCacheService.SaveManualOverrides`:

```csharp
public void SaveManualOverrides(ManualLayoutOverrides overrides, ProjectSettings settings)
{
    if (overrides == null || !overrides.HasOverrides) return;   // <-- BUG

    string cacheDir = _settingsService.ResolveCacheDirectory(settings);
    string path = Path.Combine(cacheDir, "layout.overrides.json");
    overrides.LastSavedUtc = DateTime.UtcNow;
    var json = JsonSerializer.Serialize(overrides, ...);
    File.WriteAllText(path, json);
}
```

`ManualLayoutOverrides.HasOverrides` is defined as
`NodePositionDeltas.Count > 0`. The guard clause means: **if you pass in an
empty/cleared overrides object, the file on disk is never touched** — not
overwritten with an empty version, not deleted. The previous (non-empty)
`layout.overrides.json` from before the reset stays on disk, untouched,
forever (or until the next *non-empty* save).

### Bug B — `OnResetLayout` relies on that save happening, then immediately reloads from disk

`MainWindow.axaml.cs`:

```csharp
private void OnResetLayout(object? sender, RoutedEventArgs e)
{
    _manualOverrides.Clear();                                    // now empty
    _cacheService.SaveManualOverrides(_manualOverrides, _currentSettings); // no-ops (Bug A)

    if (_currentGraph != null)
    {
        _layoutEngine.ManualOverrides = _manualOverrides;        // engine told "no overrides" (correct, in memory)
        SetDisplayedGraph(_currentGraph);                        // <-- but this reloads from disk!
    }
}
```

And `SetDisplayedGraph`:

```csharp
private void SetDisplayedGraph(TypeGraph? graph, string selectedNodeId = "")
{
    ...
    if (_currentSettings.PersistManualLayout)
    {
        _manualOverrides = _cacheService.LoadManualOverrides(_currentSettings); // <-- reloads STALE file
    }
    else
    {
        _manualOverrides = new ManualLayoutOverrides();
    }
    _layoutEngine.ManualOverrides = _manualOverrides;
    ...
}
```

**Sequence of events when the user clicks Reset Layout (assuming
`PersistManualLayout == true`, the normal case if they want their layout to
survive app restarts):**

1. In-memory `_manualOverrides` is cleared. ✅ correct so far.
2. `SaveManualOverrides` is called with the now-empty overrides → **no-op,
   stale file with the OLD drag deltas remains on disk.** ❌
3. `SetDisplayedGraph` runs, and as part of its normal startup-equivalent
   logic, calls `LoadManualOverrides` → reads the **stale file**, repopulates
   `_manualOverrides` with the exact deltas the user just tried to clear. ❌
4. `_layoutEngine.Layout(graph)` runs, computes the fresh
   algorithmic layout, then immediately re-applies the old manual deltas on
   top of it via `ManualLayoutApplier.ApplyOverrides`. ❌

Net effect: the "reset" button regenerates the base layout correctly
underneath, but then **silently re-applies the exact manual edits the user
was trying to discard**, because step 3 undid step 1's in-memory clear by
reloading from a file that step 2 failed to update.

This also means: **if `PersistManualLayout` is false**, Reset Layout
probably *does* work correctly today, because `SetDisplayedGraph`'s `else`
branch assigns a fresh `new ManualLayoutOverrides()` instead of reloading
from disk. This is a useful diagnostic to confirm the root cause before
patching: ask the user (or check) whether the bug reproduces with
"Persist Manual Layout" off — it shouldn't, under this diagnosis.

## The fix

Two independent, complementary changes. Do both — they fix different
failure modes and are each correct in isolation, but together they're
defense-in-depth.

### Fix 1 — `SaveManualOverrides` must persist "no overrides" too

```csharp
public void SaveManualOverrides(ManualLayoutOverrides overrides, ProjectSettings settings)
{
    string cacheDir = _settingsService.ResolveCacheDirectory(settings);
    string path = Path.Combine(cacheDir, "layout.overrides.json");

    if (overrides == null || !overrides.HasOverrides)
    {
        // Nothing to persist — make sure a stale file from a previous
        // session doesn't resurrect old deltas on next load.
        if (File.Exists(path))
            File.Delete(path);
        return;
    }

    overrides.LastSavedUtc = DateTime.UtcNow;
    var json = JsonSerializer.Serialize(overrides, new JsonSerializerOptions
    {
        WriteIndented = true,
        Converters = { new Vector2JsonConverter() }
    });
    File.WriteAllText(path, json);
}
```

This alone fixes the bug for the next time the app loads (`LoadManualOverrides`
will correctly find no file and return a fresh empty `ManualLayoutOverrides`).
It does **not** fix the immediate in-session symptom, because
`SetDisplayedGraph` still calls `LoadManualOverrides` synchronously right
after — which after Fix 1 will return an empty object correctly, so actually
**Fix 1 alone is sufficient** to resolve the reported symptom, since the
reload will now correctly find no file / an empty file. But apply Fix 2 as
well, because relying on a load-immediately-after-save round trip through
the filesystem for correctness is fragile (slow disks, antivirus locking the
file, a future code change that reorders things) — don't let in-memory state
that was *just deliberately set* get silently clobbered by a redundant
reload of the same data from disk.

### Fix 2 — `OnResetLayout` should not reload from disk after clearing in-memory state

Make `SetDisplayedGraph` not unconditionally re-source `_manualOverrides`
from disk — only do that on the "first load of a graph" path, not when the
caller has already deliberately set `_manualOverrides` itself.

Refactor `SetDisplayedGraph` to accept the overrides decision from the
caller instead of always re-deciding it internally:

```csharp
private void SetDisplayedGraph(
    TypeGraph? graph,
    string selectedNodeId = "",
    bool reloadManualOverridesFromDisk = true)
{
    if (graph == null) { /* unchanged */ return; }

    if (reloadManualOverridesFromDisk)
    {
        _manualOverrides = _currentSettings.PersistManualLayout
            ? _cacheService.LoadManualOverrides(_currentSettings)
            : new ManualLayoutOverrides();
    }
    _layoutEngine.ManualOverrides = _manualOverrides;

    var (nodes, edges) = _layoutEngine.Layout(graph);
    // ...unchanged rest of method...
}
```

And update `OnResetLayout` to pass `reloadManualOverridesFromDisk: false`,
since it has already set the canonical in-memory state it wants used:

```csharp
private void OnResetLayout(object? sender, RoutedEventArgs e)
{
    _manualOverrides.Clear();
    _cacheService.SaveManualOverrides(_manualOverrides, _currentSettings);

    if (_currentGraph != null)
    {
        _layoutEngine.ManualOverrides = _manualOverrides;
        SetDisplayedGraph(_currentGraph, reloadManualOverridesFromDisk: false);
    }
}
```

Leave every other call site of `SetDisplayedGraph` using the default
(`true`), since they represent "load/switch graph, restore whatever was last
saved" — that behavior is correct and should not change. Grep for
`SetDisplayedGraph(` before finishing — at the time of this analysis there
were calls at (line numbers approximate, re-check after Fix 1/2 land, since
edits shift lines):
- `MainWindow.axaml.cs` constructor / initial scan completion path
- the focus-navigation "set focused subgraph" path (`OnFocusRequested`-style)
- the matrix-cell-click path (`OnMatrixCellClicked` — note: this one calls
  `GraphCanvasView.SetGraph` directly, not `SetDisplayedGraph`, so it's
  unaffected)
- `OnEdgeVisibilityChanged`/settings-changed paths
- `OnResetLayout` (the one this fix changes)
- the breadcrumb/back-forward navigation restore path (`snapshot.Graph`)
- the "go to root graph" path

All of those except `OnResetLayout` should keep `reloadManualOverridesFromDisk: true`
(the default), since they're legitimately "(re)entering a view, restore
whatever the user had saved for it" moments, not "I just explicitly decided
what the override state should be" moments.

## Why both fixes, not just one

- Fix 1 alone resolves the *currently reported* symptom, because the
  synchronous save-then-load round trip through the filesystem happens to
  work once the save actually clears the file. But it leaves a latent
  fragility: any future code path that clears overrides and expects that
  state to stick without an immediate `SetDisplayedGraph` reload (e.g. a
  future "undo last drag" feature, or a unit test that checks in-memory
  state right after calling `Clear()` + `Save()`) would be silently
  vulnerable to the exact same class of bug if it also happens to trigger a
  reload from disk somewhere downstream.
- Fix 2 alone resolves the symptom too (the in-memory cleared state is no
  longer discarded by a redundant reload), and is more robust because it
  doesn't depend on disk I/O completing/succeeding correctly in the same
  tick — but without Fix 1, a stale `layout.overrides.json` would still sit
  on disk and would resurrect itself the next time the app starts and loads
  this project (since the *next* `SetDisplayedGraph` call after an app
  restart legitimately should reload from disk, and Fix 2 doesn't touch that
  legitimate path).

Doing both means: in-session reset is correct immediately (Fix 2), and the
on-disk state is also correct so a later app restart doesn't un-reset the
layout (Fix 1).

## Verification steps for whoever implements this

1. Add/extend a test in `ManualLayoutOverridesRoundtripTests.cs`
   (`tests/MermaidDiagramExporter.Tests/`) that:
   - Saves a non-empty `ManualLayoutOverrides` via `SaveManualOverrides`.
   - Confirms the file exists and round-trips via `LoadManualOverrides`.
   - Then saves an **empty/cleared** `ManualLayoutOverrides` over the same
     cache directory.
   - Confirms the file is now **gone** (or, if you prefer "write an empty
     JSON" instead of "delete the file" as your house style, confirms
     `LoadManualOverrides` returns `HasOverrides == false` afterward —
     either implementation detail is fine, but pick one and assert it,
     don't leave it unspecified).
2. Manual repro test in the running app:
   a. Load a project, drag 2–3 nodes to new positions.
   b. Confirm `layout.overrides.json` in the cache directory has 2–3
      entries (inspect the file directly).
   c. Click "Reset Layout."
   d. Confirm the nodes visually return to algorithmic positions
      immediately (no app restart needed) — this validates Fix 2.
   e. Confirm `layout.overrides.json` is now deleted/empty — this validates
      Fix 1.
   f. Restart the app, reload the same project, confirm the layout is
      still the freshly-generated one (not the old dragged positions) —
      this is the regression case Fix 1 specifically guards against.
3. Re-run the full existing test suite
   (`tests/MermaidDiagramExporter.Tests/`), in particular
   `CacheInvalidationThresholdTests.cs` and
   `ManualLayoutOverridesRoundtripTests.cs`, to confirm no existing
   persistence assumption broke.

## Non-goals / explicitly out of scope for this fix

- This fix does not change `ManualLayoutApplier.ApplyOverrides`'s logic at
  all — that class is not the source of the bug and should not be touched.
- This fix does not change anything about how drags are recorded
  (`GraphCanvas.cs` lines ~665/690, `ManualOverrides.SetDelta`) — unrelated.
- This fix is independent of the layout-algorithm rewrite in files
  `04`–`09`. Ship this fix on its own; do not block it on the algorithm
  work.

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\02-bug-reset-layout.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\03-bug-minimap.md
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

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\03-bug-minimap.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\04-algorithm-gap-analysis.md
# 04 — Algorithm Gap Analysis: `LayeredLayoutEngine` vs. Dagre

This file connects `01-research-findings.md` (what dagre does) to the actual
code in this repository (what it does instead), file by file, method by
method, so the redesign in `05`–`08` is justified by specific evidence, not
vibes.

## Summary judgment

`LayeredLayoutEngine.cs` implements a **real, coherent layered-graph-drawing
algorithm** — it is not broken or buggy in the sense of bug `02`/`03`. It
correctly does longest-path-style rank relaxation and reuses a genuinely
dagre-equivalent WMedian + transpose crossing reducer
(`CrossingReductionService`). The problem is **architectural granularity**:
it solves the layout as **two separate problems glued together** —
"arrange the clusters" then "arrange nodes inside each cluster" — where
dagre solves it as **one problem** ("arrange all nodes, with clusters as
containment + contiguity constraints on that single arrangement").

This two-pass structure is the root cause of every visual complaint in the
screenshots: tangled cross-namespace edges, clusters that don't end up near
the other clusters they're most connected to, and inconsistent/cramped
internal cluster layouts that don't reflow the way Mermaid's do.

## 4.1 Rank assignment — the core divergence

### What `LayeredLayoutEngine` does today

```csharp
private static ComponentLayout BuildComponentLayout(...)
{
    var metrics = BuildClusterMetrics(component, graph, clusterIdByNodeId);
    var ranks = AssignClusterRanks(component, graph, clusterIdByNodeId, metrics);
    var clustersByRank = GroupClustersByRank(component, ranks, metrics);
    // ... lay out each cluster's internal nodes via BuildClusterLayout,
    // ... then place whole clusters side-by-side within their rank, and
    // ... ranks (columns) of clusters left-to-right.
}
```

`AssignClusterRanks` ranks **clusters** (i.e., whole namespaces) against each
other, using `BuildClusterMetrics` to pre-aggregate every cross-cluster edge
into a single `InWeight`/`OutWeight`/`ConnectedIds` score **per cluster**.
Individual node-to-node edges are thrown away for ranking purposes the
moment they cross a cluster boundary — only their aggregate contribution to
the two clusters' scores survives.

Then, **separately**, `BuildClusterLayout` → `BuildStructuredCoreLayout` →
`AssignLocalNodeRanks` ranks the **individual nodes inside one cluster**, but
only using edges where `nodeIds.Contains(edge.FromNodeId) &&
nodeIds.Contains(edge.ToNodeId)` are both true for *that single cluster's*
node set (implicitly, since `AssignLocalNodeRanks` is called with `nodes`
already filtered to one cluster, and `graph.Edges` includes inter-cluster
edges that simply never match because one endpoint isn't in `nodeIds`).

### What dagre does

Per `01-research-findings.md` §5: `rank(util.asNonCompoundGraph(g))` runs
**once**, over **every real node in the entire graph plus nesting-graph
border dummies**, using **every edge**, regardless of which cluster either
endpoint belongs to. A node's rank is influenced by literally everything
it's connected to, cluster membership or not. Cluster membership only
enters as an additional *constraint* (via the nesting graph's high-weight
edges, §4 of the research file) that biases — but never fully overrides —
the ranker toward keeping a cluster's members rank-contiguous.

### Why this specific difference produces the screenshot symptom

Picture two namespaces, `PFE.Character` and `PFE.Systems.Combat`, where:
- `PFE.Character.CharacterAppearance` has a strong, direct association to
  `PFE.Systems.Combat.ICriticalHitSystem` (one real edge).
- But `PFE.Character` as a whole namespace has many other internal edges and
  only this one edge out to `PFE.Systems.Combat`.

In the current engine: `BuildClusterMetrics` sees this as "cluster
`PFE.Character` has `OutWeight += 1` toward... wait, it doesn't even track
*which* cluster an edge points to beyond the aggregate `InWeight`/`OutWeight`
scalar and the `ConnectedIds` set used for ordering, not directional rank
pull strength per neighboring cluster." The one specific edge between
`CharacterAppearance` and `ICriticalHitSystem` has exactly the same influence
on the cluster-level rank as it would if it were ten times weaker or
stronger relative to other cross-cluster edges, **and**, critically, it has
**zero influence** on where `CharacterAppearance` sits *relative to other
nodes inside its own cluster* — that's decided purely by intra-cluster edges
in a completely separate ranking pass that has no knowledge this
cross-cluster edge exists.

In dagre's model: this edge directly pulls on both individual nodes' ranks
in the **same unified solve** that's also placing every other node, so a
node with an important external connection naturally migrates toward the
side/rank of its cluster that's closest to that connection, and clusters
with many mutual connections naturally migrate toward each other. This is
*why* Mermaid's namespace-grouped diagrams tend to show namespaces "facing"
each other across their most-connected sides, and why individual classes
within a namespace often end up positioned near the edge of their box that
faces their most important external dependency — none of that is special
cluster-aware logic in dagre, it falls out for free from solving rank as one
global problem.

## 4.2 Ordering — partially equivalent, missing the compound-aware half

### What this codebase does today

`CrossingReductionService.RefineRows` is called from
`LayeredLayoutEngine.RefineStructuredRows`, which is itself only invoked
**within `BuildStructuredRows`, which is only called per-cluster** (from
`BuildStructuredCoreLayout`, which is called once per cluster from
`BuildClusterLayout`). There is **no call to `CrossingReductionService`
anywhere that operates across multiple clusters at once** — ordering, like
ranking, is solved completely independently inside each cluster, with no
mechanism at all for "given that cluster A and cluster B are adjacent in the
final drawing, order A's nodes and B's nodes so edges between them don't
cross."

### What dagre does

Per `01-research-findings.md` §7: ordering runs across **the whole rank**,
which may contain nodes from several different clusters side by side, with
the WMedian/barycenter/transpose machinery operating on **all of them
together** — your `CrossingReductionService`'s actual sort/swap logic is the
right algorithm, it's just never invoked at the scope dagre invokes it at
(globally, with subgraph-as-unit treatment per §7.4) — it's only ever
invoked at the narrower scope of "nodes within one single cluster."

### Verdict

This is good news for implementation effort: **you do not need to rewrite
`CrossingReductionService`'s core sorting algorithm.** You need to (a) call
it at the right scope (across the whole rank, all clusters' nodes that share
that rank, together), and (b) add the "treat each cluster's nodes as one
sortable group, sort groups first, then expand and recursively sort within
each group" two-level logic described in research file §7.4, which is new
logic, not a rewrite of existing logic. Detailed in
`07-implementation-ordering-and-borders.md`.

## 4.3 Within-cluster node layout — reasonable structure, wrong inputs

`BuildStructuredCoreLayout`'s actual mechanics (rank by local longest-path,
spread weak-association components via BFS distance in
`ApplyWeakAssociationRankSpread`, wrap long rows via `WrapStructuredRow`,
center/indent rows via `OffsetStructuredRows`) are reasonable, somewhat
bespoke heuristics for fitting a moderate number of class boxes into a
roughly-square namespace panel — closer in spirit to a constrained grid
packer than to dagre, but not unreasonable as a *local, within-one-cluster*
layout strategy once the cluster's node set and ranks are correct.

**Verdict: do not throw this away.** Once rank assignment and ordering are
fixed to operate on the **unified global graph** (per `05`–`07`), the
individual real nodes *inside* one cluster will already have correct global
ranks and a correct relative order from that unified solve. At that point,
`BuildStructuredCoreLayout`'s row-wrapping/centering logic can be **repurposed
as the final local "pack these already-rank-and-order-determined nodes into
rows with sensible wrapping" step**, rather than as a from-scratch ranking
pass. This significantly de-risks the rewrite: the visually-fiddly part
(wrapping, indentation, centering bias) doesn't need to be re-derived from
research, it already exists and works; only the part that decides *which*
rank and *which* relative order each node gets needs to change.

## 4.4 Cluster-as-component layout — the macro arrangement is also two-pass

`LayeredLayoutEngine.Run` (the outermost method) does:

```csharp
var components = ComponentSplitter.SplitClusters(graph); // connected components of CLUSTERS, not nodes
foreach (var component in components) {
    var layout = BuildComponentLayout(...); // ranks clusters, lays out each cluster internally
    // ...place this whole component's bounding box into a row-wrapped grid of components...
}
```

`ComponentSplitter.SplitClusters` finds connected components **at the
cluster level** (two clusters are "connected" if any edge connects a node in
one to a node in the other) — this is a reasonable thing to do (genuinely
disconnected subgraphs in a real codebase, e.g. an isolated utility
namespace with zero edges to anything else, should indeed be laid out
separately and tiled rather than forced into the same coordinate space as
the main connected mass), but it reinforces the same architectural pattern:
clusters are the primary unit of reasoning, nodes are secondary.

**Verdict:** Component splitting (genuinely disconnected subgraphs) is
fine to keep **as long as it's done at the node level, producing components
that contain whole clusters intact** (a cluster's nodes can never be split
across two different node-level connected components unless the cluster
itself is genuinely disconnected internally, which would be unusual for a
namespace but not impossible — e.g. two completely unrelated static utility
classes that happen to share a namespace). See
`05-target-architecture.md` for the precise redefinition.

## 4.5 What "good" looks like — translating image 1 → image 2 visually

Image 1 (the user's current plugin output, screenshotted) shows: clusters
scattered with long edges crossing between far-apart clusters, and faint
green "association" edges in particular crossing huge distances across the
canvas. This is the direct visual signature of cluster-level-only ranking:
the macro arrangement of clusters has no fine-grained pull toward where
their actual most-connected neighbors are, so two heavily-connected clusters
can easily end up far apart if their *aggregate* edge weight rank put them
in distant cluster-ranks even though one specific node-to-node connection
between them is actually very important.

Image 2 (Mermaid-style layout, same underlying codebase, different
renderer/algorithm — visible in the same screenshot, since this is the
plugin's own "Mermaid Diagram Exporter" tool comparing its custom canvas
output against an exported Mermaid `.mmd` rendering) shows tighter, more
locally-coherent clustering with shorter edges and namespaces positioned
nearer to their most significant neighbors — exactly what unified-graph
ranking produces.

## 4.6 Concrete file-level task list (forward reference into 05–08)

| File | Action | Where specified |
|---|---|---|
| `LayeredLayoutEngine.cs` | Replace `AssignClusterRanks`/`BuildClusterMetrics`/`GroupClustersByRank` (cluster-level ranking) with a unified node+border-dummy ranker | `06` |
| `LayeredLayoutEngine.cs` | Replace the per-cluster-only invocation of ordering with a per-rank, all-clusters-together invocation, adding subgraph-as-unit two-level sorting | `07` |
| `LayeredLayoutEngine.cs` | Repurpose (not discard) `BuildStructuredCoreLayout`'s row-wrap/centering logic as the final local packing step once global rank+order are known | `06`, `07`, `08` |
| `ComponentSplitter.cs` | Redefine "component" at the node level (clusters stay intact, never split across components) | `05` |
| `CrossingReductionService.cs` | Extend with subgraph-as-unit barycenter computation and recursive expand-then-sort, per research §7.4 | `07` |
| New: nesting/border-node builder | New file/class, builds the per-cluster border dummy chain described in research §4 | `06` |
| `GraphLayoutCoordinator.cs` | Wire the new engine in behind a feature flag alongside the old one | `08` |
| `Layout/Post/*` (Bounds/Overlap/TitleMargin passes) | Mostly unchanged; adapt to read cluster bounds from contained-node-and-border-dummy union instead of whatever they read today (verify against new engine's output shape) | `08` |

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\04-algorithm-gap-analysis.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\05-target-architecture.md
# 05 — Target Architecture: Unified Compound-Aware Layout Engine

This file defines **what to build**: new types, new contracts, and how they
plug into the existing `LayoutGraph`/`LayoutNode`/`LayoutCluster` model in
`Layout/LayoutModels.cs` (reproduced and referenced below — do not redefine
those types, extend around them). `06`, `07`, `08` are the step-by-step
implementation of this architecture; this file is the blueprint they all
implement against, so if an implementer is unsure "does my code match the
plan," the answer is "does it match this file's contracts."

## Design principle (restated from `04`'s conclusion)

Replace **two-pass (rank clusters, then rank nodes within each cluster)**
with **one-pass (rank all real nodes + border dummies together; clusters are
constraints on that one pass, not a separate prior pass)**. Everything in
this file exists to make that one-pass model concrete.

## 5.1 New concept: the "Compound Layout Graph"

Introduce a new internal representation used **only inside the new ranking
and ordering code** (it is built from `LayoutGraph` at the start of the new
engine's `Run` and discarded at the end — it does not replace `LayoutGraph`,
`LayoutNode`, etc. anywhere else in the codebase):

```csharp
namespace MermaidDiagramExporter.Gui.Layout.Compound;

/// <summary>
/// A node in the unified ranking/ordering graph. Either a real LayoutNode,
/// a border dummy (top/bottom-of-cluster marker for one rank), or an edge
/// dummy (intermediate point of an edge that spans more than one rank).
/// This is the moral equivalent of dagre's single flattened-but-parent-tagged
/// graph used during rank/order (see 01-research-findings.md §2, §4).
/// </summary>
public enum CompoundNodeKind { Real, ClusterBorderTop, ClusterBorderBottom, EdgeSegment }

public sealed class CompoundNode
{
    public string Id { get; set; } = "";             // stable, unique across the whole compound graph
    public CompoundNodeKind Kind { get; set; }
    public string? SourceLayoutNodeId { get; set; }   // set iff Kind == Real
    public string? OwningClusterId { get; set; }       // the cluster this node (or border) directly belongs to; null = top-level / no cluster
    public string? OriginalEdgeId { get; set; }         // set iff Kind == EdgeSegment — which LayoutEdge this dummy is a segment of
    public float Width { get; set; }
    public float Height { get; set; }

    // Filled in by the ranking phase (06):
    public int Rank { get; set; }

    // Filled in by the ordering phase (07):
    public int OrderInRank { get; set; }

    // Filled in by the coordinate-assignment phase (08):
    public float X { get; set; }
    public float Y { get; set; }
}

public sealed class CompoundEdge
{
    public string FromId { get; set; } = "";   // CompoundNode.Id
    public string ToId { get; set; } = "";     // CompoundNode.Id
    public float Weight { get; set; } = 1f;    // higher = ranker tries harder to keep this edge short
    public int MinRankSpan { get; set; } = 1;  // normally 1; nesting/border edges may differ, see 06
    public bool IsReversedForRanking { get; set; } // true if this edge was flipped to break a cycle (06, ranking step)
}

public sealed class CompoundGraph
{
    public List<CompoundNode> Nodes { get; } = new();
    public List<CompoundEdge> Edges { get; } = new();

    // Cluster containment tree, copied in from LayoutGraph.Clusters at build time.
    // Key = clusterId, Value = parent clusterId or null if top-level.
    public Dictionary<string, string?> ClusterParent { get; } = new();
    // Key = clusterId, Value = direct child cluster ids (namespace nesting).
    public Dictionary<string, List<string>> ClusterChildren { get; } = new();
    // Key = clusterId, Value = the CompoundNode.Id of that cluster's top and bottom
    // border chain at each rank it spans, built during 06. Used by 07 and 08.
    public Dictionary<string, ClusterBorderChain> ClusterBorders { get; } = new();
}

public sealed class ClusterBorderChain
{
    public string ClusterId { get; set; } = "";
    // Rank -> the border CompoundNode id at that rank, for the cluster's "low" side
    // (left, if rankdir is the LeftToRight direction this codebase already defaults to —
    // see LayoutOptions.Direction) and "high" side respectively.
    public Dictionary<int, string> LowBorderByRank { get; } = new();
    public Dictionary<int, string> HighBorderByRank { get; } = new();
    public int MinRank { get; set; }
    public int MaxRank { get; set; }
}
```

**Why a new namespace (`Layout.Compound`) instead of extending
`LayoutNode`/`LayoutEdge` directly:** `LayoutNode`/`LayoutEdge`/`LayoutGraph`
are used throughout the codebase (rendering, export, hit-testing, manual
overrides) and adding rank/order/border-dummy concerns directly onto them
would leak layout-internal bookkeeping into unrelated consumers. The
existing `LayoutNodeRole` enum (`Real`, `ClusterInboundAnchor`,
`ClusterOutboundAnchor`, `SelfLoopHelper`) already shows the project's
convention of using dummy/anchor node roles **on the public `LayoutNode`
type** — that convention can stay for whatever those existing roles are
used for (self-loop routing, etc.), but the *new* rank/order machinery
should not have to coexist with or special-case those existing roles inside
its own core loop. Keep them separate; convert at the boundary (see §5.5).

## 5.2 Building the Compound Graph (the "nesting graph" equivalent)

A new class, `Layout/Compound/CompoundGraphBuilder.cs`, with one entry
point:

```csharp
public static CompoundGraph Build(LayoutGraph graph, LayoutOptions options)
```

Responsibilities (each detailed procedurally in `06`):

1. Create one `CompoundNode { Kind = Real }` per `LayoutNode` in
   `graph.Nodes` where `Role == LayoutNodeRole.Real` (skip existing
   anchor/self-loop helper roles for now — those are handled by the
   existing `Routing/EdgeRoutingService.cs` machinery downstream and are out
   of scope for rank/order; see §5.6 for exactly how they're excluded and
   reintroduced later without conflicting).
2. Populate `ClusterParent`/`ClusterChildren` directly from
   `graph.Clusters[i].ParentClusterId`/`ChildClusterIds` (already computed by
   the existing `ClusterHierarchyPass` — reuse it as-is, do not recompute).
3. For every direct `LayoutEdge` where `Role == LayoutEdgeRole.Direct`,
   create one `CompoundEdge` between the two corresponding `CompoundNode`s,
   with `Weight` taken from edge kind (reuse
   `LayeredLayoutEngine.GetEdgeWeight(Core.TypeEdgeKind)` — this existing
   private method's weight table, Inheritance=3, Implements=2.5, else 1, is
   reasonable and dagre-equivalent in spirit since dagre also supports
   per-edge weight to bias the ranker toward keeping "more important" edges
   shorter; **move this method to a shared static utility** — e.g.
   `Layout/LayoutEdgeWeights.cs` — so both the old and new engines can use
   the identical table during the transition period described in `08`).
4. **Cluster border chain construction** — for every cluster (processed
   leaf-to-root so nested namespaces' borders are built before their
   parent's, since a parent cluster's border chain must span at least the
   rank range its children's border chains span): this is the literal
   nesting-graph mechanism from `01-research-findings.md` §4. Full procedure
   in `06`.

## 5.3 The three new phases, as classes implementing existing pass interfaces where reasonable

The codebase already has `ILayoutPass`/`IPostLayoutPass` interfaces (see
`Layout/ILayoutPass.cs`, `Layout/IPostLayoutPass.cs`) used for the
`LayoutPipeline`/`PostLayoutPipeline` *preparation* and *post-processing*
stages around the core engine. **Do not try to force the new ranking and
ordering logic into those interfaces** — `ILayoutPass` operates on
`LayoutGraph -> LayoutGraph` (graph-shape transforms before layout runs),
which is the wrong shape for "compute numeric rank/order/position
properties." Instead, the new engine is a new implementation of the
existing `IGraphLayoutEngine` interface (see `Layout/IGraphLayoutEngine.cs`)
— i.e. a sibling to `LayeredLayoutEngine` and `SimpleColumnLayoutEngine`,
selected by `GraphLayoutCoordinator` per `08`'s feature-flag plan, not a
replacement plugged into the existing pass pipelines.

```csharp
namespace MermaidDiagramExporter.Gui.Layout;

public sealed class CompoundLayeredLayoutEngine : IGraphLayoutEngine
{
    public LayoutResult Run(LayoutGraph graph, LayoutOptions options)
    {
        var compound = Compound.CompoundGraphBuilder.Build(graph, options);
        Compound.RankAssignment.Run(compound, options);          // 06
        Compound.OrderAssignment.Run(compound, options);         // 07
        Compound.CoordinateAssignment.Run(compound, options);    // 08
        return Compound.CompoundResultProjector.Project(compound, graph, options); // 08
    }
}
```

Each of `RankAssignment`, `OrderAssignment`, `CoordinateAssignment` is a
static class with one `Run(CompoundGraph, LayoutOptions)` entry point that
mutates the `CompoundNode.Rank`/`OrderInRank`/`X`/`Y` fields in place. This
mirrors dagre's own pipeline shape (a sequence of mutating passes over one
shared graph object) and keeps each phase independently testable (you can
construct a small `CompoundGraph` by hand in a unit test, run just
`RankAssignment.Run`, and assert on `.Rank` values without needing the
other two phases — see `09-validation-and-test-plan.md`).

## 5.4 Cluster geometry as a derived projection, not an input

Per research file §10: cluster rectangles must be **computed from the
positions of their contents after coordinate assignment**, never estimated
upfront. `CompoundResultProjector.Project` (in `08`) is responsible for:

1. For every cluster, take the union of:
   - the final `(X, Y, Width, Height)` of every `Real` `CompoundNode` whose
     `OwningClusterId` is this cluster (directly, or transitively via a
     descendant cluster — namespace nesting),
   - the final position of every `ClusterBorderTop`/`ClusterBorderBottom`
     `CompoundNode` belonging to this cluster's border chain.
2. Pad outward by `options.GroupLeftPadding`/`GroupTopPadding`/etc. (reuse
   existing `LayoutOptions` fields — no new spacing constants needed for
   this part).
3. Emit this as the `LayoutResult.ClusterBounds[clusterId]` entry, in the
   exact same shape the old engine already produces, so that **every
   downstream consumer (`PostLayoutPipeline`'s passes, `EdgeRoutingService`,
   rendering) needs zero changes** — they consume `LayoutResult`, not
   whichever engine produced it.

This is the single most important compatibility guarantee in this plan:
**`LayoutResult` is the stable contract.** Both the old and new engines
produce the same `LayoutResult` shape; everything downstream of "an engine
ran" is untouched by this entire rewrite.

## 5.5 Reconciling with existing dummy-node roles (anchors, self-loop helpers)

The existing code has `LayoutNodeRole.ClusterInboundAnchor` /
`ClusterOutboundAnchor` / `SelfLoopHelper`, produced by
`Passes/RepresentativeAnchorSelectionPass.cs` and
`Passes/SelfLoopExpansionPass.cs` respectively, **before** the core engine
runs (they're part of the `LayoutPipeline` preparation passes in
`GraphLayoutCoordinator`, which both the old and new engine receive as
input). Two options, pick based on what's actually simplest once you're in
the code (this is intentionally left as an implementation-time decision,
not dictated here, because the right call depends on how entangled these
roles turn out to be in practice):

- **Option A (recommended starting point):** `CompoundGraphBuilder.Build`
  includes these non-`Real` roles as `CompoundNode`s too (treat
  `ClusterInboundAnchor`/`ClusterOutboundAnchor`/`SelfLoopHelper` as
  additional `CompoundNodeKind` values, or just lump them all as `Real` for
  ranking/ordering purposes since they already carry `Width`/`Height` and
  participate in edges like any other node) — this is the path of least
  resistance and keeps existing anchor/self-loop *intra-cluster-ordering*
  behavior working without modification, since they'll just be additional
  nodes that get ranked/ordered/positioned by the same unified machinery as
  everything else.
- **Option B (only if Option A causes specific, observed problems during
  `09` validation):** exclude them entirely from the compound graph, run
  rank/order/position on `Real` nodes only, and have a final adaptation
  step that re-inserts anchor/self-loop helper nodes adjacent to their
  associated real node using the existing logic in
  `RepresentativeAnchorSelectionPass`/`SelfLoopExpansionPass` (which already
  knows how to compute their relative placement) after the unified
  rank/order/position pass completes.

Do not decide between A and B in the abstract — implement A first (it's
strictly less code), and only fall back to B if validation in `09` turns up
a concrete defect traceable to anchors/self-loop-helpers being mixed into
the main ranking pool.

## 5.6 Edge dummy-node normalization (new, needed for ordering quality)

Per research file §6: any `CompoundEdge` whose endpoints end up more than 1
rank apart needs intermediate `EdgeSegment` dummy `CompoundNode`s inserted,
one per skipped rank, chained together, so the ordering phase can route
around them like any other node. This must happen **after rank assignment**
(you don't know how many ranks an edge spans until ranks are assigned) and
**before ordering** (ordering needs the dummies present to count crossings
correctly). Concretely this is a step inside `RankAssignment.Run`'s
contract — specified precisely in `06` — not a separate phase, because
"insert dummies for edges that turned out to be long" is mechanically
coupled to "ranks were just assigned," and trying to make it a fully
separate phase would mean re-deriving which edges are long from scratch
with no benefit.

## 5.7 Component splitting, redefined (per `04.4`)

Replace `ComponentSplitter.SplitClusters` (cluster-level connectivity) with
a node-level version that still respects cluster atomicity:

```csharp
namespace MermaidDiagramExporter.Gui.Layout;

internal static class ComponentSplitter
{
    /// <summary>
    /// Splits the graph into connected components at the NODE level (two real
    /// nodes are connected if any direct edge connects them, regardless of
    /// cluster), then groups each component's clusters together. A cluster
    /// whose nodes span two node-level components is itself split into two
    /// per-component partial clusters (rare — e.g. a namespace containing two
    /// totally unrelated static helper classes) — see 06 for the exact
    /// partial-cluster handling rule.
    /// </summary>
    public static List<ConnectedComponent> SplitNodes(LayoutGraph graph) { /* ... */ }
}

public sealed class ConnectedComponent
{
    public List<string> NodeIds { get; set; } = new();
    public List<string> ClusterIds { get; set; } = new(); // clusters with >=1 node in this component
}
```

This changes the signature consumers see (`LayeredLayoutEngine.Run`'s
top-level loop currently does `foreach (var component in
ComponentSplitter.SplitClusters(graph))` where `component` is
`IReadOnlyList<LayoutCluster>`) — but since the *new* engine
(`CompoundLayeredLayoutEngine`) is the only consumer of the new
`SplitNodes`, and the *old* engine (`LayeredLayoutEngine`) keeps using the
existing `SplitClusters` unchanged (see `08`'s feature-flag plan — the old
engine is kept as-is, untouched, as a fallback), **there is no breaking
change here**: add `SplitNodes` as a new method alongside the existing
`SplitClusters`, don't replace/rename the old one.

## 5.8 What does NOT change

To keep this plan's scope honest and prevent over-engineering:

- `LayoutGraph`, `LayoutNode`, `LayoutEdge`, `LayoutCluster`,
  `LayoutSubgraph`, `LayoutResult`, `LayoutEdgePath`, `ClusterTitleMetrics`,
  `LayoutSpacingProfile`, `LayoutGraphMetadata`, `LayoutClusterVisual` —
  **zero changes.** These are the stable public contract.
- `LayoutOptions.cs` — no new required fields for the core algorithm (rank
  spacing, node spacing, etc. all already exist and are reused as-is). One
  new **optional** field is added in `08` purely to select which engine runs
  (`UseCompoundLayoutEngine` or equivalent name) — additive, defaults to
  preserving current behavior until the new engine is validated.
- `NamespaceClusterBuilder.cs`, `Passes/ClusterHierarchyPass.cs`,
  `Passes/ExternalConnectionAnalysisPass.cs`,
  `Passes/RepresentativeAnchorSelectionPass.cs`,
  `Passes/SelfLoopExpansionPass.cs`,
  `Passes/SubgraphDirectionSelectionPass.cs`,
  `Passes/RecursiveSpacingPass.cs`,
  `Passes/BoundaryEdgeNormalizationPass.cs`,
  `Passes/MeasurementPreparationPass.cs`,
  `LayoutMeasurementService.cs`, `LayoutGraphFactory.cs` — all of these run
  **before** the core engine as part of `LayoutPipeline` and are entirely
  unaffected; both old and new engines receive their output identically.
- `Layout/Post/*` (the `PostLayoutPipeline` passes) — unaffected per §5.4's
  compatibility guarantee, *provided* the new engine's `LayoutResult` output
  shape genuinely matches what these passes expect. Validate this
  explicitly in `09` rather than assuming it.
- `Routing/EdgeRoutingService.cs`, `Routing/ClusterBoundaryClipper.cs` — no
  changes planned; revisit only if `09` validation finds edge routing
  quality regressed (it should improve or stay neutral, not regress, since
  it receives better-organized node positions as input — but verify, don't
  assume).
- `CanvasRenderer.cs`, `GraphCanvas.cs`, `MinimapControl.axaml.cs`,
  `HitTestService.cs` — pure rendering/interaction, consume `GraphNode`/
  `GraphEdge` (produced by `LayoutEngine.cs` from `LayoutResult`), totally
  insulated from this change by the `LayoutResult` compatibility guarantee.
- `ManualLayoutApplier.cs`, `ManualLayoutOverrides.cs` — operate on
  `LayoutResult` after the engine runs (see `LayoutEngine.Layout`'s call to
  `ManualLayoutApplier.ApplyOverrides(result, ...)` after `_coordinator.CreateLayout(...)`),
  so they don't care which engine produced the result.

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\05-target-architecture.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\06-implementation-rank-and-nesting.md
# 06 — Implementation: Rank Assignment and Cluster Nesting

This is the highest-risk file in the whole plan. Read
`01-research-findings.md` §2–§6 and `05-target-architecture.md` §5.1–5.2,
§5.6 before starting — this file assumes that context and does not
re-explain `CompoundGraph`/`CompoundNode`/`CompoundEdge` from scratch.

Implements: `Layout/Compound/CompoundGraphBuilder.cs` (the build step) and
`Layout/Compound/RankAssignment.cs` (the rank step). These run in sequence;
`CompoundGraphBuilder.Build` is called once to produce the `CompoundGraph`,
then `RankAssignment.Run` mutates it in place.

## Step 1 — `CompoundGraphBuilder.Build`: real nodes and direct edges

```csharp
public static CompoundGraph Build(LayoutGraph graph, LayoutOptions options)
{
    var compound = new CompoundGraph();
    var idByLayoutNodeId = new Dictionary<string, string>();

    // 1a. Real nodes (see 05 §5.5 — Option A: include anchor/self-loop roles too, for now)
    foreach (var node in graph.Nodes)
    {
        var compoundId = $"real:{node.Id}";
        idByLayoutNodeId[node.Id] = compoundId;
        compound.Nodes.Add(new CompoundNode
        {
            Id = compoundId,
            Kind = CompoundNodeKind.Real,
            SourceLayoutNodeId = node.Id,
            OwningClusterId = string.IsNullOrEmpty(node.ClusterId) ? null : node.ClusterId,
            Width = node.Width > 0 ? node.Width : node.MeasuredWidth,
            Height = node.Height > 0 ? node.Height : node.MeasuredHeight,
        });
    }

    // 1b. Cluster hierarchy (copy, do not recompute — ClusterHierarchyPass already ran)
    foreach (var cluster in graph.Clusters)
    {
        compound.ClusterParent[cluster.Id] =
            string.IsNullOrEmpty(cluster.ParentClusterId) ? null : cluster.ParentClusterId;
        compound.ClusterChildren[cluster.Id] = cluster.ChildClusterIds.ToList();
    }

    // 1c. Direct edges only. (BoundarySourceLink / SelfLoop* roles handled separately —
    //     they are routing concerns for EdgeRoutingService, not ranking concerns. Confirm
    //     during implementation that excluding them from CompoundEdge entirely, while still
    //     including their endpoint NODES per §5.5 Option A, does not orphan any node from
    //     having edges for ranking purposes — if a node's only edges are non-Direct, it will
    //     rank at 0 by default per the baseline step below, which is an acceptable fallback.)
    foreach (var edge in graph.Edges)
    {
        if (edge.Role != LayoutEdgeRole.Direct) continue;
        if (!idByLayoutNodeId.TryGetValue(edge.FromNodeId, out var fromId)) continue;
        if (!idByLayoutNodeId.TryGetValue(edge.ToNodeId, out var toId)) continue;
        if (fromId == toId) continue; // self-loops handled by SelfLoopExpansionPass upstream, skip here

        compound.Edges.Add(new CompoundEdge
        {
            FromId = fromId,
            ToId = toId,
            Weight = LayoutEdgeWeights.GetWeight(edge.Kind), // moved per 05 §5.2.3
            MinRankSpan = 1,
        });
    }

    BuildClusterBorders(compound, graph, options); // Step 2, below
    return compound;
}
```

## Step 2 — Cluster border chains (the nesting-graph mechanism)

This is the literal implementation of `01-research-findings.md` §4. Process
clusters **leaf-first** (deepest-nested namespaces before their parents) so
a parent's border span calculation can see its children's already-built
spans.

```csharp
private static void BuildClusterBorders(CompoundGraph compound, LayoutGraph graph, LayoutOptions options)
{
    var clustersByDepthDescending = graph.Clusters
        .OrderByDescending(c => GetClusterDepth(c.Id, compound.ClusterParent))
        .ToList();

    foreach (var cluster in clustersByDepthDescending)
    {
        // 2a. Collect every CompoundNode directly or transitively owned by this cluster.
        var memberCompoundNodeIds = GetTransitiveMemberCompoundNodeIds(cluster.Id, compound, graph);
        if (memberCompoundNodeIds.Count == 0) continue;

        // 2b. Create exactly TWO new border CompoundNodes for this cluster (not yet rank-assigned —
        //     rank assignment in Step 3 treats these like any other node and WILL assign them ranks).
        //     These correspond to dagre's per-subgraph border dummy chain (research §4) — but note:
        //     dagre creates ONE border node PER RANK the subgraph spans, chained together, built
        //     AFTER initial ranking is known. We cannot know "every rank this cluster spans" before
        //     ranking runs (that's circular). Resolve this exactly as described in Step 2c below.
        var topBorderId = $"borderTop:{cluster.Id}";
        var bottomBorderId = $"borderBottom:{cluster.Id}";
        compound.Nodes.Add(new CompoundNode { Id = topBorderId, Kind = CompoundNodeKind.ClusterBorderTop, OwningClusterId = cluster.Id, Width = 0, Height = 0 });
        compound.Nodes.Add(new CompoundNode { Id = bottomBorderId, Kind = CompoundNodeKind.ClusterBorderBottom, OwningClusterId = cluster.Id, Width = 0, Height = 0 });

        // 2c. High-weight edges from the top border to EVERY member node, and from EVERY member
        //     node to the bottom border. This is the part of dagre's nesting graph that biases the
        //     RANKER (not just ordering) to keep a cluster's members rank-contiguous and bounded
        //     between these two markers — see research §4 point 3. Use a high weight constant
        //     (LayoutOptions does not currently have one; add ClusterContainmentEdgeWeight, default
        //     8x the strongest normal edge weight i.e. ~24, so containment dominates ranking pressure
        //     from ordinary inheritance/association edges without becoming a hard constraint that
        //     could make the ranker infeasible).
        foreach (var memberId in memberCompoundNodeIds)
        {
            compound.Edges.Add(new CompoundEdge { FromId = topBorderId, ToId = memberId, Weight = options.ClusterContainmentEdgeWeight, MinRankSpan = 1 });
            compound.Edges.Add(new CompoundEdge { FromId = memberId, ToId = bottomBorderId, Weight = options.ClusterContainmentEdgeWeight, MinRankSpan = 1 });
        }

        // 2d. If this cluster has a parent, also constrain the parent's border-to-border span to
        //     contain this cluster's border-to-border span: parent top border -> this top border,
        //     this bottom border -> parent bottom border, same high weight. This is what makes
        //     nested namespaces nest correctly in rank-space, not just in the final drawn rectangle.
        var parentId = compound.ClusterParent.GetValueOrDefault(cluster.Id);
        if (!string.IsNullOrEmpty(parentId))
        {
            var parentTop = $"borderTop:{parentId}";
            var parentBottom = $"borderBottom:{parentId}";
            // Guaranteed to already exist because we process leaf-first (children before parents)
            // -- wait, that's backwards: we need the PARENT's border nodes to exist when processing
            // the CHILD, but parents are processed AFTER children in leaf-first order. Resolve this
            // by creating ALL clusters' top/bottom border node pairs FIRST in one pass (no edges yet),
            // THEN doing a second pass (still leaf-first, for the per-member containment edges in 2c)
            // that adds the containment and parent-nesting edges. Restructure the loop body above
            // into two explicit passes accordingly when implementing — this note exists because the
            // naive single-pass version described in prose above has exactly this ordering bug, and
            // it's the kind of off-by-pass-ordering mistake this plan exists to help an agent avoid.
            compound.Edges.Add(new CompoundEdge { FromId = parentTop, ToId = topBorderId, Weight = options.ClusterContainmentEdgeWeight, MinRankSpan = 0 });
            compound.Edges.Add(new CompoundEdge { FromId = bottomBorderId, ToId = parentBottom, Weight = options.ClusterContainmentEdgeWeight, MinRankSpan = 0 });
        }
    }
}
```

**Explicit correction baked into the comment above, surfaced here so it
isn't missed:** create every cluster's top/bottom border node pair in **one
upfront pass over all clusters** (no edges yet, any order, since this part
has no dependencies), **then** do the leaf-first pass that wires up
containment edges (2c) and parent-nesting edges (2d). Do not try to do both
in a single combined pass — a single pass requires a parent's border nodes
to exist before a child references them, which conflicts with needing
children processed before parents for the depth-ordering reasons stated at
the top of this section. Two passes resolves the conflict trivially: pass 1
(create all border node pairs, order-independent), pass 2 (wire edges,
leaf-first order matters here, but all referenced nodes already exist from
pass 1 regardless of order).

**`MinRankSpan = 0` on the parent-nesting edges (2d) is intentional, not a
typo:** a parent cluster's top border can legitimately be at the *same* rank
as its child's top border (e.g. rank 0 for both, if the child cluster's
content starts at the very first rank of the parent) — there's no reason to
force a parent's border strictly outside its child's rank range by one full
rank; allowing `MinRankSpan = 0` here just means "parent border must be at a
rank ≤ child's top border / ≥ child's bottom border," not strictly less/greater.
Implement this as a `≤`/`≥` constraint in the ranker (Step 3), not as a
same-direction-as-everything-else `≥ from.Rank + MinRankSpan` constraint —
flag this special case clearly in code comments where it's implemented,
since it's the one place rank constraints go in the "container" direction
rather than the normal "dependency" direction.

### Helper: `GetTransitiveMemberCompoundNodeIds`

```csharp
private static HashSet<string> GetTransitiveMemberCompoundNodeIds(string clusterId, CompoundGraph compound, LayoutGraph graph)
{
    // All LayoutNode.Id where ClusterId == clusterId, PLUS all members of every
    // descendant cluster (transitively), mapped to their "real:{id}" CompoundNode id.
    // Use compound.ClusterChildren for the descendant walk (BFS/DFS, already built in Step 1b).
}
```

## Step 3 — `RankAssignment.Run`: the unified ranker

### 3a. Cycle handling (simplified — see research §3 for why full feedback-arc-set is a stretch goal, not required now)

Use the existing longest-path-relaxation technique
(`LayeredLayoutEngine.AssignClusterRanks`'s loop structure is the right
*technique*, reuse the shape, not the cluster-level scope):

```csharp
public static void Run(CompoundGraph compound, LayoutOptions options)
{
    InitializeBaselineRanks(compound);           // see 3b
    RelaxRanksViaLongestPath(compound, options); // see 3c — bounded-iteration relaxation
    NormalizeRanks(compound);                    // shift so min rank == 0 (reuse existing pattern from LayeredLayoutEngine.NormalizeRanks)
    InsertEdgeSegmentDummies(compound, options);  // see 3d — per 05 §5.6
}
```

### 3b. Baseline ranks

Unlike the old `BuildBaselineRanks` (which bucketed clusters by
in/out-weight balance into `sqrt(count)` groups — a heuristic specifically
shaped for *cluster* counts), the unified baseline is simpler because we now
have one global node-and-dummy graph: **every node starts at rank 0.** The
relaxation step (3c) does all the real work, exactly as dagre's
longest-path ranker does (start everything at 0 / at the source, relax
forward). Do not port the old cluster-bucketing heuristic — it was
compensating for the now-eliminated cluster-level-only ranking problem and
has no equivalent role in the unified model.

```csharp
private static void InitializeBaselineRanks(CompoundGraph compound)
{
    foreach (var node in compound.Nodes) node.Rank = 0;
}
```

### 3c. Relaxation

```csharp
private static void RelaxRanksViaLongestPath(CompoundGraph compound, LayoutOptions options)
{
    var nodeById = compound.Nodes.ToDictionary(n => n.Id);
    // Order edges by weight descending once, up front — same performance reasoning as the
    // existing code's comment ("Hoist sorting outside the loop — O(N log N) once, not O(N² log N)").
    var orderedEdges = compound.Edges.OrderByDescending(e => e.Weight).ToList();

    int maxIterations = compound.Nodes.Count * 2; // same bound the existing code already uses successfully
    for (int i = 0; i < maxIterations; i++)
    {
        bool changed = false;
        foreach (var edge in orderedEdges)
        {
            var from = nodeById[edge.FromId];
            var to = nodeById[edge.ToId];

            // SPECIAL CASE from Step 2d: parent-nesting edges use a "contain" semantic
            // (>= / <=), not a strict "must be later than" semantic. Detect these by
            // MinRankSpan == 0 AND both endpoints being border nodes for a parent/child
            // cluster pair (tag this more explicitly than "MinRankSpan==0" alone if other
            // future edge types might also want MinRankSpan==0 for unrelated reasons --
            // recommend adding an explicit `IsContainment` bool to CompoundEdge instead of
            // inferring it from MinRankSpan, to avoid this ambiguity; revise the CompoundEdge
            // definition in 05 if implementing this and add the field).
            if (edge.IsContainment)
            {
                // parent top border must be <= child top border; parent bottom border must be >= child bottom border.
                // Determine direction (top-edge vs bottom-edge) from which border kind `from`/`to` are.
                if (from.Kind == CompoundNodeKind.ClusterBorderTop && from.Rank > to.Rank)
                {
                    from.Rank = to.Rank; changed = true;
                }
                else if (from.Kind == CompoundNodeKind.ClusterBorderBottom && to.Rank < from.Rank)
                {
                    to.Rank = from.Rank; changed = true;
                }
                continue;
            }

            int proposed = from.Rank + edge.MinRankSpan;
            if (proposed > to.Rank)
            {
                to.Rank = proposed;
                changed = true;
            }
        }
        if (!changed) break;
    }
}
```

**This is structurally identical to the existing
`LayeredLayoutEngine.AssignClusterRanks`'s relaxation loop** — same bounded
iteration count, same "order edges by weight descending, propagate forward,
stop when nothing changes" shape. The only genuinely new logic is the
`IsContainment` branch. This similarity is intentional: it means an
implementer can literally use the existing method as a template and modify
it, rather than writing this from scratch, which reduces risk.

**Why this approximates network simplex well enough for now:** per research
§5, network simplex's *goal* is minimizing total weighted edge length
subject to minimum-length constraints. A longest-path relaxation with
high-weight edges processed first (as written above) tends toward the same
qualitative outcome (heavily-weighted edges end up short, by getting
satisfied early and tightly) even though it isn't provably optimal the way
true network simplex is. This is an accepted, documented simplification —
flagged explicitly as such in `01-research-findings.md` §5's
recommendation. If `09` validation finds specific pathological cases
(e.g. very deep cluster nesting producing excessive rank spans), revisit
with a real network-simplex implementation (tight-tree construction +
cut-value-based leave/enter iteration) as a stretch goal — but do not
pre-optimize for this; ship the simpler version first and measure.

### 3d. Edge segment dummy insertion (per `05` §5.6)

```csharp
private static void InsertEdgeSegmentDummies(CompoundGraph compound, LayoutOptions options)
{
    var nodeById = compound.Nodes.ToDictionary(n => n.Id);
    var longEdges = compound.Edges.Where(e => !e.IsContainment &&
        Math.Abs(nodeById[e.ToId].Rank - nodeById[e.FromId].Rank) > 1).ToList();

    foreach (var edge in longEdges)
    {
        var from = nodeById[edge.FromId];
        var to = nodeById[edge.ToId];
        int direction = to.Rank > from.Rank ? 1 : -1;
        string previousId = edge.FromId;

        for (int r = from.Rank + direction; r != to.Rank; r += direction)
        {
            var dummyId = $"edgeseg:{edge.FromId}->{edge.ToId}:{r}";
            compound.Nodes.Add(new CompoundNode
            {
                Id = dummyId, Kind = CompoundNodeKind.EdgeSegment,
                OriginalEdgeId = edge.FromId + "->" + edge.ToId, // or use a real edge id if CompoundEdge gains one
                Rank = r, Width = 0, Height = 0,
                OwningClusterId = ResolveSegmentOwningCluster(from, to, compound), // see note below
            });
            compound.Edges.Add(new CompoundEdge { FromId = previousId, ToId = dummyId, Weight = edge.Weight, MinRankSpan = 1 });
            previousId = dummyId;
        }
        compound.Edges.Add(new CompoundEdge { FromId = previousId, ToId = edge.ToId, Weight = edge.Weight, MinRankSpan = 1 });

        // Remove the original long edge from compound.Edges (it's now represented by the dummy chain)
        // -- but keep a side-table mapping the dummy chain back to the ORIGINAL LayoutEdge.Id, because
        // 08's CompoundResultProjector needs it to build the final LayoutEdgePath. Add this side-table
        // to CompoundGraph (e.g. Dictionary<string, List<string>> EdgeDummyChains keyed by original
        // edge id) when implementing -- it is not in the 05 type definitions yet because this is the
        // first point in the plan where the need for it becomes concrete. Update 05 retroactively when
        // implementing this step so the type definitions stay in sync with reality.
    }
}
```

**`ResolveSegmentOwningCluster` note:** an edge segment dummy's
`OwningClusterId` should be set so that the ordering phase (`07`) correctly
keeps the segment contiguous with whichever cluster's "lane" it's passing
through — per research §6, this is exactly what lets `EdgeRoutingService`
downstream draw a clean path instead of guessing. The precise rule (if both
endpoints share a cluster, the segment belongs to that cluster; if they
differ, the segment is "between" clusters and belongs to the nearest common
ancestor cluster, or no cluster / top-level if there is none) is specified
in `07-implementation-ordering-and-borders.md` since it's really an ordering
concern (which lane does this dummy sort into) more than a ranking concern
— this function is declared here because rank assignment is where the
dummy gets created, but its body should be written by referring to `07`'s
rule once that file's content is in front of you.

## Step 4 — Partial cluster handling (per `05` §5.7, the redefined `ComponentSplitter.SplitNodes`)

If a cluster's nodes end up split across two different node-level connected
components (rare, e.g. two unrelated static utility classes sharing a
namespace with zero edges connecting them or anything in common), do **not**
try to merge the components back together to keep the cluster whole — that
would defeat the point of component splitting (placing genuinely unrelated
graphs far apart / in a tiled grid rather than forcing a layout to connect
them). Instead:

1. Split the cluster's `NodeIds` into per-component subsets.
2. Build a **separate `LayoutCluster` instance per component**, with a
   disambiguated id (e.g. `"{originalClusterId}#component{N}"`) but the
   **same `Label`** (so the rendered title still reads as the namespace
   name — having "MyNamespace" appear twice, once in each disconnected
   region of the canvas, is the correct and expected outcome here, not a
   bug to hide).
3. Run the full rank/order/position pipeline **once per component**
   (exactly like the old engine's outer `foreach (var component in
   ComponentSplitter...)` loop already does), then tile the components'
   bounding boxes into rows exactly as `LayeredLayoutEngine.Run`'s outer
   loop already does today (`currentX`/`currentY`/`rowHeight` row-wrapping
   logic) — **this outer tiling logic is unchanged and fully reusable**,
   only what happens *inside* `BuildComponentLayout` for a single component
   changes.

## Validation checklist for this file specifically (cross-reference with full suite in `09`)

- [ ] Every `Real` `CompoundNode` ends up with `Rank >= 0` and no
      unbounded/runaway rank (sanity bound: rank should never exceed the
      total real-node count — if it does, there's a relaxation bug, likely
      an undetected cycle being relaxed indefinitely up to the iteration
      cap rather than stabilizing).
- [ ] For every cluster, the rank range
      `[min(member ranks), max(member ranks)]` is fully "owned" — i.e. no
      real node belonging to a **different, non-descendant** cluster has a
      rank strictly inside that range AND is ordered (per `07`) within that
      cluster's contiguous order-block. (Rank overlap between sibling
      clusters is fine and expected; *order* overlap within the same rank
      is what must not happen — this checklist item is necessarily partial
      until `07` is also implemented; revisit it as a joint Rank+Order
      invariant in `09`.)
- [ ] Parent cluster border ranks always satisfy
      `parentTop.Rank <= childTop.Rank` and
      `parentBottom.Rank >= childBottom.Rank` for every direct child.
- [ ] No `CompoundEdge` after `InsertEdgeSegmentDummies` spans more than 1
      rank (this is the actual postcondition that matters — assert it
      directly in a unit test, it's cheap and catches the most likely class
      of implementation bug in this entire file).

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\06-implementation-rank-and-nesting.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\07-implementation-ordering-and-borders.md
# 07 — Implementation: Ordering with Compound (Cluster-Contiguous) Constraints

Prerequisite reading: `01-research-findings.md` §7–§8,
`05-target-architecture.md` §5.1, §5.3, and `06`'s output contract (every
`CompoundNode` has a final `Rank`, every `CompoundEdge` spans exactly 1
rank after dummy insertion).

Implements: `Layout/Compound/OrderAssignment.cs`.

## Goal restated precisely

Per `04.2`'s verdict: **`CrossingReductionService`'s actual sort algorithm
(WMedian + transpose) is correct and reusable.** The job in this file is
narrower than "write a crossing reducer" — it's:

1. Group `CompoundNode`s by `Rank` (this is the **rank-by-rank "layer"
   list** that ordering operates on — equivalent to dagre's `layering`
   array).
2. For each layer, compute an **initial order** (deterministic, not yet
   crossing-minimized).
3. Run the existing median/barycenter + transpose sweeps **across the whole
   layer at once** (all clusters' nodes in that layer together), not
   per-cluster in isolation like the old engine does.
4. **New logic, not in `CrossingReductionService` today:** enforce that
   within each layer's final order, every cluster's member nodes (at that
   rank) form one contiguous block — i.e. add the "treat a subgraph as one
   sortable unit, then expand" two-level technique from research §7.4.

## Step 1 — Build layers

```csharp
public static void Run(CompoundGraph compound, LayoutOptions options)
{
    var layers = compound.Nodes
        .GroupBy(n => n.Rank)
        .OrderBy(g => g.Key)
        .ToDictionary(g => g.Key, g => g.ToList());

    AssignInitialOrder(layers, compound);              // Step 2
    RunOrderingSweeps(layers, compound, options);       // Step 3 + Step 4 combined per sweep
    WriteBackOrderInRank(layers);                       // commit final List<CompoundNode> index -> node.OrderInRank
}
```

## Step 2 — Initial order (DFS-seeded, matches dagre's approach per research §7.1)

```csharp
private static void AssignInitialOrder(Dictionary<int, List<CompoundNode>> layers, CompoundGraph compound)
{
    // DFS from rank-0 nodes (or, more precisely, from nodes with no incoming
    // CompoundEdge -- the "sources" of the DAG), visiting outgoing edges in a
    // stable, deterministic order (e.g. sort candidates by Id when there's a tie),
    // appending each node to its rank's list in visitation order. This gives a
    // starting order where directly-connected nodes already tend to be near each
    // other before any crossing-reduction sweep runs, which is exactly what dagre's
    // own initial-order step is for (a better starting point converges faster and
    // more reliably than starting from arbitrary/insertion order).
    //
    // IMPORTANT: this DFS must walk the CompoundGraph's adjacency directly (real
    // nodes, border nodes, edge-segment dummies all included) -- do not special-case
    // border or dummy nodes out of the DFS, they need an initial position in their
    // layer too.
}
```

This is a standard graph-source DFS; no compound-specific logic needed here
yet — the compound-awareness is entirely in Step 4 (group-then-expand
sorting), which runs after this initial order exists.

## Step 3 + 4 — Sweeps: per-rank sort that treats clusters as units

This is the actual new algorithm. Walk through it carefully — get this
wrong and clusters' nodes will end up interleaved with neighboring clusters'
nodes at the same rank, which is exactly the visual defect this whole
rewrite exists to fix.

### 3a. The existing reusable piece

`CrossingReductionService.ReorderRow(row, adjacentRow, adjacency)` (existing
method, unchanged) takes one layer's node list and the **already-fixed**
adjacent layer's node list, and returns a new ordering of `row` sorted by
median/barycenter of each node's neighbors' positions in `adjacentRow`. This
method's *signature* doesn't change. What changes is **what you pass as
`row`** when a layer contains nodes from multiple clusters.

### 3b. The new two-level sort, precisely

For a given layer (list of `CompoundNode`s at one rank) and a given
already-fixed adjacent layer:

1. **Partition the layer's nodes into groups.** A group is either:
   - a single node with `OwningClusterId == null` (top-level, not in any
     cluster) — its own group of size 1, OR
   - all nodes in this layer sharing the **same most-specific
     `OwningClusterId`** — but careful with nesting: if cluster `Inner` is
     nested inside cluster `Outer`, and this layer has some nodes owned
     directly by `Outer` and some owned by `Inner`, you must group
     **recursively**: first treat all of `Outer`'s transitive members (both
     direct `Outer` members and `Inner` members) as one top-level group for
     the purposes of ordering relative to nodes outside `Outer` entirely;
     **then**, recursively, within that group, treat `Inner`'s members as a
     sub-group relative to `Outer`'s direct (non-`Inner`) members. This is
     literally recursive — write `PartitionIntoGroups` as a function that
     takes a cluster-id-or-null "scope" and returns a tree, not a flat list.
2. **Compute one barycenter/median value per top-level group** (not per
   node yet): take every node in the group, find its neighbors in the fixed
   adjacent layer, and compute the median/barycenter **over all those
   neighbor positions pooled together** (i.e. the group's barycenter is
   computed exactly the way a single node's barycenter would be, just
   pooling all member nodes' neighbor-lists first). Groups with zero
   neighbors in the adjacent layer keep their existing relative position
   (same "no-neighbor" fallback rule `CrossingReductionService.BuildMetric`
   already uses for individual nodes — reuse that exact tie-breaking
   logic, generalized to groups).
3. **Sort the top-level groups** by (median, barycenter, neighbor-count
   descending, existing-index, label, id) — same comparator key order
   `CrossingReductionService.ReorderRow`'s existing
   `.OrderBy(m => ...).ThenBy(...)` chain already uses, just applied to
   groups instead of individual nodes.
4. **Recursively sort within each group** that has sub-structure (i.e. a
   cluster group containing a nested child-cluster sub-group plus direct
   members): apply steps 2–3 again, one level down, using the *same* fixed
   adjacent layer for neighbor lookups (a nested cluster's members still
   have their real neighbor-edges in the adjacent layer; nesting doesn't
   change who their graph-neighbors are, only how they're grouped for
   sorting purposes).
5. **Flatten** the now-fully-sorted group tree back into one ordered list
   of individual `CompoundNode`s — this flattened list is the new order for
   this layer, replacing whatever `ReorderRow` would have returned if called
   naively on the ungrouped list.

This is, structurally, exactly dagre's `sortSubgraph` /
`resolveConflicts` / `expandSubgraphs` trio named in research §7.4 — group,
sort-as-units, recursively expand. The difference is this plan's version is
written against this codebase's actual types instead of dagre's internal
graphlib representation.

### 3c. Where this plugs into the existing sweep structure

`CrossingReductionService.RefineRows`'s outer sweep loop (4 sweeps,
downward pass then upward pass then transpose, repeat) **stays as the outer
control flow** — you're not rewriting the sweep count or order, just what
happens inside `ReorderRow` for one layer-pair. Concretely:

```csharp
// New method, in a new file (do not modify CrossingReductionService.cs directly --
// it's used as-is by the OLD engine too during the transition period per 08's
// feature flag plan; changing its behavior would change the old engine's output,
// which must stay byte-for-byte identical until the new engine is validated and
// the old one is retired).
namespace MermaidDiagramExporter.Gui.Layout.Compound;

internal static class CompoundOrderingSort
{
    public static List<CompoundNode> ReorderLayerWithClusterContiguity(
        IReadOnlyList<CompoundNode> layer,
        IReadOnlyList<CompoundNode> adjacentLayer,
        IReadOnlyDictionary<string, HashSet<string>> adjacency, // same shape as CrossingReductionService's internal adjacency map
        CompoundGraph compound)
    {
        var rootGroup = PartitionIntoGroups(layer, compound, scopeClusterId: null);
        SortGroupRecursive(rootGroup, adjacentLayer, adjacency);
        return Flatten(rootGroup);
    }

    // ... PartitionIntoGroups, SortGroupRecursive (implements 3b steps 2-4), Flatten ...
}
```

Then `OrderAssignment.RunOrderingSweeps` calls
`CompoundOrderingSort.ReorderLayerWithClusterContiguity` in place of where
the old per-cluster code called `CrossingReductionService.RefineRows` — but
note `RefineRows`'s 4-sweep, both-directions, plus-transpose **outer
structure** still needs to be reproduced here (it's currently bundled
inside `RefineRows` itself, operating on a `List<List<LayoutNode>>` —
either (a) generalize `RefineRows` to accept a pluggable per-layer-pair sort
function and reuse it for both old and new engines, which is the cleaner
long-term design, or (b) duplicate the sweep-loop shape into
`OrderAssignment.RunOrderingSweeps` directly, which is faster to implement
and zero-risk to the old engine since nothing shared changes. **Recommend
(b) for the initial implementation** given this plan's emphasis on not
risking the old engine's behavior during the transition — revisit
extracting a shared sweep-loop utility as a cleanup step once the new
engine is validated and the old one is slated for removal, not before.

## Step 5 — Border node ordering (per research §8)

Within a layer, a cluster's `ClusterBorderTop`/`ClusterBorderBottom`
`CompoundNode`s for that rank (if this rank is within that cluster's rank
span — not every cluster has a border presence at every rank, only at ranks
its `ClusterBorderChain.MinRank..MaxRank` covers; see `06` Step 2 for how
that span gets determined implicitly by where containment edges pulled the
border nodes' ranks) must sort to the **two ends of that cluster's
contiguous block** — low border first, high border last, with all the
cluster's real/dummy member nodes at that rank sandwiched between them. This
is what gives the coordinate assignment phase (`08`) a literal left-edge and
right-edge anchor to compute the cluster's rectangle from at each rank,
matching research §8's description of why dagre's border segments exist.

Implement this as a **post-sort fixup** applied after
`ReorderLayerWithClusterContiguity` returns its flattened list, rather than
trying to bake it into the comparator: once a cluster's group is contiguous
(guaranteed by Step 3b-5's group-based sort), simply move that group's
`ClusterBorderTop` node (if present at this rank) to the start of the
group's slice and `ClusterBorderBottom` to the end. This is O(group size)
per cluster per layer and trivially correct given contiguity is already
guaranteed.

## Step 6 — Edge segment dummy lane assignment (resolves the `06` Step 3d forward-reference)

Recall `06`'s `InsertEdgeSegmentDummies` deferred the precise rule for
`ResolveSegmentOwningCluster` to this file. The rule:

- If `from.OwningClusterId == to.OwningClusterId` (including both being
  `null`, i.e. both top-level): the dummy's `OwningClusterId` is that same
  cluster (or `null`). The edge stays "inside" one cluster's lane the whole
  way.
- If they differ: walk both nodes' cluster ancestor chains (using
  `compound.ClusterParent`) and find the **lowest common ancestor cluster**
  (or `null` if they share no common ancestor, i.e. the edge genuinely
  crosses between two top-level regions). The dummy's `OwningClusterId` is
  that common ancestor (or `null`). This means an edge between two
  different namespaces gets its intermediate dummy nodes treated as
  "top-level" (or owned by whatever shared parent namespace contains both,
  for nested cases) for ordering purposes — i.e. it sorts in the gap
  between the two namespace blocks rather than being forced into either
  one, which is exactly the visual behavior you want (the edge visibly
  travels between the two boxes, not through the inside of either).

This rule must be applied **once, when the dummy is created in `06`** (it
doesn't change over time), so implement it as a small pure function
`Layout/Compound/ClusterAncestry.cs`:

```csharp
internal static class ClusterAncestry
{
    public static string? FindLowestCommonAncestor(string? clusterIdA, string? clusterIdB, CompoundGraph compound)
    {
        if (clusterIdA == clusterIdB) return clusterIdA; // handles both-null and both-same-cluster cases
        var ancestorsOfA = GetAncestorChainInclusive(clusterIdA, compound); // [clusterIdA, parent, grandparent, ..., null-terminated]
        var ancestorsOfB = GetAncestorChainInclusive(clusterIdB, compound);
        return ancestorsOfA.FirstOrDefault(a => ancestorsOfB.Contains(a)); // null is a valid "ancestor" (top-level), always shared, guarantees a result
    }
}
```

`06` should call this directly (it's a pure utility with no dependency on
ordering having happened yet — despite living conceptually "in the ordering
file" per how it's introduced here, it has no actual dependency on Steps
1–5 of this file, so don't block `06`'s implementation on this file being
fully done — just implement `ClusterAncestry.cs` early, as a small
standalone utility, possibly before either `06` or the rest of `07`).

## Validation checklist for this file

- [ ] For every rank and every cluster with members at that rank, the final
      per-layer order has all of that cluster's members (real + dummy +
      border) in one contiguous run — write a test that asserts
      `"no other cluster's node appears between the first and last index
      of cluster X's nodes in this layer's order, for every X and every
      layer."` This is the single most important invariant in the entire
      plan — it's the literal definition of "namespaces don't get their
      nodes interleaved with other namespaces," which is the user's core
      complaint about the current output.
- [ ] Border nodes sort to the exact two ends of their cluster's contiguous
      block (Step 5) in every layer they appear in.
- [ ] Nested clusters' contiguity holds at every level simultaneously (a
      child cluster's block is contiguous, AND it sits fully inside its
      parent's block, which is also contiguous relative to outside nodes).
- [ ] Total crossing count (count actual edge crossings in the final
      per-layer orders, using the same O(E log V) counting approach
      research §3 cites, or even a brute-force O(E²) counter for small test
      graphs since this is a test, not production code) is not worse than
      what the OLD per-cluster-isolated ordering produced on the same test
      graph, when measured ignoring cross-cluster edges (i.e. confirm the
      new engine didn't regress intra-cluster ordering quality while fixing
      inter-cluster contiguity) — and is meaningfully better when cross-cluster
      edges ARE counted (since those literally couldn't be optimized at all
      by the old per-cluster-isolated approach).

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\07-implementation-ordering-and-borders.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\08-implementation-coordinates-and-integration.md
# 08 — Implementation: Coordinate Assignment, Cluster Geometry, and Integration

Prerequisite reading: `01-research-findings.md` §9–§10,
`05-target-architecture.md` §5.3–§5.4, and the output contracts from `06`
(final `Rank` on every node) and `07` (final `OrderInRank` on every node,
cluster contiguity guaranteed within each rank).

Implements: `Layout/Compound/CoordinateAssignment.cs`,
`Layout/Compound/CompoundResultProjector.cs`, and the wiring changes in
`GraphLayoutCoordinator.cs` / `LayoutOptions.cs`.

## Part A — Coordinate assignment

Per research §9's recommendation: implement the **simplified
priority/median single-pass** approach, not full 4-direction Brandes-Köpf,
as the initial target. Full BK is listed as a stretch goal there — do not
build it now.

### A1. The "rank axis" coordinate (straightforward, do this first)

Given `options.Direction` (existing `LayoutDirection` enum,
`LeftToRight`/`TopToBottom`, already used throughout the codebase — no new
concept needed):

```csharp
public static void Run(CompoundGraph compound, LayoutOptions options)
{
    AssignRankAxisPosition(compound, options);     // A1
    AssignOrderAxisPosition(compound, options);     // A2 + A3
}

private static void AssignRankAxisPosition(CompoundGraph compound, LayoutOptions options)
{
    var maxSizeByRank = compound.Nodes
        .GroupBy(n => n.Rank)
        .ToDictionary(g => g.Key, g => g.Max(n => options.Direction == LayoutDirection.LeftToRight ? n.Width : n.Height));

    float cursor = options.OuterMarginX; // or OuterMarginY for TopToBottom — pick the margin matching the rank axis
    foreach (var rank in maxSizeByRank.Keys.OrderBy(r => r))
    {
        foreach (var node in compound.Nodes.Where(n => n.Rank == rank))
        {
            if (options.Direction == LayoutDirection.LeftToRight) node.X = cursor;
            else node.Y = cursor;
        }
        cursor += maxSizeByRank[rank] + options.RankSpacing;
    }
}
```

This is a direct, low-risk translation of "rank index × (max size at that
rank + rank spacing), cumulative" from research §9 — no algorithmic
judgment calls here, just bookkeeping.

### A2. The "order axis" coordinate — priority-based single pass

This is the one piece of real algorithmic work in this phase. The goal:
within each rank, position nodes along the order axis (perpendicular to the
rank axis) so that:
1. Nodes respect their already-fixed `OrderInRank` (never reorder here —
   that was `07`'s job and is final).
2. Adjacent nodes in the same rank don't overlap (minimum gap =
   `options.NodeSpacing`, reusing the existing tunable).
3. Each node is pulled toward the **median position of its neighbors in
   adjacent ranks** (this is the part that makes straight-ish edges and
   visually "pulls" a cluster's nodes toward their connected neighbors —
   the actual visual payoff of this whole rewrite).

```csharp
private static void AssignOrderAxisPosition(CompoundGraph compound, LayoutOptions options)
{
    var byRank = compound.Nodes.GroupBy(n => n.Rank).OrderBy(g => g.Key)
        .ToDictionary(g => g.Key, g => g.OrderBy(n => n.OrderInRank).ToList());

    // Initial pass: pack each rank's nodes left-to-right (top-to-bottom) along the order
    // axis using only spacing (no neighbor-pulling yet) -- this guarantees a valid,
    // non-overlapping starting layout before any "pull toward neighbors" adjustment runs.
    foreach (var rank in byRank.Keys)
        PackRankWithoutOverlap(byRank[rank], options);

    // Iterative median-pull passes (a handful of fixed iterations, e.g. 4-8, alternating
    // direction like the sweep structure in 07, is sufficient -- this does not need to run
    // to full convergence, "good enough" visually is the bar here per research §9's
    // recommendation to skip full BK).
    int passes = options.CoordinateAssignmentPasses; // new LayoutOptions field, default e.g. 6
    for (int pass = 0; pass < passes; pass++)
    {
        bool downward = pass % 2 == 0;
        var ranksInPassOrder = downward ? byRank.Keys.OrderBy(r => r) : byRank.Keys.OrderByDescending(r => r);
        foreach (var rank in ranksInPassOrder)
            PullTowardNeighborMedianAndResolveOverlaps(byRank[rank], compound, options, downward);
    }
}
```

`PackRankWithoutOverlap`: trivial — walk the already-order-sorted list,
`cursor = 0`, place each node at `cursor`, advance `cursor += node size on
order axis + options.NodeSpacing`.

`PullTowardNeighborMedianAndResolveOverlaps`: for each node in the rank (in
order-axis order), compute its neighbors' positions in the **adjacent rank
in the direction just processed** (the rank "behind" us in this pass's
direction, which already has updated positions this pass), take the median
of those positions as this node's "desired" position, then:
1. Try to move the node toward its desired position.
2. **Priority rule** (this is the "priority" in "priority-based" — borrowed
   from the same family of simplified BK-alternative algorithms referenced
   in research §9): a node's priority = its neighbor count (more connected
   = higher priority = more entitled to pull its neighbors out of the way
   rather than being blocked by them). When two adjacent nodes' desired
   moves would cause an overlap, the **lower-priority node yields** (gets
   pushed to maintain `options.NodeSpacing`, possibly away from its own
   desired position) and the **higher-priority node gets to move freely
   toward its desired position.** Process the rank in priority order
   (highest first) for this reason, not in left-to-right order, when
   *resolving* overlaps (the `PackRankWithoutOverlap` pre-pass position is
   what gives you a left-to-right order-axis order to begin with — sort by
   priority only for the resolve step).
3. Border nodes (`ClusterBorderTop`/`Bottom`) have **zero size and zero
   pull priority of their own** — they don't have "neighbors" in the
   median-pull sense (their only edges are containment edges from `06`, not
   real graph edges) — instead, after every real/dummy node in their
   cluster's slice has settled at this rank, **reposition the border nodes
   to sit exactly at the slice's two ends** (same fixup-after-the-fact
   approach `07` Step 5 used for ordering, repeated here for position).

## Part B — Cluster geometry projection (`CompoundResultProjector`)

```csharp
public static LayoutResult Project(CompoundGraph compound, LayoutGraph originalGraph, LayoutOptions options)
{
    var nodeBounds = new Dictionary<string, Rect>();
    foreach (var node in compound.Nodes.Where(n => n.Kind == CompoundNodeKind.Real))
        nodeBounds[node.SourceLayoutNodeId!] = new Rect(node.X, node.Y, node.Width, node.Height);

    var clusterBounds = new Dictionary<string, Rect>();
    foreach (var cluster in originalGraph.Clusters)
        clusterBounds[cluster.Id] = ComputeClusterBoundingBox(cluster, compound, options); // per 05 §5.4

    float contentWidth = nodeBounds.Values.DefaultIfEmpty(default).Max(r => r.xMax) + options.OuterMarginX;
    float contentHeight = nodeBounds.Values.DefaultIfEmpty(default).Max(r => r.yMax) + options.OuterMarginY;

    return new LayoutResult
    {
        NodeBounds = nodeBounds,
        ClusterBounds = clusterBounds,
        ContentSize = new Vector2(
            Mathf.Max(options.MinimumContentWidth, contentWidth),
            Mathf.Max(options.MinimumContentHeight, contentHeight)),
        // NodeClusterIds / ClusterVisuals / EdgePaths are filled in by
        // GraphLayoutCoordinator AFTER this call returns, exactly as it already
        // does today for the old engine's LayoutResult (see GraphLayoutCoordinator.CreateLayout,
        // the three lines after `_layeredLayoutEngine.Run(...)` that set those three
        // properties) -- do not duplicate that logic inside Project; keep it where it
        // already lives, since it's engine-agnostic and operates on `preparedGraph` +
        // whatever LayoutResult came back, regardless of which engine produced it.
    };
}
```

`ComputeClusterBoundingBox`: implements `05` §5.4 exactly — union of every
contained real node's rect (transitively through nested clusters) plus that
cluster's own border node positions, padded by the existing
`GroupLeftPadding`/`GroupTopPadding`/`GroupBottomPadding` options (reuse,
don't add new padding options).

**Edge paths:** the original `CompoundEdge`s that got split into dummy
chains (`06` Step 3d) need their final `LayoutEdgePath.Points` built from
the dummy chain's final positions — walk the `EdgeDummyChains` side-table
(added in `06`'s implementation per that file's note) and emit one point
per dummy in the chain, in order, as the polyline. This replaces whatever
the existing `EdgeRoutingService.BuildPaths` does for the same edges **only
in the sense that it now has better input** (real intermediate points
instead of having to interpolate/guess between two cluster boxes) — confirm
during implementation whether `EdgeRoutingService` should consume these
dummy-chain points directly, or whether it should keep doing its own
clipping/routing logic using the dummy points as additional waypoints rather
than a complete replacement. **This decision is deliberately left open
here** — make it during `09` validation by comparing both approaches
visually on a real test graph, since "is the existing routing service's
curve-fitting better with or without these extra waypoints" is an empirical
question this plan's author cannot answer without seeing rendered output.

## Part C — Wiring into `GraphLayoutCoordinator` behind a feature flag

### C1. New `LayoutOptions` field

```csharp
// In LayoutOptions.cs, additive only:
public bool UseCompoundLayoutEngine { get; set; } = false; // default OFF until validated per 09
public float ClusterContainmentEdgeWeight { get; set; } = 24f; // per 06 Step 2c
public int CoordinateAssignmentPasses { get; set; } = 6; // per 08 Part A2
```

### C2. `GraphLayoutCoordinator.CreateLayout` change

```csharp
private readonly IGraphLayoutEngine _layeredLayoutEngine = new LayeredLayoutEngine();
private readonly IGraphLayoutEngine _compoundLayeredLayoutEngine = new CompoundLayeredLayoutEngine(); // NEW
private readonly IGraphLayoutEngine _simpleColumnLayoutEngine = new SimpleColumnLayoutEngine();
// ...

public LayoutResult CreateLayout(Core.TypeGraph graph, LayoutOptions? options = null)
{
    // ...unchanged prep pipeline...

    LayoutResult layoutResult = preparedGraph.Nodes.Count == 0
        ? _simpleColumnLayoutEngine.Run(preparedGraph, resolvedOptions)
        : resolvedOptions.UseCompoundLayoutEngine
            ? _compoundLayeredLayoutEngine.Run(preparedGraph, resolvedOptions)
            : _layeredLayoutEngine.Run(preparedGraph, resolvedOptions);

    // ...unchanged post-layout pipeline, NodeClusterIds, ClusterVisuals, EdgePaths assignment...
}
```

This is the **entire integration risk surface** — three lines changed in
one file, gated behind a flag defaulted to `false`. The old engine's code
path is byte-for-byte unchanged and remains the default until explicitly
flipped, per `09`'s rollout plan.

### C3. Exposing the flag (optional, for manual A/B comparison during validation)

Add a toggle in `Settings/SettingsWindow.axaml.cs` /
`Settings/ProjectSettings.cs` (a simple bool checkbox, "Use experimental
compound layout engine") so a human reviewer can flip it live and compare
against the same loaded graph without rebuilding — this is purely a
developer/validation convenience, not a user-facing feature commitment; it
can be removed once the new engine becomes the unconditional default and
`LayeredLayoutEngine` is eventually retired (a future cleanup step, **not**
part of this plan's scope — this plan stops at "new engine validated and
available behind a flag," it does not mandate removing the old one).

## Part D — Rollout sequence (do these in order, do not skip ahead)

1. Implement `06`, `07`, `08` Parts A–B with `UseCompoundLayoutEngine`
   defaulted `false`. Old engine, old behavior, zero risk to current users.
2. Run the full `09` validation suite with the flag forced `true` in tests
   only (production default stays `false`).
3. Manually compare output on at least 2–3 real-world-sized project scans
   (the user's own Unity project referenced in the uploaded context is a
   good candidate — it's large enough to be a meaningful stress test) with
   the flag on vs off, side by side.
4. Only after both automated validation (`09`) and manual visual comparison
   pass, flip the default to `true` in a follow-up change — and only then
   consider this plan's scope complete.
5. Do not delete `LayeredLayoutEngine.cs`, `ComponentSplitter.SplitClusters`,
   or any other "old path" code as part of this plan. Retiring the old
   engine is future cleanup, explicitly out of scope here (per
   `00-INDEX.md` ground rule #2).

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\08-implementation-coordinates-and-integration.md


//\Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\09-validation-and-test-plan.md
# 09 — Validation and Test Plan

This file exists because, per `00-INDEX.md`'s framing, "looks right" was
apparently sufficient validation for the *current* `LayeredLayoutEngine` to
ship despite being structurally wrong (per `04`'s gap analysis) — it
produced *a* plausible-looking diagram, just not one that matches the
target. This plan does not want a repeat: every phase below has a concrete,
automatable check, and the final sign-off requires both automated checks
**and** visual comparison on a real project, not either alone.

## 1. Unit-level invariants (one test class per phase, can run independently of each other)

### 1.1 `CompoundGraphBuilder` (06, Step 1–2)

- Given a `LayoutGraph` with 2 clusters, no nesting, 3 nodes each, 2
  cross-cluster edges: assert the resulting `CompoundGraph` has exactly
  6 `Real` nodes, exactly 4 `ClusterBorderTop`/`Bottom` nodes (2 per
  cluster), and the containment edges from `06` Step 2c present for every
  member (2 × 3 × 2 = 12 containment edges, plus the 2 original
  cross-cluster edges carried over as `CompoundEdge`s = 14 total non-border
  edges... actually recompute this exact number when writing the test
  rather than trusting this plan's arithmetic blindly — the point of the
  test is to assert a number you derived from the fixture by hand, not to
  copy a number from this document).
- Given 2 levels of cluster nesting (parent contains child contains 3
  nodes; parent also directly contains 2 other nodes): assert
  `ClusterParent`/`ClusterChildren` round-trip correctly, and assert the
  parent-nesting containment edges from `06` Step 2d exist between the
  correct border node pairs.
- Given a cluster whose nodes are split across two node-level connected
  components (per `06` Step 4 / `05` §5.7): assert two separate
  `LayoutCluster`-equivalent groupings are produced with the same `Label`
  but disambiguated ids, and that no node ends up in neither/both.

### 1.2 `RankAssignment` (06, Step 3)

- **No-cluster sanity check:** a plain chain `A -> B -> C` with no clusters
  at all produces ranks `0, 1, 2`. (This is the simplest possible case and
  should pass trivially — if it doesn't, something fundamental is broken
  before any cluster logic is even exercised.)
- **Cluster contiguity (the headline invariant):** construct a graph where
  a 4-node cluster's members have direct edges only to each other plus one
  edge from one member to an external node — assert all 4 members end up
  with ranks forming a contiguous-enough range that no external node's rank
  falls strictly inside the cluster's `[min, max]` member rank range. (Note:
  per `06`'s validation checklist, full contiguity is a joint Rank+Order
  property — this unit test checks the Rank half only; the Order half is
  tested in 1.3 below; the *combined* invariant is tested at the
  integration level in §2.)
- **Nested containment ordering:** assert
  `parentTop.Rank <= childTop.Rank <= childBottom.Rank <= parentBottom.Rank`
  holds for a 2-level nesting fixture, including a case where the child
  cluster's content is NOT at the very start/end of the parent's rank range
  (i.e. the parent has its own direct members both before and after the
  child cluster's rank span) — this is the case most likely to break if the
  containment-edge direction (`IsContainment` branch in `06` Step 3c) is
  implemented backwards.
- **Edge segment dummy insertion postcondition:** after
  `InsertEdgeSegmentDummies`, assert programmatically that **every**
  remaining `CompoundEdge` has `Math.Abs(to.Rank - from.Rank) == 1` (or
  `0` for the special-cased containment edges, if those are still present
  post-insertion — confirm whether containment edges should also be
  dummy-chained if they ever span >1 rank, which they can, e.g. a cluster
  spanning 5 ranks has containment edges from the top border directly to
  members at ranks 0 through 4, i.e. spans of varying length. **This is a
  gap in `06`'s description worth flagging explicitly:** containment edges
  as described in `06` Step 2c are NOT necessarily single-rank-span, and
  `06`'s `InsertEdgeSegmentDummies` pseudocode explicitly excludes
  `IsContainment` edges via `.Where(e => !e.IsContainment && ...)`. Decide
  during implementation whether containment edges need their own dummy
  chains too (probably not strictly necessary for visual correctness since
  they're invisible bookkeeping edges, never rendered — but confirm they
  don't confuse the *ordering* phase in `07`, which does iterate
  `compound.Edges` for adjacency in some contexts — re-read `07` Step 1's
  adjacency-building carefully when implementing and make sure containment
  edges are explicitly excluded from the ordering adjacency map too, the
  same way `CrossingReductionService.IsOrderingEdge` already excludes
  non-`Direct`-role edges in the old engine).

### 1.3 `OrderAssignment` / `CompoundOrderingSort` (07)

- **Contiguity invariant, direct test:** the exact test described in `07`'s
  own validation checklist — for every rank and every cluster with members
  at that rank, assert no foreign node's order-index falls between the
  min and max order-index of that cluster's members at that rank. Write
  this as a single reusable assertion helper
  (`AssertClusterContiguity(CompoundGraph compound)`) since it's referenced
  by multiple test cases and by the integration-level check in §2.
- **Nested contiguity:** same assertion, applied at both nesting levels
  simultaneously, on a 2-level nesting fixture.
- **Border node placement:** assert `ClusterBorderTop`'s `OrderInRank` is
  strictly less than every member's, and `ClusterBorderBottom`'s strictly
  greater, for every rank the cluster appears in.
- **Crossing count regression check:** build one moderately-complex fixture
  (suggest: 3 clusters, 4–6 nodes each, a mix of intra-cluster and
  inter-cluster edges, at least one nested cluster) and assert the new
  ordering's total crossing count (write a simple brute-force crossing
  counter for test purposes — O(E²) is fine for a fixture this size) is
  **less than or equal to** what you measure from manually constructing the
  equivalent old-engine per-cluster-isolated ordering on the same fixture.
  This is the test that most directly proves the rewrite achieved its
  stated goal, so don't skip it even though it's more effort to set up than
  the structural assertions above.

### 1.4 `CoordinateAssignment` / `CompoundResultProjector` (08)

- **No-overlap postcondition:** for every rank, assert no two nodes'
  final order-axis rects overlap (allowing exactly `options.NodeSpacing` as
  the minimum gap, not less).
- **Order preservation:** assert the final order-axis positions are
  monotonically non-decreasing in `OrderInRank` order, for every rank (i.e.
  coordinate assignment never silently reordered what `07` decided).
- **Cluster bounding box correctness:** for a known fixture, assert
  `ClusterBounds[clusterId]` is at least as large as the tightest
  axis-aligned box containing every member node's rect, plus at least the
  configured padding on each side (don't assert exact equality unless
  you've also fixed every padding constant in the test — asserting
  "at least as large as the unpadded tight box, by at least the minimum
  expected padding" is more robust to minor constant tuning later).
- **`LayoutResult` shape parity with the old engine:** run both engines on
  the *same* `LayoutGraph` fixture and assert the **keys** of `NodeBounds`
  and `ClusterBounds` are identical sets between old and new results (the
  *values*/positions will differ — that's the whole point — but every node
  and cluster that existed in the input must be present in both outputs;
  this catches accidental drops, e.g. a node that only has non-`Direct`
  edges getting silently excluded from the new engine because of the `06`
  Step 1c filtering, if that filtering has a bug).

## 2. Integration-level checks (run the full new engine end-to-end)

1. **Round-trip through `GraphLayoutCoordinator.CreateLayout`** with
   `UseCompoundLayoutEngine = true`, on:
   - A trivial fixture (3 nodes, 1 cluster).
   - A fixture with 2 sibling clusters and cross-cluster edges.
   - A fixture with 2 levels of cluster nesting.
   - A fixture with a genuinely disconnected component (tests `06` Step 4 /
     `05` §5.7's partial-cluster-splitting path end-to-end, not just in
     isolation).
   - A fixture with at least one long edge (spanning 3+ ranks) to exercise
     dummy-chain insertion, ordering, and final edge-path projection
     together.
2. For each fixture above, run the **same combined contiguity assertion**
   described in `07`'s checklist (the joint Rank+Order invariant that unit
   tests in §1.2/§1.3 could only check half of independently) — i.e. after
   the full pipeline runs, re-verify contiguity holds on the *final* state,
   not just at the intermediate checkpoints. This catches bugs where one
   phase's correct output gets silently corrupted by a later phase (e.g. if
   `08`'s coordinate assignment accidentally reordered something it should
   have only repositioned).
3. **Existing test suite must still pass unchanged**, specifically:
   - `tests/MermaidDiagramExporter.Tests/MermaidExporterTests.cs`
   - `tests/MermaidDiagramExporter.Tests/TypeGraphTests.cs`
   - `tests/MermaidDiagramExporter.Tests/CliAndEndToEndTests.cs`
   - `tests/MermaidDiagramExporter.Tests/RenderingBenchmarkTests.cs`
     (pay particular attention here — if this test asserts on absolute
     timing/performance, confirm the new engine doesn't regress it
     meaningfully; the new engine does more total work per layout pass —
     unified ranking is global instead of per-cluster — so some slowdown on
     very large graphs is plausible and should be measured, not assumed
     away)

   Run these with the flag **false** (default) first to confirm zero
   regression to existing behavior, then re-run with the flag forced
   **true** where applicable to confirm the new path doesn't crash/error
   even if exact output assertions in these particular tests are
   old-engine-specific and therefore skipped/adapted for the new-engine run.

## 3. Visual / manual validation (cannot be fully automated — budget real time for this)

1. Use the user's own real project (the Unity codebase referenced in
   `csharp_project_context.txt`, or any sufficiently large/complex C#
   solution) as the primary stress test — synthetic fixtures in §1–2 prove
   correctness of individual invariants, but only a real, messy, large
   graph proves the *visual* outcome actually looks like the target.
2. Generate three renders for direct comparison:
   - Old engine (`UseCompoundLayoutEngine = false`) — the current,
     complained-about output (matches the conversation's "Image 1").
   - New engine (`UseCompoundLayoutEngine = true`).
   - The same project's data exported to `.mmd` and rendered through actual
     Mermaid (the user's plugin already has a "Save .mmd" button and an
     "Open Live Editor" button per the screenshots — use them) as the
     ground-truth reference (matches the conversation's "Image 2").
3. Specific things to visually check, in order of how directly they map to
   the user's original complaint:
   - Are namespace boxes' member nodes visually grouped with no
     interleaving from other namespaces at the same height/column? (Direct
     visual check of the §1.3/§2.2 contiguity invariant — if the automated
     check passes but this still looks wrong, the bug is downstream, in
     rendering or in how `EdgeRoutingService`/cluster geometry consumes the
     new engine's output, not in the core algorithm.)
   - Do heavily-cross-referenced namespaces end up visually near each
     other, the way they do in the Mermaid reference render? (Direct visual
     check of the §1.2 cluster-contiguity-via-unified-ranking goal — this
     is the thing cluster-as-supernode ranking structurally couldn't do at
     all, per `04`.)
   - Are there fewer long, far-traveling edge lines crossing large empty
     areas of the canvas compared to the old engine's output on the same
     project? (A rough, eyeball proxy for "did unified ranking actually
     pull connected things together" — also checkable more rigorously via
     the §1.3 crossing-count regression test, but a human glance at the
     full diagram catches things a synthetic-fixture crossing count won't,
     like "technically fewer crossings but now there's one extremely long
     edge that's visually worse than three short crossing ones.")
4. Get a second pair of eyes (another agent or the user) on the
   side-by-side comparison before flipping the default flag — per `08` Part
   D, this is a required gate, not optional polish.

## 4. Sign-off checklist before flipping `UseCompoundLayoutEngine` default to `true`

- [ ] All unit tests in §1 pass.
- [ ] All integration tests in §2 pass, including the existing suite
      unchanged with the flag off.
- [ ] Visual comparison in §3 completed on at least one real, large project,
      reviewed by a second pair of eyes, and judged to be a clear visual
      improvement over the old engine and a reasonable approximation of the
      Mermaid reference render (not necessarily pixel-identical to
      Mermaid's output — that's not the goal; structurally similar
      clustering/edge-routing behavior is).
- [ ] Bug fixes `02` and `03` are independently verified (they don't depend
      on this work, but confirm they haven't regressed if any shared file —
      e.g. `MainWindow.axaml.cs` — was touched by both efforts).
- [ ] Performance on the largest available real test project is measured
      and judged acceptable (no specific numeric target is set here
      deliberately — this plan's author has no baseline timing data for
      this codebase; whoever implements this should record a before/after
      wall-clock time for a full layout pass on the largest available real
      project and use judgment, flagging to the user if it's dramatically
      slower, e.g. >3-5x, since that would warrant discussing whether the
      network-simplex-approximation relaxation bound in `06` needs
      tightening for large graphs before shipping).

//end of \Games\UnityGames\importedUnityPackagesToAIWith\MermaidDiagramExporter\docs\09-validation-and-test-plan.md


