# LayoutOptions Split + Settings Dependency Model — Implementation Plan

Date: 2026-07-18
**Status: ✅ IMPLEMENTED (2026-07-18).** All 7 steps landed; 346/346 tests green
(327 pre-existing + 19 new: `LayoutOptionsFactoryTests`, `ProjectSettingsMigrationTests`,
`LayoutOptionsCloneTests`). Layout output is behavior-preserving; the only intentional
logic change is the R5 subgraph-inheritance fix (§1.6), inert until numeric options
become configurable. Historical docs (`docs/Done/*`, the 2026-07-02 chat transcript)
still reference the old boolean flags — they describe the past and are left unedited.

Audience: an LLM (or human) implementer. Self-contained: exact files, current values,
target values, order, verification, pitfalls.
Builds on: `2026-07-02-Clustering_connected_classes_in_MSAGL_layout.md`, which flagged
"`LayoutOptions` is a ~50-property god-object shared by all four engines" as the one real
architecture smell and the thing making layout changes risky. **Fix this BEFORE any further
layout-engine work** (edge weighting, connectivity sub-clusters, per-namespace composition),
because every one of those changes otherwise has to touch the same flat bag.

---

## 0. Ground truth — how layout settings work TODAY

### 0.1 The god-object: `src/MermaidDiagramExporter.Gui/Layout/LayoutOptions.cs`

42 public auto-properties in one flat class. Grouped below by **actual consumer**
(found by grepping `options.<Name>` across `Layout/`):

**Core — read by ≥2 engines, or by shared services/passes/post-passes (25 props):**

| Property | Default | Consumers |
|---|---|---|
| `Direction` | LeftToRight | `Compound/CoordinateAssignment`, `MsaglLayoutEngine`, `Passes/SubgraphDirectionSelectionPass`, `MeasurementPreparationPass` |
| `RankSpacing` | 90 | `LayeredLayoutEngine`, `Compound/CoordinateAssignment`, `MsaglLayoutEngine`, `Passes/RecursiveSpacingPass` |
| `NodeSpacing` | 18 | `SimpleColumnLayoutEngine`, `Compound/CoordinateAssignment`, `MsaglLayoutEngine`, `Passes/RecursiveSpacingPass` |
| `ClusterSpacing` | 30 | `LayeredLayoutEngine`, `Post/ClusterOverlapResolutionPass` |
| `GroupSpacing` | 26 | `MsaglLayoutEngine` (→ `ClusterMargin`), `SimpleColumnLayoutEngine` |
| `OuterMarginX/Y` | 40/52 | all 4 engines, `Compound/CompoundResultProjector`, all 3 post passes |
| `MinimumContentWidth/Height` | 2200/2200 | all 4 engines, `CompoundResultProjector` |
| `NodeWidth` | 280 | `LayoutGraphFactory`, `LayoutMeasurementService` |
| `MaxMeasuredNodeWidth` | 420 | `LayoutMeasurementService` |
| `ClusterTitleTopMargin/BottomMargin` | 8/8 | `LayoutMeasurementService` |
| `ClusterTitleHorizontalPadding` | 24 | `Post/ClusterBoundsPolishPass`, `SimpleColumnLayoutEngine` |
| `GroupLeftPadding` | 18 | `LayeredLayoutEngine`, `SimpleColumnLayoutEngine`, `ClusterBoundsPolishPass`, `CompoundResultProjector` |
| `GroupTopPadding` | 34 | `LayeredLayoutEngine`, `SimpleColumnLayoutEngine`, `Post/ClusterTitleMarginPass`, `CompoundResultProjector` |
| `GroupBottomPadding` | 18 | same four |
| `GroupWidth` | 320 | `LayeredLayoutEngine`, `SimpleColumnLayoutEngine`, `ClusterBoundsPolishPass`, `CompoundResultProjector` |

**Layered engine only (10 props):** `TargetRowWidth` (2400), `ComponentSpacing` (60),
`StructuredClusterMaxRowWidth` (980), `StructuredClusterMaxNodesPerRow` (3),
`StructuredNodeColumnSpacing` (24), `StructuredRankGap` (34), `StructuredWrappedRowGap` (14),
`StructuredRowIndentStep` (18), `StructuredRowMaxIndent` (56), `StructuredRowCenteringBias` (0.10)
— all read only inside `LayeredLayoutEngine.cs`.

**Custom prep-pipeline only (3 props):** `ClusterAnchorWidth/Height` (18/18) — read by
`Passes/SelfLoopExpansionPass` and `ClusterBoundaryEdgeNormalizer`; `RecursiveRankSpacingBonus`
(25) — read by `Passes/RecursiveSpacingPass`. These passes run for the **Layered and Compound**
engines only (`GraphLayoutCoordinator._pipeline`), never for MSAGL (`_msaglPipeline` is only
measurement + cluster hierarchy).

**Compound engine only (2 props):** `ClusterContainmentEdgeWeight` (24) — `Compound/CompoundGraphBuilder`;
`CoordinateAssignmentPasses` (6) — `Compound/CoordinateAssignment`.

**MSAGL engine only (3 props):** `UseMsaglEngine`, `SeparateAppAndTests`,
`PartitionByFirstLevelNamespace` — `MsaglLayoutEngine`.

**Engine selection (1 prop):** `UseCompoundLayoutEngine` — read only in
`GraphLayoutCoordinator.CreateLayout`.

**⚠ DEAD properties (2):** `NodeColumnSpacing` (16) and `MaxClusterColumns` (3) are declared
in `LayoutOptions.cs:22-23` and **never read anywhere in the repo** (only
`StructuredNodeColumnSpacing` is used). Delete them in this refactor.

### 0.2 Duplication into `ProjectSettings`

`src/MermaidDiagramExporter.Gui/Settings/ProjectSettings.cs` re-declares the 4 flag properties
(`UseCompoundLayoutEngine` :94, `UseMsaglEngine` :102, `SeparateAppAndTests` :109,
`PartitionByFirstLevelNamespace` :116) for JSON persistence. None of the ~35 numeric layout
properties is persisted at all — **there is no way for a user to tune spacing per project;
`LayoutOptions` defaults are the only values ever reached from the UI.**

### 0.3 The settings → options mapping is hand-copied 4×, and 3 of the 4 are buggy

`MainWindow.axaml.cs` constructs `LayoutOptions` in 4 places:

| Site | Line | Copies |
|---|---|---|
| Design-mode edge re-route | :208 | `UseCompoundLayoutEngine`, `UseMsaglEngine` — **drops both partition flags** |
| `OnDesignResetLayout` | :1059 | same — **drops both partition flags** |
| Analyze-mode layout (settings load) | :1559 | all 4 flags ✅ |
| `RedrawEdgesNow` (Ctrl+R / drag) | :2068 | `UseCompoundLayoutEngine`, `UseMsaglEngine` — **drops both partition flags** |

No site forwards any numeric property. The 3 buggy sites mostly don't matter today (they run
when `_layoutEngine.LayoutOptions` is null, which is rare), but they are exactly the kind of
quiet divergence the god-object invites.

### 0.4 Setting dependencies today — all implicit or UI-only

| # | Rule | Enforced where today |
|---|---|---|
| R1 | `UseMsaglEngine=true` ⇒ `UseCompoundLayoutEngine` is ignored (MSAGL wins) | Comment on both properties + `if/else` order in `GraphLayoutCoordinator.CreateLayout` :59-69. Not enforced in the type — both can be true. |
| R2 | `SeparateAppAndTests` ⊕ `PartitionByFirstLevelNamespace` (mutually exclusive) | **Only** in `SettingsWindow.axaml.cs` :93-102 Click handlers (checking one unchecks the other). If a hand-edited JSON has both `true`, the engine silently prefers `PartitionByFirstLevelNamespace` (`MsaglLayoutEngine.cs` :228-231) — the UI and the engine disagree about who wins. |
| R3 | Partition flags are meaningless unless `UseMsaglEngine=true` | Nowhere. UI lets you check them with MSAGL off; they silently do nothing. |
| R4 | `PackingMethod.Columns` is derived from R2 flags | Computed inline in `MsaglLayoutEngine.cs` :110. Fine, but it's a hidden derived setting. |
| R5 | Subgraph measurement inherits parent options | **Broken/partial**: `MeasurementPreparationPass.CreateSubgraphOptions` :69-83 news up a fresh `LayoutOptions` copying only `Direction/NodeSpacing/RankSpacing/OuterMarginX/Y` — every other field (incl. `NodeWidth`, `MaxMeasuredNodeWidth`, cluster title metrics) silently resets to defaults inside extracted subgraphs. Also calls `new LayoutOptions()` twice just to read defaults (:76, :79). |
| R6 | Numeric layout values are not user-configurable | By omission — no persistence, no UI. Out of scope to fix here, but the new model must not make it harder. |

Established precedent for dependent settings already exists in the codebase — follow it:
`LlmSettings.EffectiveBaseUrl` (computed from `Provider` + `BaseUrl` override) and
`SettingsService.ResolveCacheDirectory` / `ResolveSourceBundleDirectory` (custom path or
default). **Dependent settings become computed properties / resolver methods, never
independently stored values that can disagree.**

---

## 1. Target architecture

### 1.1 Kill boolean-dependency pairs with enums (R1, R2 become unrepresentable)

```csharp
// Layout/LayoutEngineKind.cs
public enum LayoutEngineKind { Layered, Compound, Msagl }

// Layout/MsaglPartitionMode.cs
public enum MsaglPartitionMode { None, AppVsTests, FirstLevelNamespace }
```

One enum replaces `UseCompoundLayoutEngine`+`UseMsaglEngine` — "MSAGL takes precedence over
Compound" (R1) can no longer be expressed, so it can no longer be violated. One enum replaces
`SeparateAppAndTests`+`PartitionByFirstLevelNamespace` — mutual exclusion (R2) likewise.

### 1.2 Split `LayoutOptions` into core + per-engine groups

`LayoutOptions` keeps the 23 live core properties plus one group property per engine.
**`IGraphLayoutEngine.Run(LayoutGraph, LayoutOptions)` signature does not change** —
engines reach into their group (`options.Msagl`, `options.Compound`, `options.Layered`).
Deliberate KISS choice: this refactor changes *shape*, not *interfaces*; engines and passes
keep compiling with mechanical edits only.

```csharp
public sealed class LayoutOptions
{
    // engine selection (replaces 2 bools)
    public LayoutEngineKind Engine { get; set; } = LayoutEngineKind.Layered;

    // core: direction, spacing, margins, content bounds, measurement, cluster chrome
    public LayoutDirection Direction { get; set; } = LayoutDirection.LeftToRight;
    public float RankSpacing { get; set; } = 90f;
    // ... (all 23 live core props, same names + defaults)

    // per-engine groups
    public LayeredEngineOptions Layered { get; set; } = new();
    public CompoundEngineOptions Compound { get; set; } = new();
    public MsaglEngineOptions Msagl { get; set; } = new();
    public CustomPipelineOptions Pipeline { get; set; } = new();
}

public sealed class LayeredEngineOptions       // read only by LayeredLayoutEngine
{
    public float TargetRowWidth { get; set; } = 2400f;
    public float ComponentSpacing { get; set; } = 60f;
    public float StructuredClusterMaxRowWidth { get; set; } = 980f;
    // ... the other 7 Structured* props, same names + defaults
}

public sealed class CompoundEngineOptions      // read only by Compound/* engine classes
{
    public float ClusterContainmentEdgeWeight { get; set; } = 24f;
    public int CoordinateAssignmentPasses { get; set; } = 6;
}

public sealed class MsaglEngineOptions         // read only by MsaglLayoutEngine
{
    /// <summary>Top-level partitioning. Only consulted when Engine == Msagl (R3 by
    /// construction: the MSAGL engine is the only reader).</summary>
    public MsaglPartitionMode Partition { get; set; } = MsaglPartitionMode.None;
}

public sealed class CustomPipelineOptions      // read only by GraphLayoutCoordinator._pipeline passes
{
    public float ClusterAnchorWidth { get; set; } = 18f;
    public float ClusterAnchorHeight { get; set; } = 18f;
    public float RecursiveRankSpacingBonus { get; set; } = 25f;
}
```

New files: `Layout/LayoutEngineKind.cs`, `Layout/MsaglPartitionMode.cs`,
`Layout/LayeredEngineOptions.cs`, `Layout/CompoundEngineOptions.cs`,
`Layout/MsaglEngineOptions.cs`, `Layout/CustomPipelineOptions.cs`.
Edited: `Layout/LayoutOptions.cs` (slimmed to core + groups).

Consumer edits (mechanical, exact list from §0.1):
- `LayeredLayoutEngine.cs` → `options.Layered.*` for its 10 props.
- `Compound/CompoundGraphBuilder.cs`, `Compound/CoordinateAssignment.cs` → `options.Compound.*`.
- `MsaglLayoutEngine.cs` → `options.Msagl.Partition` (:110 packing decision, :228-231
  `PartitionClustersIfEnabled`); `PartitionByAppAndTests`/`PartitionByFirstLevel` called via
  `switch` on the enum.
- `ClusterBoundaryEdgeNormalizer.cs`, `Passes/SelfLoopExpansionPass.cs`,
  `Passes/RecursiveSpacingPass.cs` → `options.Pipeline.*`.
- `GraphLayoutCoordinator.cs` :52-78 → `switch (resolvedOptions.Engine)` selecting pipeline,
  engine, and post-layout skip (R1 gone; MSAGL precedence is now just a `case`).

### 1.3 One factory, one home for cross-field rules

```csharp
// Layout/LayoutOptionsFactory.cs
public static class LayoutOptionsFactory
{
    /// <summary>The ONLY place ProjectSettings → LayoutOptions mapping happens.</summary>
    public static LayoutOptions FromSettings(ProjectSettings settings) => new()
    {
        Engine = settings.Engine,
        Msagl = { Partition = settings.Msagl.PartitionMode },
    };
}
```

All 4 `MainWindow.axaml.cs` sites (:208, :1059, :1559, :2068) become
`var options = _layoutEngine.LayoutOptions ?? LayoutOptionsFactory.FromSettings(_currentSettings);`
— the dropped-partition-flags bug (§0.3) dies with the duplication.

### 1.4 `ProjectSettings` restructure + legacy migration

```csharp
// replaces the 4 bools at ProjectSettings.cs :89-116
public LayoutEngineKind Engine { get; set; } = LayoutEngineKind.Layered;
public MsaglLayoutSettings Msagl { get; set; } = new();   // mirrors the LlmSettings precedent

public sealed class MsaglLayoutSettings   // in Settings/ or Layout/ next to the enum
{
    public MsaglPartitionMode PartitionMode { get; set; } = MsaglPartitionMode.None;
}
```

**Legacy JSON migration (required — STJ silently ignores unknown fields, so without this
every existing user's engine choice resets to Layered):**

Keep the 4 old properties, marked `[Obsolete]`, as plain get/set auto-properties. Add:

```csharp
/// <summary>Folds legacy boolean flags into the enum model and normalizes cross-field
/// rules. Called by SettingsService after every Load and by SettingsWindow before Save.
/// Idempotent. The single home for setting-dependency rules that enums can't encode.</summary>
public void Normalize()
{
    if (Engine == LayoutEngineKind.Layered)
    {
#pragma warning disable CS0618
        if (UseMsaglEngine) Engine = LayoutEngineKind.Msagl;
        else if (UseCompoundLayoutEngine) Engine = LayoutEngineKind.Compound;

        if (Msagl.PartitionMode == MsaglPartitionMode.None)
        {
            if (PartitionByFirstLevelNamespace) Msagl.PartitionMode = MsaglPartitionMode.FirstLevelNamespace;
            else if (SeparateAppAndTests) Msagl.PartitionMode = MsaglPartitionMode.AppVsTests;
        }

        UseMsaglEngine = UseCompoundLayoutEngine = false;
        SeparateAppAndTests = PartitionByFirstLevelNamespace = false;
#pragma warning restore CS0618
    }
}
```

Note the precedence inside the legacy fold matches the old engine behavior
(`PartitionByFirstLevelNamespace` wins over `SeparateAppAndTests`) — preserves semantics of
any hand-edited JSON. After `Normalize()` the legacy fields are false, so re-saved JSON is
inert. (`Normalize` is also where future cross-field rules go — see §3.)

### 1.5 SettingsWindow: dependencies visible in the UI instead of hacked in Click handlers

Replace the 4 checkboxes (`UseCompoundEngineCheck`, `UseMsaglEngineCheck`,
`SeparateAppAndTestsCheck`, `PartitionByFirstLevelNsCheck`) with:

- `EngineCombo` — 3 items (Layered / Compound / MSAGL), maps to `ProjectSettings.Engine`.
- `MsaglPartitionCombo` — 3 items (None / Application vs Tests / First-level namespace),
  `IsEnabled = EngineCombo.SelectedIndex == 2` (R3 enforced by disabling, not by hope).

Delete the mutual-exclusivity Click handlers (`SettingsWindow.axaml.cs` :93-102) — R2 no
longer exists. Update `LoadForProject` (:87-90), `OnSave` (:166-169), `OnResetDefaults`
(:205-208) accordingly. XAML edits in `SettingsWindow.axaml` to swap the controls.

### 1.6 Fix R5 (subgraph option inheritance) — the one intentional behavior change

`MeasurementPreparationPass.CreateSubgraphOptions` should **clone the parent options and
override only what the subgraph specifies**, instead of starting from defaults:

```csharp
private static LayoutOptions CreateSubgraphOptions(LayoutOptions parent, LayoutSubgraph subgraph)
{
    var clone = parent.Clone();            // add a deep-copy Clone() on LayoutOptions + groups
    clone.Direction = subgraph.Direction;
    if (subgraph.Spacing is { } s)
    {
        if (s.NodeSeparation > 0f) clone.NodeSpacing = s.NodeSeparation;
        if (s.RankSeparation > 0f) clone.RankSpacing = s.RankSeparation;
        clone.OuterMarginX = s.MarginX;
        clone.OuterMarginY = s.MarginY;
    }
    return clone;
}
```

Caller `CloneMeasuredSubgraph` passes the pass's `options` down. ⚠ This **changes rendered
output** for graphs with extracted subgraphs (they previously measured with default
`NodeWidth`/title metrics). It is a bug fix, but expect screenshot diffs; verify against
`CompoundLayoutEngineTests` and any subgraph fixtures.

---

## 2. Steps in order (build + run tests after EACH)

1. **Enums + group classes** — add the 6 new files (§1.1, §1.2). Nothing references them yet.
   `dotnet build` green.
2. **Slim `LayoutOptions`** — move the 15 non-core props into the groups; delete dead
   `NodeColumnSpacing`/`MaxClusterColumns`; fix every consumer per §1.2 (7 files).
   `dotnet build` + full test suite green — layout output must be byte-identical
   (same defaults, same code paths). This step is pure refactor.
3. **`LayoutOptionsFactory`** + rewire the 4 `MainWindow.axaml.cs` sites (§1.3). Build green.
4. **`ProjectSettings` restructure + `Normalize()` + legacy migration** (§1.4);
   `SettingsService.LoadSettings` calls `Normalize()` right after deserialize (:98-103)
   and on the default path (:108). `SettingsServiceTests`-style roundtrip + migration tests.
5. **SettingsWindow UI** (§1.5): XAML + code-behind, remove Click hacks. Manual check:
   open settings, toggle engine combo, verify partition combo enables/disables, save, reload.
6. **R5 fix** (§1.6): `Clone()` on `LayoutOptions` (+ groups), rewrite
   `CreateSubgraphOptions`, run `CompoundLayoutEngineTests` / `CompoundEdgeDummyChainTests`.
7. **Docs** — update any `docs/` references to the old property names if they exist
   (grep `UseMsaglEngine`, `SeparateAppAndTests` under `docs/`).

---

## 3. The dependency matrix — single source of truth

After this refactor, every cross-setting rule lives in exactly one of three places:

| Rule | Kind | Home after refactor |
|---|---|---|
| Engine precedence (was R1) | unrepresentable | `LayoutEngineKind` enum |
| Partition mutual exclusion (was R2) | unrepresentable | `MsaglPartitionMode` enum |
| Partition applies only under MSAGL (R3) | structural + UI | `MsaglEngineOptions` read only by `MsaglLayoutEngine`; `MsaglPartitionCombo.IsEnabled` bound to engine selection |
| `PackingMethod.Columns` derivation (R4) | computed | stays computed inline in `MsaglLayoutEngine` from `options.Msagl.Partition != None` |
| Subgraph inheritance (R5) | explicit clone | `MeasurementPreparationPass.CreateSubgraphOptions` |
| Legacy flag migration | normalize-on-load | `ProjectSettings.Normalize()` (called by `SettingsService`) |
| Cache/bundle folder fallback | computed resolver | existing `SettingsService.ResolveCacheDirectory`/`ResolveSourceBundleDirectory` — unchanged |
| LLM base URL per provider | computed property | existing `LlmSettings.EffectiveBaseUrl` — unchanged |

**Rule for the future (write it into `AGENTS.md` if one is added):** a setting that depends
on another setting must be (a) merged into an enum with its siblings, (b) a computed
property/resolver, or (c) a line in `Normalize()` — never a second stored field that can
disagree with the first, and never enforced only in UI event handlers.

---

## 4. Tests

New (follow `AppSettingsServiceTests.cs` / `CompoundLayoutEngineTests.cs` patterns):

- `tests/.../LayoutOptionsFactoryTests.cs` — mapping defaults; each engine kind maps through.
- `tests/.../ProjectSettingsMigrationTests.cs` —
  legacy JSON `{ "useMsaglEngine": true, "separateAppAndTests": true }` →
  `Engine == Msagl`, `PartitionMode == AppVsTests`, legacy bools reset;
  both partition bools true → `FirstLevelNamespace` (matches old engine precedence);
  new-format JSON roundtrips unchanged; `Normalize()` idempotent (`Normalize(); Normalize()` — same state).
- `tests/.../LayoutOptionsCloneTests.cs` — deep copy mutates neither parent nor sibling groups.

Must stay green: `CompoundLayoutEngineTests`, `CompoundEdgeDummyChainTests`,
`CliAndEndToEndTests`, `AppSettingsServiceTests`, `RenderingBenchmarkTests` (watch for
unexpected layout diffs — only Step 6 may change output).

## 5. Pitfalls

- **STJ drops unknown JSON fields silently** — skipping the legacy fold (Step 4) resets every
  existing user's engine to Layered without any error.
- `MeasurementPreparationPass` currently news up `LayoutOptions` twice just to read defaults
  (:76, :79) — after the split, defaults live on the groups; `new LayoutOptions().NodeSpacing`
  still compiles (core prop) but don't cargo-cult the pattern into group props.
- Grep `new LayoutOptions` repo-wide when rewiring — there are exactly 5 construction sites
  today (4 in `MainWindow.axaml.cs`, 1 in `MeasurementPreparationPass`) plus the fallback
  `?? new LayoutOptions()` in `GraphLayoutCoordinator` :46, :103 and `LayoutEngine.cs`
  :29, :57, :68. The coordinator/engine fallbacks are fine (pure defaults); the
  `MeasurementPreparationPass` one is Step 6.
- Do **not** change any default value in this refactor — behavior preservation is what makes
  it safe to land before the clustering work.
- Keep `MsaglLayoutSettings`/`LlmSettings`-style nested groups camelCase-serializable
  (STJ options in `SettingsService` already set `JsonNamingPolicy.CamelCase`) — nested objects
  serialize fine, no service changes needed beyond the `Normalize()` call.
- **Out of scope (do not gold-plate):** UI/persistence for the ~23 numeric layout fields
  (R6 — separate future doc); DI container; changing `IGraphLayoutEngine` signature;
  the actual clustering improvements from the 2026-07-02 doc (they come after this lands).
