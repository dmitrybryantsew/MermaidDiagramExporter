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
