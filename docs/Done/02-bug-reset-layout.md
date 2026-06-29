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
