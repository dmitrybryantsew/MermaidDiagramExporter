# Layout Alternatives for Large Codebases — Research Findings

Date: 2026-07-18
Status: Research / decision support.
**Implementation status (2026-07-18, updated):** Phase 1 landed — §3.1 edge
weights wired (`Edge.Weight` from `LayoutEdgeWeights`), §3.3 MDS available as
`LayoutEngineKind.MsaglMds`. **§3.2 (MSAGL FIL) was tried and rejected**: its
public path (`InitialLayout`) is a deliberately cheap seed — PivotMDS + only
5–10 force iterations, real `FastIncrementalLayout` ctor is `internal` — and it
degenerated to a 6539×36518 vertical strip (aspect 0.18) on the real 498-class
graph. Instead an own force engine ships as `LayoutEngineKind.Force`
(`ForceDirectedLayoutEngine`: FR-style, rectangle-aware forces, weighted
springs, cluster gravity, deterministic seed) — measured 14747×14309, aspect
1.03, 0 overlaps, tight clusters on the same graph; ~2s. MSAGL MDS needed
own cluster bounds too (`ClusterBoundsComputer`) because MSAGL's MDS ignores
clusters entirely. **Guaranteed non-overlapping namespaces** ship as
`ForceLayoutSettings.PreventClusterOverlap` (default on, Settings → Engine →
Force checkbox): `ForceClusterSeparation` rigidly translates overlapping
sibling clusters apart (2D minimal push + monotone rightward fallback sweep,
bottom-up with parent re-enclosure) — measured **0 overlapping cluster pairs**
on the real 498-class graph (was 115 without), aspect 0.87 preserved, ~2s.
Bundling (§3.4) is NOT directly applicable: `EdgeRoutingService`
computes its own paths from bounds, so MSAGL's routing output is discarded —
bundling needs its own work item there. **Zone-first hybrid (§4.1) shipped**
(`LayoutEngineKind.ZoneFirst = 5`, Settings → Engine): macro step collapses
each top-level namespace into a zone supernode with coupling-weighted springs
(`ZoneMacroLayout` — a dedicated big-box force model: grid seed, penetration-
growing separation, short-range repulsion, 500 iterations), micro step lays
out each zone independently (MSAGL Sugiyama by default, Force optional) with
per-zone cluster separation; zone boxes guaranteed non-overlapping. Measured
on the real graph: **0 overlapping zones, coupled zones ~2× closer than
average (gap ratio 0.45), aspect ~0.9, ~350ms** (6–10× faster than the plain
Force engine), deterministic. **Aggregate highway edges (§6.4) shipped** as a
canvas toggle (Settings → Diagram → "Aggregate inter-namespace edges into
highways", off by default): renderer-level grouping (`HighwayGrouper`) — one
thick line per namespace pair (width ∝ log count) with per-direction
arrowheads and a count badge, anchored on the drawn zone borders. Per-edge
data stays intact (kind filters, focus mode, minimap unaffected) and
selecting a node re-expands its individual edges (expand-on-select). Next per
this doc: zone collapse/expand (§6.3) and semantic zoom (§6.2).
Context: follow-up to `2026-07-02-Clustering_connected_classes_in_MSAGL_layout.md`
Scale target: ~500 classes / ~670 relations (sparse, avg degree ≈ 2.7, one big connected component)

## TL;DR

Your three screenshots all collapse toward the center because **Sugiyama is the wrong objective function for what you want**. It optimizes crossing count and flow direction, then centers every layer — proximity information is destroyed. Three concrete paths, in order of payoff per effort:

1. **Stay in MSAGL, switch algorithm**: `FastIncrementalLayout` (force-directed + clusters + constraints) and `MdsLayoutSettings` (stress/MDS) ship inside the MSAGL 1.2.1 package you already reference. Force-directed is *by definition* "connected = close, unrelated = far". Both support clusters.
2. **Zone-first hybrid (macro/micro layout)**: lay out the *namespace graph* (≈20–40 zones) separately, place zone boxes, then lay out each zone internally with Sugiyama. This is the only approach that **guarantees** clean zone separation, and it directly enables collapse/expand and stable add/remove.
3. **Clean the inter-zone hairball**: MSAGL ships metro-map **edge bundling** (`SplineBundling` + `BundlingSettings`) and a level-of-detail zoom engine (`LgLayoutSettings`, the GraphMaps engine). Aggregating zone-to-zone edges into "highways" does more for readability than any node placement change.

Everything in option 1 and 3 was **verified by reflection against the MSAGL 1.2.1 assembly in your NuGet cache** — these are real types you can use today, not external dependencies.

---

## 1. Why everything blobs at the center (diagnosis)

All three current modes (None / AppVsTests / FirstLevelNamespace) run the same Sugiyama layout; partitioning only changes top-level packing. The center-blob has three structural causes:

1. **Objective mismatch.** Sugiyama minimizes edge crossings and keeps edges short *between adjacent ranks*. It never optimizes "connected nodes near each other" in 2D Euclidean space. Rank assignment flattens each node to a single integer (its layer); everything with the same rank gets centered on one line → radial star around the barycenter.
2. **No edge weights reach MSAGL.** `LayoutEdgeWeights` (Inheritance=3, Implements=2.5) exists but `MsaglLayoutEngine` creates `new Edge(src, tgt)` without setting `Edge.Weight` (the property exists: `Int32 Edge.Weight`, verified). With uniform weights, a critical inheritance edge and a throwaway association are equally strong, so the ranker has no reason to keep tightly-coupled classes together.
3. **Partitioning ≠ separation.** `PackingMethod.Columns` packs the top-level synthetic clusters into columns, but cross-namespace edges (667 of them) pull the intra-cluster layout back into one interleaved stack, and MSAGL draws each cluster around wherever its nodes ended up. Guaranteed separation requires separating the layout *process*, not just the packing.

---

## 2. Restating your goals as layout requirements

| Goal (your words) | Layout property needed |
|---|---|
| "better separation for logical zones" | Hard cluster boundaries; inter-cluster gaps; zone placement driven by zone-to-zone coupling |
| "connected zones stay closer, unrelated farther" | Distance ∝ graph-theoretic/coupling distance → force-directed or stress/MDS objective |
| "easily manipulate, add or remove classes" | Incremental layout with **mental-map preservation**; pin/lock support; stable positions across re-layout |
| "see how systems are connected" | Zone-level abstraction: aggregate edges, bundles, collapse/expand, level-of-detail zoom |
| "how data flows" | Separate call-graph view (see §8) — not solvable by class-layout alone |

No single algorithm gives all five. The realistic answer is a **hybrid** (§4) plus **interaction patterns** (§6).

---

## 3. Options inside MSAGL 1.2.1 (verified, zero new dependencies)

Reflection dump of `Microsoft.Msagl.dll` (netstandard2.0, v1.2.1) confirms these public types:

### 3.1 Quick win: pass edge weights to Sugiyama
- **What**: set `Edge.Weight` from the existing `LayoutEdgeWeights.GetWeight(kind)` when building the `GeometryGraph` in `MsaglLayoutEngine`. ~3 lines in one file.
- **Effect**: the network-simplex ranker minimizes *weighted* edge span → inheritance/implement pairs land in adjacent ranks and next to each other more often.
- **Limit**: helps locality, does nothing for zone separation or the center-blob.

### 3.2 `FastIncrementalLayoutSettings` (force-directed + clusters + constraints)
This is the most interesting finding. MSAGL's FIL is a stress-based force layout with:

- **Cluster support**: `ClusterGravity`, `AttractiveInterClusterForceConstant`, `UpdateClusterBoundariesFromChildren`, `ClusterMargin`, `LiftCrossEdges` — clusters pull their members together and repel other clusters.
- **Per-edge ideal length**: `IdealEdgeLength` (`EdgeConstraints`) — strongly-coupled pairs can request short edges.
- **Constraint API** (`Microsoft.Msagl.Layout.Incremental.IConstraint` implementations, all public): `MinSeparationConstraint`, `MaxSeparationConstraint`, `StickConstraint`, `LockPosition`, `VerticalSeparationConstraint`, `HorizontalSeparationConstraint`, `ProcrustesCircleConstraint`.
  - `LockPosition` = **pinning**: after a drag, pin the node; re-run layout without disturbing it.
  - `ProcrustesCircleConstraint` / starting from existing positions = **mental-map preservation** across add/remove.
- Tunables: `RepulsiveForceConstant`, `AttractiveForceConstant`, `GravityConstant`, `MaxIterations`, `ApproximateRepulsion` (Barnes-Hut-style speed), `AvoidOverlaps`.
- **This is literally the "connected close / unrelated far" objective**, and its constraint system is the natural backend for your manual-drag workflow. Candidate for a new `LayoutEngineKind.MsaglForce` + `MsaglEngineOptions` entries.
- Caveats: force layouts are non-deterministic-ish (seed needed for stability), can look "organic/messy" for inheritance-heavy graphs, and need tuning (repulsion vs. gravity) at 500 nodes. Sensible approach: seed it from the Sugiyama result instead of random.

### 3.3 `MdsLayoutSettings` (PivotMDS + stress majorization)
- MDS places nodes so 2D distances approximate **graph-theoretic distances** (`PivotDistances`, `PivotMDS`, `AllPairsDistances` types present). Zones emerge from topology: this is the "proximity clustering" Claude suggested trying in the archived doc.
- Options: `PivotNumber` (accuracy/speed), `RemoveOverlaps`, `IterationsWithMajorization` (stress refinement), `ScaleX/ScaleY`, `RotationAngle`, `ClusterMargin`.
- Good at global structure ("which subsystem lives where"), weaker at local readability; overlap removal (`StressMajorization` in `ProximityOverlapRemoval`) may distort clusters. Worth a one-day experiment as `LayoutEngineKind` option #2.

### 3.4 `EdgeRoutingMode.SplineBundling` + `BundlingSettings` (metro-map bundling)
- Verified types: `BundlingSettings`, `BundleRouter`, `GeneralMetroMapOrdering`, `SimulatedAnnealing`.
- Bundles groups of edges that travel between the same zones into shared "metro lines". At 667 edges this can remove 50–80% of visible ink between zones. Can be applied **on top of any engine** as a routing mode (your `EdgeRoutingService` already isolates routing — bundling fits there).
- Related research: Holten 2006 (hierarchical edge bundling), Holten & van Wijk 2009 (force-directed edge bundling) — MSAGL implements the metro-map variant (Pupyrev et al.).

### 3.5 `LgLayoutSettings` / GraphMaps (level-of-detail zoom)
- The `Microsoft.Msagl.Layout.LargeGraphLayout` namespace (`LgInteractor`, `Rail`, `RailGraph`, `LgData`, tile generation) is the engine behind MSAGL's **GraphMaps**: the graph renders like an online map — zoomed out you see important nodes + highways; zooming in reveals detail. Paper: Nachmanson et al., *GraphMaps: Browsing Large Graphs as Interactive Maps* (arXiv:1506.06745).
- Bigger integration (it wants its own interactor/tiles), but it is *the* canonical answer to "500 classes on one canvas". Mentioned for completeness; see §6.2 for a cheaper hand-rolled alternative.

### 3.6 Smaller helpers worth knowing
- `RankingLayout` (`Prototype.Ranking`) — MDS-flavored prototype; low priority.
- `GraphConnectedComponents` — split into components, layout each, pack (you already have `ComponentSplitter`; MSAGL can do it natively).
- `Microsoft.Msagl.Layout.Initial.Relayout` / `InitialLayoutByCluster` — MSAGL's own wrappers for *modifying an existing layout when elements are added/removed* — directly relevant to your add/remove-classes goal; investigate before writing your own incremental wrapper.
- `ProximityOverlapRemoval` namespace (`StressMajorization`, MST overlap removal) — usable as a standalone post-pass for MDS/force output.

---

## 4. Architecture-level options (engine-agnostic)

### 4.1 Zone-first hybrid ("macro/micro" two-level layout) — **recommended core architecture**
Layout pipeline:

1. **Macro**: collapse each namespace to a supernode; edge weight = number/strength of cross-namespace references. Lay out this ~20–40-node zone graph with force/stress (FIL or a tiny hand-rolled Fruchterman–Reingold — at this size even O(n²) is instant). Coupled zones land adjacent; unrelated zones repel to the periphery.
2. **Place**: reserve a rectangle per zone at its macro position (simple grid/rectangle-packing; ELK's `rectpacking`/`topdownpacking` are the reference designs).
3. **Micro**: run Sugiyama (your current strength — hierarchy reads well) **inside each zone box**, independently. Zero cross-zone edge interference → guaranteed clean columns/regions.
4. **Route**: draw inter-zone edges last (your decoupled `EdgeRoutingService` already works this way), optionally bundled (§3.4) or aggregated into zone-to-zone "highway" edges with thickness = coupling count.

This is Option 3 from the archived doc, generalized: zones don't have to be columns — their placement reflects *actual coupling*, and unconnected zones genuinely end up far apart because the macro step is force-directed. It also composes with everything else below.

- Cost: mostly orchestration around what you have (a macro graph builder + a box placer + calling the existing engine per zone). No new dependencies.
- Bonus: per-zone Sugiyama is parallelizable and makes add/remove cheap (only re-layout the touched zone).

### 4.2 Community detection: zones from coupling, not from names
Namespaces are *declared* structure; what you actually want to see is *behavioral* structure. Modularity clustering derives zones from the edge topology:

- **Louvain** (Blondel et al. 2008): fast greedy modularity optimization; ~150 lines to implement, handles 500 nodes in milliseconds.
- **Leiden** (Traag, Waltman, van Eck 2019): fixes Louvain's disconnected-community defect; equally implementable.
- Label propagation (Raghavan 2007) is a cheaper but noisier alternative.

No mainstream .NET library ships these (QuikGraph doesn't), but they're small. Two ways to use the result:
1. **As layout zones** (replace namespace clusters entirely) — honest, but can confuse users when a community ≠ its namespace.
2. **As a visual overlay** (recommended): keep namespace boxes, tint nodes by detected community, and show a "community X spans namespaces A,B,C" hint. Instantly surfaces misplaced classes and hidden coupling — very aligned with your "see how systems connect" goal.
3. Hybrid: intersect communities with namespace prefixes; only split a namespace box when a community is strongly separated.

### 4.3 Seeded/anchored layout
Let the user (or a config file) pin a few well-known anchor classes ("WeaponSystem top-left, UI bottom-right"); layout proceeds under those constraints. FIL's `LockPosition`/relative constraints support this directly; it converts "the tool guessed wrong" into a 30-second manual fix that persists.

---

## 5. External algorithms & libraries (what else exists, integration cost)

| Engine / algorithm | Type | Cluster-aware | Notes for this project |
|---|---|---|---|
| **Graphviz `sfdp`** (Yifan Hu 2005 multilevel force) | Force, multilevel | via `K` per cluster, `pack` | Reference standard for large sparse graphs. Integration: export DOT → run CLI → parse `plain` coords. Doable but adds a native-binary dependency; FIL gets you ~80% there in-proc. |
| **Graphviz `osage` / `patchwork`** | Cluster packing | native | Draws clusters as packed rectangles — the "zones as tiles" look. Same CLI integration. |
| **Graphviz `neato`** | Stress majorization (Kamada–Kawai / Gansner–Koren–North) | partial | MSAGL MDS is the same family — no reason to integrate. |
| **ELK** (Eclipse Layout Kernel) | Many: `layered` (with partitioning + model order), `force`, `stress`, `mrtree`, `radial`, `rectpacking`, `topdownpacking`, `disco` (disconnected components) | layered/stress yes | The most complete open layout library, but Java (or elkjs). Realistic only via a service/JS frontend — out of scope for the Avalonia app. Use its docs as a design reference (esp. `topdownpacking`, `interactive` layout options). |
| **OGDF `FM³`** (Hachul & Jünger 2004) | Force, multipole multilevel | yes (energy-based clustering) | Best-in-class open C++ force layout. C++ interop from Avalonia = high cost. |
| **GraphShape** (.NET/WPF) | FR, KK, ISOM, LinLog, **CompoundFDP**, EfficientSugiyama | CompoundFDP yes | Only .NET lib with compound force-directed layout. WPF-coupled, dormant-ish project; evaluate before relying on it (license/code extraction feasibility). |
| **fCoSE / CoSE** (Bilkent, Cytoscape.js) | Force on **compound graphs** + constraints (fixed node, alignment, relative placement) | native, best-in-class | JS-only, but its paper (Balcı & Doğrusöz, TVCG 2022) is the exact spec of what FIL-with-constraints should do — use as tuning reference. Sibling: CiSE (circular cluster layout). |
| **Gephi algorithms** | ForceAtlas2 (degree-weighted repulsion, LinLog mode → strong community emergence), OpenOrd (cuts long edges → crisp clusters), Yifan Hu | n/a | Not embeddable (Java), but ForceAtlas2 and OpenOrd are simple, well-documented algorithms if you ever hand-roll (§4.1 macro step is a fine place for ~100 lines of ForceAtlas2). |
| **yFiles** (commercial) | Organic (cluster-aware force), hierarchic with swimlanes, radial, tree | best-in-class | Not licensable here, but its *feature set* is the checklist to copy: organic layout + group nesting + incremental mode + edge bundling. |

---

## 6. Interaction patterns (the "usability" half of your question)

Placement alone won't deliver "easily add/remove classes and see how systems work". These patterns will, roughly cheapest → richest:

1. **Incremental re-layout / mental-map preservation** (Purchase et al.: users navigate by remembered positions). Mechanics: run layout from *current* positions, lock everything except changed nodes. With FIL: `LockPosition` constraints on untouched nodes + `ProcrustesCircleConstraint` alignment. You already persist manual positions as **deltas** (`ManualLayoutOverrides`) — combining deltas + a stabilizing force pass gives "add one class → it finds its spot, nothing else jumps".
2. **Semantic zoom / level of detail**. Zoomed out: zone rectangles + aggregate inter-zone highways (this alone answers "how are systems connected"). Zoomed in: full class boxes and edges. Hand-roll it (you have `NamespaceMatrix`, cluster bounds, and a Skia renderer — LOD is mostly a render-time filter), or go all-in on GraphMaps/`LgLayoutSettings` (§3.5).
3. **Zone collapse/expand**: double-click a zone → it becomes one supernode; its internal edges hide; cross edges re-anchor to the supernode. Requires the macro/micro split (§4.1) and turns 498 nodes into an explorable 30-node system map.
4. **Aggregate "highway" edges between zones**: one thick labeled edge ("Data → Systems: 23 refs") instead of 23 lines, expand on hover/click. Biggest single decluttering move available; pairs with bundling (§3.4).
5. **Ego / neighborhood view**: you have `FocusNavigator` — extend with an N-hop slider and a **radial ego layout** (focus at center, rings = hop distance; Graphviz `twopi`/ELK `radial` are the reference). For understanding one class's place, an ego view beats any global layout.
6. **Fisheye / degree-of-interest** (van Ham & Perer, "Search, Show Context, Expand on Demand"): full-size nodes near focus/search hits, shrinking with distance. Cheaper than collapse/expand, no model changes.
7. **Pin on drag**: dragging a node should pin it (`LockPosition`) so the next re-layout keeps it — one sentence of UX, one property in FIL.

---

## 7. Comparison matrix

| Approach | Zones separated | Connected = close | Hierarchy readable | Stable on add/remove | Effort here |
|---|---|---|---|---|---|
| Sugiyama + edge weights (§3.1) | ✗ | ~ | ✓✓ | ~ (delta overrides) | hours |
| MSAGL MDS (§3.3) | ~ (soft) | ✓✓ | ✗ | ✗ (re-run jumps) | 1 day (new engine kind) |
| MSAGL FastIncremental (§3.2) | ✓ (cluster gravity) | ✓✓ | ~ | ✓✓ (constraints/pins) | 2–4 days + tuning |
| Zone-first hybrid (§4.1) | ✓✓ **guaranteed** | ✓ (zone level) | ✓✓ (inside zones) | ✓✓ (only touched zone re-lays) | ~1 week |
| Community overlay (§4.2) | n/a (visual) | reveals truth | n/a | n/a | 2–3 days |
| Edge bundling (§3.4) | n/a | n/a | n/a | n/a | 1–2 days (routing only) |
| GraphMaps / LG zoom (§3.5) | ✓ | ✓ | ~ | ~ | 1–2 weeks |
| External (sfdp / ELK / OGDF) | ✓ | ✓✓ | varies | varies | high (interop) |

## 8. Data flow (brief — for completeness)

Per the archived doc: data flow needs a **call graph** (Roslyn method-body analysis + BFS from a seed), which is a different extraction, not a different class layout. When built, render it as its own view with a plain left-to-right Sugiyama on the small BFS subgraph (it shines on 10–50 nodes) or a sequence-diagram layout. Don't force it into the 498-class canvas. The zone-first map (§4.1) + aggregate highways (§6.4) *is* the static-structure answer to "how systems connect"; the call-graph view answers "how data flows".

---

## 9. My recommendation (opinionated)

1. **Now (hours)**: wire `LayoutEdgeWeights` → `Edge.Weight`; flip edge routing for inter-cluster edges to `SplineBundling`. Cheap, additive, reversible.
2. **Next (days)**: add `LayoutEngineKind.MsaglForce` (FIL) seeded from current Sugiyama positions, with pin-on-drag → `LockPosition`. This directly delivers "connected close / unrelated far" and stable manipulation. Also try `MdsLayoutSettings` as a one-enum-value experiment — keep whichever reads better.
3. **Core investment (~week)**: the **zone-first hybrid** (§4.1). It's the only option that guarantees zone separation, it makes every later feature (collapse/expand, highways, LOD, per-zone re-layout) straightforward, and it keeps Sugiyama doing what it's good at *inside* zones. Combined with community-detection tinting (§4.2) you get both "declared architecture" and "actual coupling" in one picture.
4. **Later**: aggregate highway edges + semantic zoom; treat GraphMaps as the stretch goal.
5. **Keep the custom Layered/Compound engines** untouched; add new kinds via the existing `LayoutEngineKind`/`MsaglEngineOptions` seam so every experiment is a flag, not a refactor.

## References

- MSAGL repo & GraphMaps: github.com/microsoft/automatic-graph-layout; Nachmanson et al., arXiv:1506.06745
- sfdp: Yifan Hu, *Efficient and High Quality Force-Directed Graph Drawing* (2005); graphviz.org/docs/layouts/sfdp
- ELK algorithm reference: eclipse.dev/elk/reference/algorithms.html
- fCoSE: Balcı & Doğrusöz, IEEE TVCG 28(12), 2022; CiSE (circular clusters), same lab
- ForceAtlas2: Jacomy et al., PLOS ONE 2014; OpenOrd: Martin et al. 2011; FM³: Hachul & Jünger 2004 (OGDF)
- PivotMDS: Brandes & Pich 2006; stress majorization: Kamada–Kawai 1989 / Gansner–Koren–North
- Louvain: Blondel et al. 2008; Leiden: Traag, Waltman, van Eck, Sci. Rep. 2019
- Edge bundling: Holten 2006 (hierarchical), Holten & van Wijk 2009 (force-directed); MSAGL metro-map bundling (Pupyrev et al.)
- Mental map: Purchase et al. 2007; DOI trees: van Ham & Perer 2009
- Verified locally: type/property dumps of `Microsoft.Msagl.dll` 1.2.1 (NuGet cache), this repo's `MsaglLayoutEngine`, `LayoutEdgeWeights`, `ManualLayoutOverrides`, `FocusNavigator`
