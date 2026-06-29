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
