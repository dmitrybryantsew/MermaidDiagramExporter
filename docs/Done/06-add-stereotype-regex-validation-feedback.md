# Step 06 — Surface invalid stereotype regex patterns to the user

## Problem (in plain language)

Users can type custom regex patterns into a settings screen to define
"stereotypes" (custom categorization rules for types, presumably matched
against type names). If the user types an invalid regex (the review's
example: `*[`, which is invalid because `*` at the start of a pattern with
no preceding token is a syntax error), the underlying engine,
`CustomStereotypeEngine`, silently skips it. The user gets no feedback at
all — their rule just silently does nothing, with no error message, no
visual indicator, nothing. This is confusing: the user has no way to know
whether their custom stereotype is broken or just not matching anything.

## What to find

- `grep -rn "class CustomStereotypeEngine" --include=*.cs .`
- Inside it, find where regex patterns are compiled — likely something like
  `new Regex(pattern, RegexOptions.Compiled)` wrapped in a `try/catch` that
  swallows `ArgumentException` (the exception type thrown by `Regex` for
  invalid patterns). Confirm this is really a silent catch — read the catch
  block contents.
- Find the settings UI: `grep -rln "Stereotype" --include=*.axaml .` and
  `grep -rln "Stereotype" --include=*.axaml.cs .` — likely
  `SettingsWindow.axaml` / `SettingsWindow.axaml.cs`, per the review.
- Find where the user's typed pattern is read from a text box and handed off
  to `CustomStereotypeEngine` (or wherever validation could plug in).

## The fix

The goal is: when a user enters an invalid regex pattern, they see a clear,
specific error message near the input, rather than silent failure.

1. In `CustomStereotypeEngine`, find (or add, if none exists) a method whose
   single job is to validate a pattern and report success/failure without
   swallowing the reason. For example:
   ```csharp
   public static bool TryValidatePattern(string pattern, out string? errorMessage)
   {
       try
       {
           _ = new Regex(pattern);
           errorMessage = null;
           return true;
       }
       catch (ArgumentException ex)
       {
           errorMessage = ex.Message;
           return false;
       }
   }
   ```
   Adjust the method's accessibility/placement to match the class's existing
   conventions (e.g. if other validation helpers in this class are instance
   methods, not static, follow that pattern instead).
2. **Do not change the existing silent-catch behavior inside whatever method
   actually compiles patterns for use during a scan** (e.g. if there's a
   `BuildRules` or `Compile` method that's called during the actual graph
   build) — that path should arguably keep skipping bad patterns gracefully
   so a single bad regex doesn't crash an entire scan. The new validation
   method above is for *UI-time* feedback, separate from *scan-time*
   tolerance. These are two different concerns; do not merge them into one
   code path.
3. In the settings UI code-behind, find the event handler that runs when the
   user finishes typing a pattern (e.g. a `TextChanged` handler, or a
   "Save"/"Apply" button handler — find the actual mechanism used for other
   validated fields in the same settings window, and follow that existing
   pattern for consistency rather than inventing a new one).
4. Call `TryValidatePattern` and, if it returns `false`, show the
   `errorMessage` to the user. Check how the rest of the settings window
   displays validation errors for other fields (there may already be a
   `TextBlock` or tooltip convention used elsewhere in the same XAML file —
   reuse that convention rather than introducing a new one).

## Constraints

- Do not change the regex *matching* behavior — only add validation
  feedback.
- Do not silently auto-correct invalid patterns.
- Keep the scan-time tolerant behavior (skip bad patterns without crashing)
  fully intact — this step is additive UI feedback, not a removal of
  existing safety nets.

## Verification

1. `dotnet build`.
2. Manually trace: enter `*[` as a pattern in the settings flow (by reading
   the code path, not necessarily running the GUI) and confirm the new
   validation method would return `false` with a non-null `errorMessage`.
3. Confirm a valid pattern (e.g. `^I[A-Z].*`) still passes validation.

## Done when

- An invalid regex pattern produces a visible, specific error message in
  the settings UI.
- Scan-time tolerance for bad patterns (no crash on a full scan) is
  unchanged.
- Build passes.
