# Step 21 — Add a basic frame-time benchmark for rendering

## Problem (in plain language)

There are currently no performance benchmarks measuring how long it takes
to render a frame for a graph with N nodes. This means Steps 10–13 (paint
caching, picture caching, partial redraw, minimap fix) have no automated,
repeatable way to prove they actually improved anything, beyond manual
visual impression. A simple benchmark lets you (and future maintainers)
catch a future performance regression automatically, rather than only
noticing "huh, dragging feels slower than I remember" months later.

**Do this step last, after Steps 10–13 are complete.** Otherwise you have
nothing meaningful to benchmark yet, and no "before" baseline to compare
"after" against if you write it before those fixes land. If you'd like a
before/after comparison, this also means: if you have the ability to check
out the codebase state from before Step 10 (e.g. via your earlier
per-step commits/snapshots, per the master plan's "commit after each step"
instruction), running this same benchmark against that earlier snapshot and
recording the numbers would make a genuinely useful comparison — but this
is a nice-to-have, not a hard requirement for completing this step.

## What to find

- Confirm whether the project already references a benchmarking library —
  search `grep -rn "BenchmarkDotNet" **/*.csproj`. If `BenchmarkDotNet` (the
  standard .NET micro-benchmarking library) is already referenced anywhere,
  use it and follow its existing usage conventions in this codebase. If it
  is not referenced anywhere, **do not add a new package dependency just
  for this step** — instead, write a simple, dependency-free benchmark using
  a basic `Stopwatch`-based timing harness inside a regular test (or a
  small separate console/test utility), which is more than sufficient for
  this codebase's needs and avoids introducing a new dependency for a
  "nice to have" measurement tool.
- Find the main render entry point in `GraphCanvas` (same one touched in
  Steps 10–12).
- Find (or note the absence of) any existing way to construct a synthetic
  test graph with a controllable number of nodes/edges for testing purposes
  — search `grep -rn "GenerateTestGraph\|CreateTestGraph\|SampleGraph" --include=*.cs .`.
  If nothing like this exists, you'll need to write a small helper that
  builds a `TypeGraph` (or whatever the in-memory graph type is) with N
  synthetic nodes and a reasonable number of synthetic edges between them,
  for benchmarking purposes — keep this helper simple (e.g. nodes in a grid
  or random positions, edges connecting some fraction of node pairs) since
  its only job is to produce *some* representative graph at a chosen size,
  not to model realistic code structure.

## The fix

1. Decide where this benchmark lives. If `BenchmarkDotNet` is available and
   already used elsewhere, follow that existing pattern/project location. If
   writing a simple `Stopwatch`-based version instead, a new test method (or
   a small standalone benchmark runner if rendering can't run headless
   inside the normal xUnit/NUnit test host — see the note below) in the
   `Tests` project is a reasonable location.
2. **Important caveat to investigate first**: `GraphCanvas` renders via
   SkiaSharp into a surface that's normally backed by an Avalonia
   `WriteableBitmap`/window surface. Confirm whether rendering can actually
   run in a headless test environment (no real window/GPU context) in this
   project — search for whether `Tests` project already does any rendering-
   adjacent testing (the review states it currently does NOT — "No tests
   for GraphCanvas rendering (hard without headless Skia)"). If genuinely no
   headless rendering path exists and creating one is non-trivial, the
   honest and correct outcome for this step may be to benchmark a narrower,
   headless-friendly piece instead — e.g. directly time the **draw-call
   preparation** work (building the list of what needs to be drawn, doing
   coordinate transforms, picture-recording per Step 11) using a plain
   in-memory `SKSurface` created off-screen (SkiaSharp supports creating
   raster surfaces without any window/GPU context — `SKSurface.Create` with
   an `SKImageInfo` works headlessly and is what you should use here,
   rather than needing an actual Avalonia window). This still exercises the
   real rendering code path (since `GraphCanvas`'s draw methods take an
   `SKCanvas`, which you can get from a headless `SKSurface` just as validly
   as from a window-backed one) without requiring a live GUI.
3. Write a benchmark that:
   - Constructs a synthetic graph at a chosen size (start with one size,
     e.g. 200 nodes, matching the review's stated example size; optionally
     add a couple more sizes like 50 and 500 if easy to parametrize, but
     don't over-engineer this).
   - Creates a headless `SKSurface` of a reasonable canvas size.
   - Times N repeated calls to the main render method (e.g. 100 iterations),
     using `Stopwatch`, and reports average time per frame.
   - Prints or asserts on the result. A reasonable, conservative initial
     assertion: average frame time should be under some generous threshold
     (e.g. 50ms) — set this loosely enough that it won't flake on a slower
     CI machine, since the goal right now is "have a number we can track
     over time and a sanity ceiling," not "enforce a tight performance SLA"
     this early. If you have a believable "before Step 10" baseline number
     (per the optional before/after comparison above), you can additionally
     report the percentage improvement, but don't block the test on hitting
     a specific improvement percentage — environments vary too much for
     that to be a reliable hard assertion.

## Constraints

- Do not add a new package dependency (like `BenchmarkDotNet`) if one isn't
  already present — keep this dependency-free using `Stopwatch`.
- Do not try to benchmark actual GUI interaction (real pointer events,
  actual window rendering) — scope this to the headless rendering call
  path only, as described above.
- If after investigating you find headless rendering genuinely isn't
  feasible at all in this environment for some reason not anticipated
  above, it is acceptable to stop and report that finding clearly, rather
  than forcing something fragile into place. A clearly-documented "this
  isn't currently benchmarkable, here's why, here's what would be needed"
  is a legitimate and useful outcome for this step.

## Verification

1. `dotnet build`.
2. `dotnet test` (or run the standalone benchmark utility, if you went that
   route) — confirm the benchmark runs to completion and produces a
   sensible (non-zero, non-absurd) frame-time number.

## Done when

- A headless, dependency-free (`Stopwatch`-based, unless `BenchmarkDotNet`
  was already present) benchmark exists that exercises `GraphCanvas`'s real
  rendering code path against a synthetic graph of a controllable size.
- It runs successfully and reports a frame-time figure.
- Build passes.
