# Step 07 — Prevent regex denial-of-service in `CustomStereotypeEngine`

## Problem (in plain language)

Users can type arbitrary regex patterns for custom stereotypes. Some regex
patterns (a classic example: `(a+)+` matched against a long string with no
match at the end) can take exponential time to evaluate due to
"catastrophic backtracking." If a user (even accidentally, not maliciously)
types such a pattern, and it's evaluated against many type names during a
scan, the app could hang or become unresponsive for a very long time. There
is currently no protection against this.

## What to find

- Same class as step 06: `CustomStereotypeEngine`.
- Find every place a `Regex` object is constructed from user-supplied input
  in this class. Search: `grep -n "new Regex" <path-to-file>` once you've
  located it via `grep -rln "class CustomStereotypeEngine" --include=*.cs .`.
- Note the current `RegexOptions` passed in — the review states
  `RegexOptions.Compiled` is currently used.
- Check the project's target framework: `grep -n "<TargetFramework" **/*.csproj`
  (run from repo root, or just `find . -name "*.csproj" -exec cat {} \;` and
  read the `<TargetFramework>` value). This matters because
  `RegexOptions.NonBacktracking` requires **.NET 7 or later**, and the timeout
  constructor requires only `Regex` itself (works on any version).

## The fix

You have two complementary tools available. Apply both if the framework
version allows; apply only the timeout approach if stuck on an older
framework.

### Option A — Regex timeout (works on any .NET version, do this regardless)

`Regex` has a constructor overload that accepts a `TimeSpan` timeout:

```csharp
new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(500))
```

When a match takes longer than the timeout, `Regex` throws
`RegexMatchTimeoutException`. You must wrap actual match calls (not just
construction) in a try/catch for this exception, since the timeout fires
during `.IsMatch()` / `.Match()` calls, not during construction.

Find every place this engine actually **runs** a compiled pattern against a
type name (likely a `.IsMatch(typeName)` call inside whatever method applies
stereotypes during a scan) and wrap it:

```csharp
try
{
    if (compiledRegex.IsMatch(typeName))
    {
        // existing match-handling logic, unchanged
    }
}
catch (RegexMatchTimeoutException)
{
    // Treat as "did not match" — log or skip, following whatever the
    // existing pattern is for the already-existing invalid-pattern
    // catch block from step 06. Do not let this exception propagate
    // and crash a scan over one bad pattern.
    continue; // or equivalent, matching the surrounding loop structure
}
```

### Option B — `RegexOptions.NonBacktracking` (.NET 7+ only)

If the `.csproj` confirms target framework is `net7.0` or later, you can
additionally pass `RegexOptions.NonBacktracking` at construction time. This
mode mathematically cannot exhibit catastrophic backtracking (it uses a
different matching engine internally), so it's a stronger fix than the
timeout alone — the timeout is a safety net, NonBacktracking removes the
underlying risk.

**Important constraint**: `RegexOptions.NonBacktracking` is **incompatible**
with some other regex features (e.g. backreferences, some lookaround). If
the codebase relies on stereotype patterns using those features, switching
to `NonBacktracking` could break valid existing patterns. Check whether
there are any example/default/built-in stereotype patterns shipped with the
app (search `grep -rn "Stereotype" --include=*.json .` or similar config)
and verify none of them use backreferences (`\1`, `\2`, etc.) or
lookaround (`(?=`, `(?<=`, etc.) before applying this option. If you find
any, do not apply `NonBacktracking` — rely on Option A's timeout alone and
note this limitation in a code comment.

If target framework is older than net7.0, skip Option B entirely and rely
on Option A.

## Constraints

- Do not remove `RegexOptions.Compiled` — keep it alongside whichever new
  option(s) you add (they're combinable with a bitwise OR, e.g.
  `RegexOptions.Compiled | RegexOptions.NonBacktracking`).
- Do not change the public API surface of `CustomStereotypeEngine` if it can
  be avoided — this should be an internal hardening change.
- Pick a timeout value that's generous enough not to false-positive on
  legitimate complex-but-valid patterns against long type names, but short
  enough to not visibly hang the UI. 500ms per pattern-match call is a
  reasonable starting point; do not pick something under 100ms (too easy to
  false-positive) or over 2000ms (defeats the purpose during a scan with
  many type names).

## Verification

1. `dotnet build`.
2. `dotnet test`.
3. If feasible, write a quick throwaway test (not part of the permanent
   suite, just to confirm locally) using a known catastrophic pattern like
   `(a+)+$` against a long string of `a` characters followed by a
   non-matching character, and confirm it now throws/handles
   `RegexMatchTimeoutException` within roughly the timeout window instead of
   hanging indefinitely. Delete the throwaway test after confirming, unless
   you think it's worth keeping — if so, that's fine, just note it.

## Done when

- All user-pattern regex evaluation calls are protected by a timeout.
- `RegexMatchTimeoutException` is caught and treated as a non-match, not an
  unhandled crash.
- `NonBacktracking` is applied if and only if the framework version supports
  it and no shipped pattern relies on backreferences/lookaround.
- Build and tests pass.
