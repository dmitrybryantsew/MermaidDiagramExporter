# Step 19 — Add a round-trip persistence test for `ManualLayoutOverrides`

## Problem (in plain language)

`ManualLayoutOverrides` stores user-made manual position adjustments as
deltas (per the review, this is a deliberately good design choice — deltas
survive node additions and layout engine changes better than absolute
positions would). However, there's no existing test that confirms: if you
save a `ManualLayoutOverrides` object to disk (or whatever the persistence
mechanism is) and load it back, you get the same data out that you put in.
Without this test, a future change to the serialization logic could silently
corrupt saved manual layouts with no test failure to catch it.

## What to find

- `grep -rln "class ManualLayoutOverrides" --include=*.cs .`
- Find how it's persisted — search for `Save`, `Load`, `Serialize`,
  `Deserialize` methods on this class or on a related service (it may be
  persisted as part of `ProjectSettings`, mentioned elsewhere in the
  review, or it may have its own dedicated save/load path — confirm which).
- Find the existing test project structure: locate the `Tests` project
  (mentioned as one of the three projects: `Core`, `Gui`, `Tests`) and find
  an existing test file that tests something conceptually similar (e.g. a
  settings round-trip test, if one exists, for `ProjectSettings`) to copy
  its structure/conventions (test framework in use — xUnit? NUnit? — naming
  conventions, how temp files/folders are handled, e.g. the `TempSourceFolder`
  disposable pattern mentioned positively in the review might have an
  analogous "temp settings file" pattern worth reusing or imitating).

## The fix

Write a new test (in the existing test framework/style already used in the
`Tests` project — do not introduce a new testing framework) that:

1. Constructs a `ManualLayoutOverrides` instance with some non-trivial,
   realistic sample data — at minimum: more than one node's position delta,
   and (if `ManualLayoutOverrides` also stores cluster-level manual
   adjustments, per the "Namespace Cluster Drag" feature mentioned in the
   review — confirm whether it does) at least one cluster-level override
   too, so the test actually exercises everything the class is responsible
   for persisting, not just the simplest case.
2. Saves/serializes it using whatever the real persistence mechanism is.
3. Loads/deserializes it back into a new instance.
4. Asserts that the loaded instance's data matches the original exactly —
   compare every field that matters (node IDs and their delta X/Y values,
   cluster IDs and their deltas if applicable). Do not just assert the
   loaded object is "not null" — that would not actually catch a corruption
   bug; assert specific values match.
5. If persistence goes through an actual file on disk (rather than just an
   in-memory serialize/deserialize round-trip), use a temp file/folder that
   gets cleaned up after the test (following whatever disposal pattern,
   e.g. `IDisposable`, the existing `TempSourceFolder`-style tests already
   use) so the test doesn't leave stray files behind or interfere with
   other tests.

## Constraints

- Do not change `ManualLayoutOverrides` or its persistence logic itself in
  this step — this step is purely additive (a new test), unless writing the
  test reveals an actual round-trip bug, in which case: stop, report the
  bug clearly (what data went in, what came out differently), and do not
  attempt to fix the underlying persistence bug as part of this testing
  step — that becomes a new, separate correctness fix to be planned and
  reviewed on its own, since the original review didn't anticipate this
  specific bug and it deserves its own focused attention.

## Verification

1. `dotnet build`.
2. `dotnet test` — confirm the new test passes (or, per the constraint
   above, clearly fails and is reported as a found bug rather than silently
   worked around).

## Done when

- A new round-trip test for `ManualLayoutOverrides` persistence exists, in
  the existing test project, using the existing test framework and
  conventions.
- The test asserts specific field-level equality, not just non-null checks.
- Either the test passes (confirming round-trip correctness), or a genuine
  bug was found and clearly reported for separate follow-up.
