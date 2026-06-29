# Step 08 — Validate folder input at the UI boundary (path traversal)

## Problem (in plain language)

The user picks a folder to scan (via a text box, likely `FolderTextBox` per
the review). That raw text is passed down into `RoslynTypeScanner`, which
eventually calls `Path.GetFullPath` (or similar) to resolve it before
calling `Directory.EnumerateFiles`. Because the path isn't validated at the
point of user input, a crafted path containing `..` segments could resolve
to a directory the user didn't intend to scan (e.g. outside the intended
project folder, or even system directories). `Path.GetFullPath` will
"normalize" such a path rather than reject it, so today there is no
boundary check at all — only an implicit resolution deep inside the scanner.

Note: this app scans the user's own filesystem with the user's own
permissions — this is not a remote/network-facing input. The risk here is
lower severity than a typical server-side path traversal (it's closer to
"the user can accidentally or deliberately point the tool somewhere
unintended," not "an attacker gains access they shouldn't have"), but it's
still worth fixing because: (a) it's good practice for any tool that resolves
user-provided paths, and (b) it gives clearer error messages instead of
silently scanning the wrong place.

## What to find

- `grep -rn "FolderTextBox" --include=*.cs . --include=*.axaml .`
- `grep -rn "class RoslynTypeScanner" --include=*.cs .`
- Inside `RoslynTypeScanner`, find where the incoming folder path is first
  used — search for `Path.GetFullPath`, `Directory.EnumerateFiles`,
  `Directory.Exists`, or whatever the constructor/entry method does first
  with the path argument.
- Find where `FolderTextBox.Text` is read and handed off to start a scan
  (likely a "Scan" button click handler in `MainWindow.axaml.cs` — search
  `grep -n "FolderTextBox.Text" **/*.cs`).

## The fix

Add explicit validation **at the UI boundary** — i.e. at the point where the
text box's raw string is about to be used to start a scan — before it's
ever handed to `RoslynTypeScanner`. This is a defense-in-depth addition, not
a replacement for whatever `RoslynTypeScanner` already does internally.

1. At the scan-trigger call site (the button click handler or equivalent),
   before calling into the scanner:
   ```csharp
   string rawPath = FolderTextBox.Text ?? string.Empty;

   if (string.IsNullOrWhiteSpace(rawPath))
   {
       // existing/new user-facing error: "Please select a folder."
       return;
   }

   string fullPath;
   try
   {
       fullPath = Path.GetFullPath(rawPath);
   }
   catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
   {
       // show user-facing error: "The folder path is not valid."
       return;
   }

   if (!Directory.Exists(fullPath))
   {
       // show user-facing error: "The selected folder does not exist."
       return;
   }

   // proceed to pass `fullPath` (the normalized, validated path) into the scanner,
   // not the original raw, unvalidated `rawPath`.
   ```
2. The key behavioral change: always resolve to a full path **once, at the
   boundary**, check it actually exists, and pass the resolved, confirmed
   path onward — rather than passing the raw text box string deep into
   `RoslynTypeScanner` and letting it resolve/normalize implicitly with no
   existence check beforehand.
3. Do **not** attempt to block `..` segments by string-matching for `..`
   in the raw input — that approach is notoriously easy to get wrong (e.g.
   it misses encoded variants, alternate separators, etc.) and isn't
   actually the right fix here anyway, since `Path.GetFullPath` + `Directory.Exists`
   already gives a normalized, confirmed-real path. The fix is "validate and
   confirm existence before use," not "blacklist a substring."
4. Check whether `RoslynTypeScanner`'s constructor or entry method already
   does its own `Directory.Exists` check internally. If it does, you don't
   need to duplicate that specific check, but you should still add the
   `Path.GetFullPath` + early validation at the UI layer so invalid input is
   caught with a clear message immediately, rather than surfacing as a deep
   exception from inside the scanner during a background scan operation.

## Constraints

- Do not change `RoslynTypeScanner`'s existing internal behavior unless you
  find it has no validation at all and crashes ungracefully on a bad path —
  in that case, a try/catch around its entry point with a user-facing
  message is reasonable, but the primary fix belongs at the UI boundary as
  described above.
- Do not restrict what folders a user is *allowed* to scan beyond
  "must exist" — this tool's job is to scan wherever the user points it; the
  fix is about validating and resolving the path safely, not about adding
  an allowlist of permitted directories.

## Verification

1. `dotnet build`.
2. Manually trace: an empty string, a non-existent path, and a valid path
   each produce the expected outcome by reading the code (empty → early
   error, non-existent → error, valid → proceeds to scan with the resolved
   full path).

## Done when

- The folder path is resolved with `Path.GetFullPath` and checked with
  `Directory.Exists` at the UI boundary before any scan begins.
- Invalid/non-existent paths produce a clear user-facing message instead of
  an unhandled exception or a silent scan of the wrong directory.
- Build passes.
