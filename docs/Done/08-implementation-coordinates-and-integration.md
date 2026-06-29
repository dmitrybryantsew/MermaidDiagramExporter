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
