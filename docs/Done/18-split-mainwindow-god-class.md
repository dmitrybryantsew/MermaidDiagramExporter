# Step 18 — Reduce `MainWindow`'s responsibilities (extract, don't redesign)

## Problem (in plain language)

`MainWindow.axaml.cs` currently orchestrates scanning, caching, layout,
focus navigation, search, the matrix view, settings, and export, all in one
class, with its constructor alone wiring up 8 event handlers. This makes it
hard to test (you can't meaningfully unit test "what happens when a scan
completes" without spinning up the whole window) and hard to safely modify
(any change risks touching unrelated orchestration logic by accident, since
it's all in one place).

## Why this is the highest-risk step in the entire plan

This is **more invasive** than Step 17's `GraphCanvas` split, because
`MainWindow` is the central orchestration point that wires everything else
in the application together — by definition, touching it touches the most
call paths. **Do this step last, after every other step has been completed
and verified.** Do not attempt this on a codebase where earlier steps
(especially Step 16's dependency injection groundwork) haven't already
landed — Step 16 is a prerequisite, since extracting orchestration logic
out of `MainWindow` is much safer once dependencies are already passed in
via constructors rather than constructed ad-hoc inside `MainWindow` itself.

## Recommended scope: a mediator/coordinator, not a full MVVM rewrite

The review suggests "a `MainWindowViewModel` or mediator pattern." A full
MVVM conversion (introducing `INotifyPropertyChanged`, data-binding
everything, etc.) is a much larger and riskier undertaking than this plan
should ask of a less-capable model in one step. **Scope this step to
extracting orchestration logic into a small number of focused coordinator
classes that `MainWindow` delegates to — not a full ViewModel/MVVM rewrite.**
If you find yourself wanting to introduce data-binding infrastructure to
make this work, that's a sign you've expanded scope beyond what this step
asks — stop and report rather than proceeding into a much larger change.

## What to find

- Open `MainWindow.axaml.cs` in full and read it end to end before changing
  anything.
- In the constructor, list out all 8 event handler wirings the review
  mentions (`grep -n "+=" <path-to-MainWindow.axaml.cs>` will catch most
  event subscriptions) and note which area of functionality each one
  belongs to (scanning, caching, layout, focus nav, search, matrix view,
  settings, export).
- Group the class's methods by which of those 8 areas they belong to. Some
  methods may genuinely belong to "general window plumbing" (e.g. handling
  window close, generic UI glue) rather than any specific feature area —
  that's fine, those stay in `MainWindow`.

## The fix

Pick **one** feature area to extract first as a proof of concept before
doing the rest — **scanning/caching orchestration** is a reasonable first
choice since it's described as one cohesive flow (scan → cache check →
cache load or rescan), per the P0 features in the review.

1. Create a new class, e.g. `ScanCoordinator`, in a new file.
2. Move the methods and fields related purely to scan/cache orchestration
   into it (the actual scan-triggering logic, cache-validation-decision
   logic, progress reporting if any) — following the same "extract nearly
   verbatim, don't redesign" principle as Step 17.
3. `ScanCoordinator` will need access to whatever `MainWindow` currently
   uses for this (the `RoslynTypeScanner` factory and `TypeGraphCacheService`
   from Step 16's DI work, plus whatever UI elements it currently updates
   directly, like a progress bar or status text). For UI updates, the
   cleanest minimal-risk approach: have `ScanCoordinator` expose events
   (e.g. `ScanProgressChanged`, `ScanCompleted`) that `MainWindow`
   subscribes to and uses to update its own UI elements directly — this
   keeps `ScanCoordinator` itself free of any direct Avalonia UI control
   references, while `MainWindow` keeps doing what it already does
   (updating its own controls), just triggered by an event from
   `ScanCoordinator` instead of inline code.
4. `MainWindow`'s constructor now constructs (or receives via Step 16's DI)
   a `ScanCoordinator` instance, subscribes to its events, and calls into it
   instead of containing the orchestration logic directly.
5. `dotnet build`. Fix compile errors one at a time.
6. `dotnet test`.
7. If you can run the GUI, perform an actual scan and confirm it still
   works exactly as before (progress reporting, cache prompt dialog
   triggering correctly, results appearing). If you cannot run the GUI,
   state this limitation explicitly and rely on careful tracing.

## After the first extraction succeeds

Only after `ScanCoordinator` is fully extracted, built, tested, and (ideally)
manually verified, consider whether to continue with additional coordinators
for the other feature areas (e.g. `SearchCoordinator`, `ExportCoordinator`)
using the exact same process. **Treat each as its own fully-verified
sub-step, in the same spirit as Step 16's 16a/16b/16c breakdown.** Do not
extract multiple feature areas in a single unverified pass.

If, after completing the scan/cache extraction, you judge that the
remaining areas are lower priority, tightly coupled to each other in ways
that resist clean separation, or that further extraction risk outweighs
benefit at this point — it is completely acceptable to stop after the first
extraction and report this as a deliberate, reasoned stopping point, rather
than forcing every remaining area through the same process at elevated risk.

## Constraints

- Do not change any actual behavior — every extraction should be
  behavior-preserving.
- Do not introduce a DI container or MVVM framework as part of this step
  (see scope note above).
- Do not let any new coordinator class take a direct dependency on Avalonia
  UI controls (`Button`, `TextBox`, etc.) — keep them UI-framework-agnostic,
  communicating back to `MainWindow` via plain events or return values, so
  they remain testable without spinning up a window. This mirrors why
  `MermaidDiagramExporter`'s `Core` project is kept UI-agnostic in the first
  place (per the review's praise of that separation) — the same principle
  should apply to these new coordinators.
- If at any point a clean extraction seems to require also fixing an
  unrelated bug you notice along the way, do not fix it inline as part of
  this step — note it separately. Keep this step scoped to the
  responsibility-splitting, not opportunistic unrelated fixes.

## Done when

- At minimum, scan/cache orchestration logic has been extracted from
  `MainWindow` into a separate, UI-framework-agnostic coordinator class.
- `MainWindow` delegates to it via constructor-injected dependency (per
  Step 16) and subscribes to its events for UI updates.
- No behavior changed.
- Build and tests pass.
- Any further extractions beyond the first are done with the same rigor, or
  explicitly deferred with reasoning given.
