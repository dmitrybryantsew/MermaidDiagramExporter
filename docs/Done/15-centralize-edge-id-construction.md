# Step 15 — Centralize edge ID string construction

## Problem (in plain language)

In multiple places across the codebase, an edge's identifier is built by
concatenating strings inline:
```csharp
edge.FromNodeId + "->" + edge.ToNodeId + ":" + edge.Kind
```
Because this exact format is repeated by hand in more than one place, any
small inconsistency (different separator, different order, forgetting the
`Kind` suffix in one spot, different casing/`.ToString()` behavior for
`Kind`) would create two different ID schemes that don't match each other —
silently breaking anything that compares or looks up edges by this string
(e.g. a dictionary keyed by edge ID, or a duplicate-detection check).

## What to find

- Search every occurrence of this concatenation pattern, since the exact
  variable names may differ slightly at each call site:
  ```
  grep -rn '"->"' --include=*.cs .
  grep -rn 'FromNodeId.*ToNodeId\|ToNodeId.*FromNodeId' --include=*.cs .
  ```
- For each match, confirm it's actually building an edge identifier (not
  some unrelated string) by reading the surrounding code.
- Find the `Edge` (or similarly named) class/record that holds
  `FromNodeId`, `ToNodeId`, and `Kind` — search
  `grep -rn "FromNodeId" --include=*.cs .` to find its declaration, and
  confirm the exact property names and the type of `Kind` (an enum?
  confirm its `.ToString()` output is stable and matches what's used in the
  existing concatenations).

## The fix

1. Add a single static factory method, placed on the `Edge` class/record
   itself if you can modify it conveniently (preferred, since it keeps the
   ID logic next to the data it describes), or as a static method on a
   small dedicated helper class (e.g. `EdgeId`) if `Edge` is a `record` with
   a tight, deliberately minimal shape that the project seems to want kept
   that way (use judgment based on how other similar small concerns are
   handled elsewhere in the codebase — if other "ID building" logic for
   other entity types lives in their own small static helper, follow that
   convention).
   ```csharp
   public static string Create(string fromNodeId, string toNodeId, EdgeKind kind)
       => $"{fromNodeId}->{toNodeId}:{kind}";
   ```
   Use the actual type of `Kind` (it may not be called `EdgeKind` — confirm)
   and the actual property names found above. If `Edge` already has an
   existing `Id` property that's computed some other way, read it first —
   you may already have a method to call here rather than needing to write
   a new one, and this step might really be "use the existing one
   everywhere" rather than "create a new one."
2. Replace every inline concatenation found in your search with a call to
   this new (or existing) factory method, passing the actual edge's fields.
3. If any call site doesn't have a full `Edge` object handy (only the raw
   `fromNodeId`, `toNodeId`, `kind` values separately, e.g. before an `Edge`
   object is constructed), make sure your factory method's parameter list
   supports being called with just those raw values (as in the snippet
   above) — don't force every call site to construct a full `Edge` object
   just to get its ID.

## Constraints

- Do not change the actual format of the resulting string (still
  `from->to:kind`) — this step is about having one source of truth for that
  format, not about changing the format itself. Changing the format would
  invalidate anything that persisted edge IDs previously (e.g. in cache
  files, per Step 01's area) and is out of scope here.
- Make sure you don't miss any call site — re-run your search after editing
  to confirm zero remaining inline `"->"` concatenations for edge IDs exist
  (excluding the one canonical implementation inside the new factory
  method itself).

## Verification

1. `dotnet build`.
2. `dotnet test`.
3. Re-run `grep -rn '"->"' --include=*.cs .` after your edits — the only
   remaining match should be inside the new factory method itself.

## Done when

- Exactly one piece of code constructs the `from->to:kind` edge ID string.
- Every other former inline-concatenation call site now calls that single
  method instead.
- Build and tests pass.
