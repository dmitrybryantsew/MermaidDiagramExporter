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
