# Fix Plan: MermaidDiagramExporter — Master Index

## Why this document exists

You (the model executing this) are working through a list of known issues
in an existing C# / Avalonia / SkiaSharp codebase called MermaidDiagramExporter.
A senior-level code review already happened. This plan turns that review into
a sequence of small, independent, verifiable steps.

**You did not write this codebase and you have not seen the full review.**
Do not assume you know the surrounding code. Every step below tells you
exactly what to search for and how to confirm you found the right thing
before you change anything.

## Ground rules — read before doing anything


1. **Run the existing test suite after every step that touches non-UI code**
   (i.e. anything in the Core project). `dotnet test` on the Tests project.
   If a step is in the Gui project only (rendering, XAML, controls), building
   successfully is the minimum bar — UI behavior should also be sanity
   checked by reading the diff, since there may be no automated coverage.
2. **Do not delete tests, comments, or XML doc summaries** unless a step
   explicitly tells you to.
3. **Commit (or at least snapshot/diff) after each numbered step**, with a
   commit message matching the step's title. This makes it trivial to revert
   a single step if it turns out to be wrong, without losing the others.


## Order of operations and why

The steps are ordered from **safest and most isolated** to **riskiest and
most invasive**. Do not reorder them. Rationale:

| Phase | Steps | Why this order |
|---|---|---|
| 1. Correctness bugs | 01–06 | Small, self-contained, each touches 1–2 methods, no architectural risk, high value. Fixing these first means later performance/architecture work isn't built on top of known-buggy logic. |
| 2. Security hardening | 07–09 | Also isolated and low-risk to the build, but conceptually separate from "bugs" — these are about untrusted input, not incorrect logic. |
| 3. Performance | 10–13 | Touches rendering code more broadly. Safer to do after correctness fixes so you're not optimizing code you're also simultaneously debugging. |
| 4. Maintainability / magic numbers | 14–15 | Mechanical but touches many call sites — easy to verify, low risk, but only useful once the behavior underneath is already correct. |
| 5. Architecture (DI, SRP splits) | 16–18 | Highest risk, broadest surface area, most likely to break things if done carelessly. Done last, with the most hand-holding, after everything underneath is already solid. |
| 6. Testing gaps | 19–21 | New tests are easiest to write correctly once the code they test is already fixed — otherwise you might write a test that encodes the bug as "expected" behavior. |

## File list

- `02-fix-cluster-bounds-padding.md`
- `03-fix-png-background-color-constant.md`
- `04-fix-focus-navigator-mismatch.md`
- `05-fix-manual-layout-changed-event-noop.md`
- `06-add-stereotype-regex-validation-feedback.md`
- `07-fix-regex-dos-stereotype-engine.md`
- `08-fix-path-traversal-folder-input.md`
- `09-add-file-size-limit-source-bundle.md`
- `10-cache-skpaint-objects.md`
- `11-skpicture-static-content-caching.md`
- `12-partial-redraw-on-drag.md`
- `13-fix-minimap-png-encoding-overhead.md`
- `14-extract-magic-numbers-to-constants.md`
- `15-centralize-edge-id-construction.md`
- `16-introduce-dependency-injection.md`
- `17-split-graphcanvas-responsibilities.md`
- `18-split-mainwindow-god-class.md`
- `19-add-manual-layout-overrides-roundtrip-test.md`
- `20-add-cache-invalidation-threshold-tests.md`
- `21-add-frame-time-benchmark.md`

## Definition of done for the whole plan

- Solution builds with zero errors and zero new warnings.
- All existing tests pass.
- Each numbered step has a corresponding commit/diff that can be reviewed
  in isolation.

Proceed to `02-fix-cluster-bounds-padding.md`.
