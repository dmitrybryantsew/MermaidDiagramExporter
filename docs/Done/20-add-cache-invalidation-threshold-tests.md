# Step 20 — Add tests for cache invalidation thresholds

## Problem (in plain language)

The cache system decides whether a saved scan cache is still usable, or
needs a rebuild, based on some "percentage of files changed" threshold
(the review mentions "10% change detection" as an example). There are
currently no tests exercising this threshold logic directly — meaning the
boundary behavior (what happens at exactly the threshold, just under it,
just over it) is unverified, and Step 01's fix (adding new-file detection
to the change count) has no dedicated test confirming it actually changed
the right outcome in a realistic scenario.

**Do this step after Step 01 is complete and merged** — these tests should
exercise the corrected logic, including the new-file-detection fix, not
the pre-fix buggy behavior.

## What to find

- The same `TypeGraphCacheService.ValidateManifest` method from Step 01.
- The actual threshold value and how it's computed — find where the
  "percentage changed" or "should rebuild" decision is made, and what
  number it's compared against (the review mentions "10%" — confirm the
  actual constant/value in the real code, it may differ).
- The existing test project's conventions for testing this class, if any
  partial coverage already exists (search `grep -rln "TypeGraphCacheService" --include=*.cs . | grep -i test`).

## The fix

Write a small set of focused tests covering the threshold boundary, using
the existing test framework and conventions in the `Tests` project:

1. **No changes**: manifest matches current files exactly (same files, same
   hashes) → should report "up to date," no rebuild needed.
2. **Below threshold**: a small number of files changed/added/deleted, such
   that the computed change percentage is clearly below the rebuild
   threshold → should still report "up to date."
3. **At or just above threshold**: enough files changed to cross the
   threshold → should report "changed" / "needs rebuild."
4. **New files only** (this is the specific case Step 01 fixed): zero
   files deleted or modified, but enough files newly added to cross the
   threshold → should report "changed" / "needs rebuild." This test
   specifically guards against the exact bug described in the original
   review and fixed in Step 01 — if Step 01's fix were ever accidentally
   reverted, this test should fail.
5. **Deleted files only**: symmetrically, confirm deletions alone (with no
   additions or modifications) are still correctly detected, since the
   review states this part already worked — this test guards against a
   future regression of the part that wasn't broken.

For each test, you'll need to construct a manifest object and a
"current files" input with controlled, specific file paths and hashes (or
whatever the actual method's input types are — confirm exact parameter
types by reading `ValidateManifest`'s signature) rather than reading from
real files on disk, so the test is fast, deterministic, and doesn't depend
on filesystem state. If the method's current signature makes this awkward
(e.g. it takes a folder path and scans it directly, rather than taking
already-computed file/hash collections as parameters), consider whether a
small, behavior-preserving signature change (extracting an overload that
takes the data directly, while keeping a folder-path-taking method that
calls into it) would make this testable without changing real behavior. If
making it testable requires anything more invasive than that, stop and
report rather than reshaping the method extensively just to fit a test.

## Constraints

- Do not change the actual threshold value.
- Do not change `ValidateManifest`'s core logic in this step — Step 01 is
  where that's already been fixed; this step is purely about adding test
  coverage for it (with the possible minor testability-enabling refactor
  described above, if needed, and if it can be done without changing
  behavior).

## Verification

1. `dotnet build`.
2. `dotnet test` — all five new tests (or however many you ended up
   writing) should pass against the current (Step-01-fixed) implementation.
3. As a sanity check on the tests themselves: if you temporarily revert
   Step 01's fix locally (just to check, then re-apply it — don't leave it
   reverted), the "new files only" test should fail against the unfixed
   code and pass against the fixed code. This confirms the test actually
   catches the bug it's meant to catch, rather than passing regardless of
   the fix. Make sure to re-apply Step 01's fix afterward and confirm the
   build/tests are clean before finishing this step.

## Done when

- Tests exist covering: no change, below-threshold change, above-threshold
  change, new-files-only above threshold, and deleted-files-only above
  threshold.
- All tests pass against the current, fixed implementation.
- You've confirmed (per the sanity check above) that at least the
  new-files-only test would fail against the pre-Step-01 buggy logic.
