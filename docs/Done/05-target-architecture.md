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
