# Step 14 — Extract magic numbers into named constants

## Problem (in plain language)

The codebase has numeric literals scattered through the code with no name
explaining what they mean: `6` (max members shown per node), `10` (arrow
length), `0.01f` (some epsilon, likely for layout convergence), `4` (a
"sweep count," likely related to the crossing-reduction layout pass). When
a number like `6` appears bare in the middle of a method, a future reader
(or a future you) has to reverse-engineer what it represents and why that
value was chosen, and there's no single place to change it if it needs
tuning.

## What to find

For each of the four numbers below, search broadly first, then narrow down
to confirm context, since e.g. `6` and `4` and `10` will match enormous
numbers of unrelated lines — you must read surrounding context for each
match, not just grep-and-replace blindly.

1. **Max members shown (`6`)**: search
   `grep -rn "MaxMembers\|maxMembers\|members.Take(6)\|Take(6)" --include=*.cs .`
   Look for a loop or `.Take(...)` / `.Count > ...` check related to
   limiting how many member names are displayed inside a node's rendered
   box.
2. **Arrow length (`10`)**: search
   `grep -rn "arrowLength\|ArrowLength\|arrow" --include=*.cs .` in
   whatever file draws edge arrowheads (likely inside `GraphCanvas`'s edge
   drawing code).
3. **Epsilon (`0.01f`)**: search `grep -rn "0.01f" --include=*.cs .` — there
   may be more than one occurrence with different meanings; read each one's
   surrounding context (e.g. layout convergence check vs. something else)
   before deciding whether they should become the same named constant or
   separate ones.
4. **Sweep count (`4`)**: search
   `grep -rn "sweepCount\|SweepCount\|sweep" --include=*.cs .` — likely
   inside `CrossingReductionService` or a related layout pass class
   (review mentions crossing-reduction explicitly).

## The fix

For each of the four (and any other clearly-unexplained bare numeric
literal you encounter while doing this — use judgment, but don't go on an
unbounded hunt across the entire codebase; focus on the ones named above
plus anything immediately adjacent to them in the same method):

1. Decide where the constant belongs:
   - If the value is something a user could reasonably want to tune via
     settings (e.g. "max members shown" feels like a legitimate user
     preference), it probably belongs as a property on `LayoutOptions`
     (the same class touched in Step 02) rather than a `const`. Check
     whether `LayoutOptions` already has a slot for it under a different
     name before adding a new one (search `grep -n "Members\|Member" 
     <path-to-LayoutOptions.cs>`).
   - If the value is an internal implementation detail with no plausible
     user-facing tuning need (e.g. a layout convergence epsilon, a fixed
     algorithmic sweep count), a `private const` (or `internal const` if
     tests need to reference it) field near where it's used is more
     appropriate than cluttering `LayoutOptions` with internals users
     shouldn't touch.
   - When in doubt, prefer a local `const` over adding to `LayoutOptions` —
     it's easy to promote a `const` to a configurable option later if
     needed, but adding unnecessary settings surface area is harder to walk
     back once a settings UI starts depending on it.
2. Give each constant a clear, specific name reflecting both *what* it is
   and *why* roughly that value (a one-line comment is fine if the name
   alone can't carry the "why"), e.g.:
   ```csharp
   /// <summary>Maximum number of member names shown inside a node box before truncating with "...".</summary>
   private const int MaxMembersShownPerNode = 6;
   ```
   ```csharp
   /// <summary>Length, in canvas units, of edge arrowheads.</summary>
   private const float ArrowheadLength = 10f;
   ```
   ```csharp
   /// <summary>Convergence threshold below which a layout pass is considered stable.</summary>
   private const float LayoutConvergenceEpsilon = 0.01f;
   ```
   ```csharp
   /// <summary>Number of crossing-reduction sweep iterations performed per layout pass.</summary>
   private const int CrossingReductionSweepCount = 4;
   ```
   Adjust names/values/placement to match what you actually find — these
   are illustrative, not to be pasted verbatim if the real context differs.
3. Replace each bare literal usage with a reference to the new named
   constant.
4. If the same literal value (e.g. `0.01f`) appears in multiple places for
   genuinely different purposes (confirmed by reading context, not assumed),
   do NOT collapse them into one shared constant — give each its own
   appropriately-named constant even if the numeric value happens to
   coincide. Merging unrelated concepts that happen to share a value today
   is a trap: if one needs to change later, the other shouldn't be dragged
   along with it.

## Constraints

- Do not change any of the actual values — this step is purely about
  naming, not retuning behavior.
- Do not touch every single numeric literal in the codebase — scope this to
  the four named in the review (plus closely adjacent ones you notice while
  already looking at that code). A broader magic-number sweep is not what
  this step asks for, and an unbounded search increases risk of unrelated
  changes.

## Verification

1. `dotnet build`.
2. `dotnet test` — these are pure renames with no logic change, so all
   existing tests should pass unchanged. If any test fails, you likely
   introduced an unintended value change — find and fix it before
   proceeding.

## Done when

- The four specifically-named magic numbers (and clearly related neighbors
  found in the same pass) are now named constants or `LayoutOptions`
  properties with clear names and brief doc comments.
- No behavior changed.
- Build and tests pass.
