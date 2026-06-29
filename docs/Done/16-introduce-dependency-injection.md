# Step 16 — Introduce dependency injection (incrementally, not all at once)

## Problem (in plain language)

Throughout the codebase, classes like `RoslynTypeScanner`, `LayoutEngine`,
and `SettingsService` are constructed directly with `new` wherever they're
needed (`new RoslynTypeScanner()`, `new LayoutEngine()`, etc.), rather than
being provided to the classes that use them (e.g. via constructor
parameters). This makes it impossible to substitute a fake/mock version of
these dependencies in a unit test of, say, `MainWindow` — since
`MainWindow`'s constructor hardcodes exactly which concrete implementation
it creates, a test has no way to intercept that.

## Why this step is high risk and how to manage that

This is the riskiest step in the entire plan because it touches
**construction call sites throughout the application**, and a mistake here
(e.g. passing the wrong shared instance to two places that were supposed to
get the same instance, or vice versa) can introduce subtle bugs that don't
show up as build errors — the code will compile and often even run, but
behave incorrectly (e.g. two different `SettingsService` instances writing
to the same file unaware of each other's cache).

**Because of this, do NOT attempt a full DI container rollout in one pass.**
Instead, do this incrementally, one dependency at a time, fully verifying
each before moving to the next.

## Scope for this step

Introduce **constructor-parameter-based** dependency injection (no DI
container/framework needed — plain "pass it into the constructor" is
sufficient and is the right level of complexity for this codebase's size).
Do **not** pull in a DI framework package (e.g. Microsoft.Extensions.
DependencyInjection) unless one is already referenced somewhere in the
project — check first: `grep -rn "Microsoft.Extensions.DependencyInjection" **/*.csproj`.
If it's already there, you may use it; if not, plain constructor injection
without a container is the appropriate scope for this step — do not add a
new package dependency just for this.

## What to find, one dependency at a time

Work through these in this exact order — each is a fully separate sub-step,
verify the build after each before moving to the next:

### 16a. `SettingsService`
1. `grep -rn "new SettingsService()" --include=*.cs .` — find every
   construction site.
2. Find `SettingsService`'s class declaration and constructor.
3. Find every class that currently does `new SettingsService()` internally
   — likely `MainWindow`, possibly `SettingsWindow`.
4. For each such class: add a constructor parameter of type
   `SettingsService` (or whatever its actual type/interface is — check if
   an interface like `ISettingsService` already exists; if not, don't
   invent one yet, just take the concrete type as a parameter — introducing
   an interface is a separate, optional improvement, not required for this
   step).
5. At the single top-level place where the application starts up (likely
   `App.axaml.cs`'s `OnFrameworkInitializationCompleted` or wherever
   `MainWindow` is first constructed), create **one** `SettingsService`
   instance and pass it down into `MainWindow`'s new constructor parameter.
6. If `MainWindow` previously created its own `SettingsService` and also
   passed (or expected child windows like `SettingsWindow` to create their
   own separate instance), make sure both ends now share the **same**
   instance, passed down from `MainWindow` to `SettingsWindow` via
   `SettingsWindow`'s own new constructor parameter — do not let
   `SettingsWindow` construct its own independent `SettingsService` if the
   intent is for both to operate on shared, consistent settings state.
7. `dotnet build`. `dotnet test`. Manually trace: confirm there is now
   exactly one `SettingsService` instance alive during normal app
   operation, created once at startup, threaded through every class that
   needs it.

### 16b. `LayoutEngine`
Repeat the same process as 16a, but for `LayoutEngine`. Pay attention to
whether `LayoutEngine` is expected to be a single shared long-lived
instance (likely, if it holds caches like `LayoutMeasurementService`'s
content-hash cache mentioned in the review) or whether a fresh instance per
scan/layout-run is actually intended (read its current usage carefully —
if it's currently constructed fresh every time a layout runs, that might be
intentional and you should preserve that lifecycle, just make the
*construction* injectable rather than hardcoded, e.g. inject an
`ILayoutEngineFactory` or simply inject the constructor dependencies
`LayoutEngine` itself needs, rather than forcing it into a singleton
lifecycle it wasn't designed for).

### 16c. `RoslynTypeScanner`
Repeat again for `RoslynTypeScanner`. This one is likely constructed fresh
per-scan (scanning a specific folder) rather than being a long-lived shared
instance — if so, the right pattern is usually to inject a **factory**
(e.g. a simple delegate `Func<string, RoslynTypeScanner>` or a small
factory class) rather than a single shared instance, since each scan needs
its own scanner configured for that scan's folder path. Use judgment based
on the actual constructor signature of `RoslynTypeScanner` — if it takes a
folder path as a constructor argument, a shared singleton instance doesn't
make sense; a factory does.

## Constraints

- Do not change the internal logic of `SettingsService`, `LayoutEngine`, or
  `RoslynTypeScanner` themselves in this step — only how they are
  *constructed and supplied* to their consumers.
- Do not introduce interfaces (`ISettingsService`, etc.) unless you find
  this is trivially easy and clearly matches an existing convention
  elsewhere in the codebase. Concrete-type constructor injection is a
  legitimate, sufficient improvement on its own and is lower-risk than also
  introducing new interface abstractions in the same pass. If you do decide
  interfaces are warranted, treat that as worth flagging in your summary as
  a deliberate scope expansion, not something to do silently.
- After this step, `MainWindow`'s constructor should accept its
  dependencies as parameters rather than constructing them itself — but its
  *default, parameterless* usage from `App.axaml.cs` (or wherever it's
  normally instantiated for real application use) must still work exactly
  as before from the end user's perspective. If Avalonia's XAML-based
  window instantiation makes a fully parameterized constructor awkward
  (e.g. if `MainWindow` is instantiated by the XAML framework itself in a
  way that doesn't easily support custom constructor arguments), check how
  the project currently handles `MainWindow` instantiation before assuming
  constructor injection is straightforward — Avalonia/WPF-style frameworks
  sometimes need a parameterless constructor for the designer/XAML loader,
  with a separate property-setter or `Initialize` method used for
  dependency wiring instead. Confirm which pattern fits before implementing.

## Verification (after each of 16a, 16b, 16c)

1. `dotnet build`.
2. `dotnet test`.
3. If feasible, actually run the application (if this environment supports
   running an Avalonia desktop app, which it may not) and confirm normal
   operation: scan a folder, change a setting, confirm it's saved. If you
   cannot run the GUI in this environment, state this limitation explicitly
   and rely on careful code tracing instead.

## Done when

- `SettingsService`, `LayoutEngine`, and `RoslynTypeScanner` (or its
  factory) are supplied to their consumers via constructor parameters
  rather than constructed ad-hoc with bare `new` calls scattered through
  consumer classes.
- Exactly one shared `SettingsService` instance exists during normal
  operation (unless you found evidence the original design intentionally
  wanted otherwise — if so, document that finding rather than silently
  preserving a bug).
- Build and tests pass after each of the three sub-steps.
