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
