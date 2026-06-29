# Step 02 — Fix hardcoded padding in `RecalculateClusterBounds`

## Problem (in plain language)

When a namespace cluster's visual boundary box is recalculated (e.g. after a
manual drag), the method that does this calculation uses hardcoded literal
numbers for padding and title-bar height instead of reading the same values
from the shared layout options object. If a user changes those options
elsewhere (e.g. in a settings screen, or a different code path that does
read from `LayoutOptions`), the cluster boxes drawn by this method will be
visually inconsistent with the rest of the layout — wrong padding, wrong
title height — because this one method didn't get the memo.

## What to find

- Class name: `ManualLayoutApplier`
- Method name: `RecalculateClusterBounds`
- Search:
  ```
  grep -rn "RecalculateClusterBounds" --include=*.cs .
  grep -rln "ManualLayoutApplier" --include=*.cs .
  ```
- Inside the method, look for local variables or literals that look like:
  ```csharp
  float padding = 24;
  float titleHeight = 24;
  ```
  (The exact literal values and variable names may differ slightly from this
  — confirm what's actually there. The review states these are both `24` in
  the current code, but verify.)

## What to find (the source of truth)

- Look for a class or type named `LayoutOptions` (search:
  `grep -rn "class LayoutOptions" --include=*.cs .`).
- Inside it, find properties that represent the same two concepts: cluster
  padding and cluster title-bar height. They may be named differently than
  the local variables in `RecalculateClusterBounds` — e.g.
  `ClusterPadding`, `NamespacePadding`, `ClusterTitleHeight`,
  `TitleBarHeight`. Read the whole `LayoutOptions` class to find the actual
  matching property names.
- Confirm that other layout passes (the ones doing the *initial* layout, not
  manual override) already read padding/title-height from this same
  `LayoutOptions` instance — that's how you confirm these are really meant
  to be the same value, not a coincidence. Search for where the initial
  layout pass draws/measures cluster bounds and see what it reads from.

## The fix

1. Find how `RecalculateClusterBounds` gets access to layout configuration.
   It likely already has an instance of `LayoutOptions` available — either
   as a constructor-injected field, a method parameter, or accessible via
   some shared context object passed into `ManualLayoutApplier`. If it does
   not currently have access to `LayoutOptions` at all, you will need to add
   it as a constructor parameter (and update every call site that constructs
   `ManualLayoutApplier` — search `new ManualLayoutApplier(` to find them
   all).
2. Replace the hardcoded literals with reads from the actual `LayoutOptions`
   properties you identified above, e.g. (using whatever the real property
   names are):
   ```csharp
   float padding = layoutOptions.ClusterPadding;
   float titleHeight = layoutOptions.ClusterTitleHeight;
   ```
3. If you had to add a new constructor parameter, make sure every existing
   call site that constructs this class is updated to pass the right
   `LayoutOptions` instance (it's likely already floating around in whatever
   code constructs `ManualLayoutApplier` — check before assuming you need to
   create a new one).

## Constraints

- Do not change the numeric values themselves unless `LayoutOptions`'
  current default differs from `24` — in that case, that's expected; the
  whole point is to stop hardcoding and start reading the configured value,
  even if today's configured default happens to also be `24`.
- Do not add new fields to `LayoutOptions` — the properties you need almost
  certainly already exist, since other layout code reads cluster
  padding/title height from somewhere. If you search thoroughly and truly
  cannot find equivalent properties, stop and report this rather than
  inventing new ones — it may mean the concept doesn't exist yet and needs a
  design decision above your scope.

## Verification

1. `dotnet build`.
2. `dotnet test` — pay particular attention to any test with "Cluster" or
   "ManualLayout" in its name.
3. Manually trace: pick one call site of `RecalculateClusterBounds` and
   confirm the value it now reads matches what the rest of the layout engine
   uses for the same constants.

## Done when

- No literal `24` (or whatever the original hardcoded values were) remains
  in `RecalculateClusterBounds` for padding/title height.
- The values now come from `LayoutOptions`.
- Build and tests pass.
