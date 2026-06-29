# Step 03 — Use the shared color constant in `GraphCanvas.SaveToPng()`

## Problem (in plain language)

There's a background color used when exporting the canvas to a PNG file.
Instead of reusing the existing shared color constant for the canvas
background, the export method independently parses the same color from a
hex string literal. Today these two values happen to match, so there's no
visible bug — but they are two separate sources of truth for what should be
one value. If someone changes the background color constant later (e.g. to
support theming, see step 17), this PNG export call site will silently keep
using the old color, because nobody told it to use the constant.

## What to find

- Class: `GraphCanvas`
- Method: `SaveToPng` (or similarly named — search for the export-to-PNG
  method if the exact name differs)
- Search:
  ```
  grep -rn "SaveToPng" --include=*.cs .
  grep -rn "1A1E24" --include=*.cs .
  grep -rn "ColorBg" --include=*.cs .
  ```
- You're looking for a line resembling:
  ```csharp
  var bg = SKColor.Parse("#1A1E24");
  ```
  and, elsewhere in the same class (likely near the top, as a field), a
  declaration resembling:
  ```csharp
  private static readonly SKColor ColorBg = new(0x1A, 0x1E, 0x24);
  ```
  Confirm both actually exist and that the hex digits genuinely match before
  editing — the review says they match today, but verify with your own eyes
  since you're the one making the change.

## The fix

Replace the `SKColor.Parse("#1A1E24")` (or whatever the literal actually is)
with a direct reference to the existing field, e.g.:

```csharp
var bg = ColorBg;
```

If `ColorBg` is `private` and `SaveToPng` is in the same class, this just
works with no further changes. If for some reason `SaveToPng` lives in a
different class than the one declaring `ColorBg` (double check this — the
review implies they're the same class), you'll need to either make the field
accessible (e.g. `internal` or a public static property) or pass the color
in as a parameter — prefer making the field `internal static readonly` only
if absolutely necessary, since over-widening visibility is its own minor
smell. Report back if this turns out to be the case rather than guessing.

## Constraints

- Do not change the actual color value.
- Do not introduce a new constant — reuse the one that already exists.
- This should be a one-line (or few-line) change. If you find yourself
  touching more than the single line that constructs the background color
  inside `SaveToPng`, stop and re-read the method — you may be in the wrong
  place.

## Verification

1. `dotnet build`.
2. If there's a visual/export test (search `grep -rln "SaveToPng" --include=*.cs . | grep -i test`),
   run it. If none exists, this is a low-risk enough change that a clean
   build is sufficient confirmation for this step.

## Done when

- `SaveToPng` references the shared `ColorBg` constant instead of
  re-parsing a hex string.
- Build passes.
