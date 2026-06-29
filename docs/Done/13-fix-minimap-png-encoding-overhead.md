# Step 13 — Remove unnecessary PNG encoding in the minimap

## Problem (in plain language)

The minimap (`MinimapControl`) redraws every time the graph changes. Its
current implementation: render to an `SKBitmap`, encode that bitmap to PNG
bytes (`bitmap.Encode(SKEncodedImageFormat.Png, 100)`), write those PNG
bytes into a `MemoryStream`, then construct an Avalonia
`Avalonia.Media.Imaging.Bitmap` by decoding that PNG stream back into an
image. This is encode-then-immediately-decode for no benefit — PNG encoding
is relatively expensive (especially at quality 100), and there's no need to
go through a compressed byte format at all when the data is about to be
displayed in the same process that just rendered it.

## What to find

- `grep -rn "class MinimapControl" --include=*.cs .`
- Inside it, find the render method (review calls it `RenderMinimap()` —
  confirm actual name).
- Find the exact chain: `SKBitmap` creation → `.Encode(SKEncodedImageFormat.Png, ...)`
  → `MemoryStream` → `new Avalonia.Media.Imaging.Bitmap(stream)` (or
  whatever Avalonia bitmap constructor is actually used — confirm by
  reading the code, names may differ slightly, e.g. it might go through
  `WriteableBitmap` instead).

## The fix

The goal: go from `SKBitmap` to something Avalonia can display directly,
without an intermediate PNG round-trip. There are two reasonable approaches
— pick based on what's simplest given the actual Avalonia version and APIs
available in this project (check the `.csproj` for the Avalonia package
version: `grep -n "Avalonia" **/*.csproj`).

### Approach A — Use Avalonia's `WriteableBitmap` directly

If `WriteableBitmap` is already used elsewhere in the codebase (the review
mentions the main `GraphCanvas` already uses a `WriteableBitmap` approach),
this is the most consistent fix: render directly into a
`WriteableBitmap`'s pixel buffer instead of into an `SKBitmap` that then
gets re-encoded.

1. Find how the main canvas's existing `WriteableBitmap` usage works (search
   `grep -rn "WriteableBitmap" --include=*.cs .` across the whole project,
   not just the minimap) — there is likely already a working pattern for
   "get an `SKCanvas` backed directly by an Avalonia `WriteableBitmap`'s
   pixel buffer, draw into it with Skia calls, done — no encode/decode."
2. Apply the same pattern to `MinimapControl`: create a `WriteableBitmap`
   sized for the minimap, lock its buffer, wrap it in an `SKSurface` (via
   `SKSurface.Create` with an `SKImageInfo` matching the bitmap's pixel
   format and a pointer to the locked buffer — this is exactly what the main
   canvas's existing code already does, copy that approach), draw the
   minimap content using the same Skia draw calls (rendering simplified
   node rectangles/dots and the viewport indicator) directly into that
   surface's canvas, unlock, and use the `WriteableBitmap` directly as the
   `Image` control's source.
3. Remove the `SKBitmap` → PNG encode → `MemoryStream` → decode chain
   entirely.

### Approach B — `SKImage` to Avalonia bitmap without PNG (if `WriteableBitmap` pattern isn't easily reusable)

If for some reason the `WriteableBitmap`-direct approach doesn't fit
cleanly (e.g. minimap rendering is structured very differently from the
main canvas), a lighter intermediate fix: use `SKBitmap.Bytes` /
`SKBitmap.GetPixels()` to get the raw pixel buffer directly, and construct
the Avalonia bitmap from raw pixels using whatever raw-pixel-bitmap
constructor Avalonia's version offers (check Avalonia's
`Avalonia.Media.Imaging.Bitmap` constructor overloads — some versions
support constructing directly from a pixel pointer + `PixelFormat` +
`PixelSize`, with no PNG involved at all). This avoids the encode/decode
round-trip even without unifying with the main canvas's exact pattern.

**Prefer Approach A** if at all reasonably achievable, since it reuses an
already-proven pattern in the same codebase rather than introducing a
second, different way of bridging Skia and Avalonia bitmaps.

## Constraints

- Do not change what the minimap actually displays (node positions, colors,
  viewport rectangle) — only how the rendered pixels get from Skia's buffer
  into something Avalonia can show on screen.
- Do not change how often the minimap re-renders (that's a separate
  question from how each render is encoded) — this step is purely about
  removing the PNG round-trip cost per render, not about reducing render
  frequency.
- Double check pixel format compatibility (e.g. `SKColorType.Bgra8888` vs
  whatever Avalonia expects) — this is the most common source of bugs when
  bypassing an encode/decode step, since the encode/decode was incidentally
  also handling any format conversion needed. Confirm the chosen Skia pixel
  format matches what Avalonia's bitmap/pixel-buffer constructor expects, or
  colors will come out wrong (e.g. red/blue channels swapped).

## Verification

1. `dotnet build`.
2. Manual code-read confirming the PNG encode/decode chain no longer exists
   in `MinimapControl`.
3. If you can run the GUI, visually confirm the minimap still displays
   correctly (correct colors, correct node positions, viewport rectangle in
   the right place) after a graph change. Pay special attention to color
   correctness given the pixel-format note above. If you cannot run the
   GUI, state this limitation explicitly.

## Done when

- The minimap no longer encodes to PNG and decodes back on every render.
- Visual output (colors, positions) is unchanged.
- Build passes.
