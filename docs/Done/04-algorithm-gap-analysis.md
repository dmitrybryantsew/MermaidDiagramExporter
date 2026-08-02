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
