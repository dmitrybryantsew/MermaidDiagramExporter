# Step 09 — Add file size limits in `SourceBundleService`

## Problem (in plain language)

Whatever code reads source files into memory for bundling/export
(`SourceBundleService`, per the review) currently reads every `.cs` file in
full, with no size limit. If a project folder contains a generated file
that's multiple gigabytes (this does happen in real codebases — e.g.
generated parser tables, embedded data, minified bundles accidentally given
a `.cs` extension), reading it fully into memory could exhaust available
memory and crash the application.

## What to find

- `grep -rn "class SourceBundleService" --include=*.cs .`
- Inside it, find where files are actually read — search for
  `File.ReadAllText`, `File.ReadAllBytes`, `StreamReader`, or similar.
- Find whether file paths reaching this point have already been filtered by
  extension (likely yes, only `.cs` files) — confirm where that filtering
  happens, since the size check should be added at or before the same point
  files are first opened for reading, not after they're already loaded.

## The fix

1. Decide on a sane maximum file size. A reasonable default for legitimate
   hand-written or even generated C# source is well under 50 MB — pick
   **10 MB** as a starting default unless you find evidence elsewhere in the
   codebase (config, comments, existing constants) suggesting a different
   value is expected. Make this a named constant, not a bare literal, e.g.:
   ```csharp
   private const long MaxSourceFileSizeBytes = 10 * 1024 * 1024; // 10 MB
   ```
2. Before reading each file's full contents, check its size first using
   `FileInfo`, which does not require opening/reading the file:
   ```csharp
   var fileInfo = new FileInfo(filePath);
   if (fileInfo.Length > MaxSourceFileSizeBytes)
   {
       // Skip this file. Do not throw and abort the whole bundle/scan —
       // one oversized file should not stop processing of everything else.
       // Follow whatever pattern the surrounding code already uses for
       // "skip this file and continue" (there is likely already such a
       // pattern for files that fail to parse, or are excluded by some
       // other filter — reuse that convention, e.g. a warnings/skipped-files
       // list that gets reported to the user, if one exists).
       continue;
   }
   ```
3. Check whether there's a reasonable way to surface "N files were skipped
   because they exceeded the size limit" to the user (e.g. if there's
   already a results/summary object that gets shown after a scan — search
   for where parse errors or excluded files are currently reported, if at
   all, and add to that same mechanism). If no such reporting mechanism
   exists anywhere in the codebase, it's acceptable to just log the skip
   (if there's an existing logging mechanism) without building a whole new
   UI feature for this step — don't over-build a new reporting pipeline just
   for this; flag it as a possible future improvement instead.

## Constraints

- Do not change behavior for files under the size limit — they should be
  read exactly as before.
- Do not make the limit configurable in this step unless a settings UI
  mechanism is trivially already there for similar options (e.g. if
  `LayoutOptions`-style settings classes are easy to extend and there's an
  obvious existing settings screen field to add it to). If adding
  configurability would require non-trivial new UI work, skip it — a fixed,
  sane default is sufficient for this step.
- Make sure this check applies wherever `SourceBundleService` reads files,
  not just in one of multiple methods if there's more than one read path.
  Search thoroughly: `grep -n "ReadAllText\|ReadAllBytes\|StreamReader" <file>`
  inside the actual `SourceBundleService` file to find every read site.

## Verification

1. `dotnet build`.
2. `dotnet test`.
3. If feasible, create a throwaway large dummy file (e.g.
   `dotnet-script`-free approach: `fsutil`/`dd`/`head -c` to generate an
   11MB file with a `.cs` extension in a temp test folder) and confirm the
   bundle service skips it without crashing, while still processing other
   normal-sized files in the same folder. Clean up the dummy file
   afterward.

## Done when

- Files larger than the chosen size threshold are skipped, not read into
  memory.
- The rest of the bundling/scanning process continues normally for all
  other files.
- Build and tests pass.
