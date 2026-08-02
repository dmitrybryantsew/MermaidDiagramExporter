# Step 04 — Fix `FocusNavigator` / `FocusedSubgraphBuilder` mismatch

## Problem (in plain language)

There are two different pieces of code that both decide "which nodes should
be visible when the user is focused on node X":

1. `FocusNavigator.GetVisibleNodeIds` — only returns nodes that are *exactly
   one edge away* from the focused node (direct neighbors only).
2. `FocusedSubgraphBuilder` — supports a mode called
   `GraphFocusTraversalMode.AllVisibleRelations`, which can walk out to
   depth N (more than one hop), depending on settings.

This means: if a user has configured depth-N focus traversal, the actual
focused subgraph (built by `FocusedSubgraphBuilder`) will contain nodes 2+
hops away — but `FocusNavigator.GetVisibleNodeIds` will only agree on the
1-hop nodes. Whatever in the canvas/UI relies on `GetVisibleNodeIds` to
decide what's visible will disagree with what's actually in the focused
subgraph. Nodes that should be visible (per the user's depth setting) may be
hidden, or vice versa.

## What to find

- `grep -rn "GetVisibleNodeIds" --include=*.cs .`
- `grep -rn "class FocusNavigator" --include=*.cs .`
- `grep -rn "class FocusedSubgraphBuilder" --include=*.cs .`
- `grep -rn "AllVisibleRelations" --include=*.cs .`
- `grep -rn "GraphFocusTraversalMode" --include=*.cs .`

Read all four things before changing anything:
1. The full body of `FocusNavigator.GetVisibleNodeIds`.
2. The full body of whatever method on `FocusedSubgraphBuilder` actually
   builds the subgraph (search for a `Build` method or similar — the exact
   name isn't given in the review, find it).
3. Every call site of `GetVisibleNodeIds` (`grep -rn "GetVisibleNodeIds"`)
   — you need to know who consumes this list and what they do with it,
   since the fix changes what gets returned.
4. Every call site that constructs or uses `FocusedSubgraphBuilder` for the
   currently-focused node, so you can find the actual focused-subgraph
   object that already exists at the time `GetVisibleNodeIds` is called.

## The fix

The review's own suggestion is the right direction: **the canvas (or
whatever consumes `GetVisibleNodeIds`) should use the focused graph's actual
node list directly, instead of recomputing a separate 1-hop-only
approximation.**

Concretely:
1. Determine whether, at the point `GetVisibleNodeIds` is called, there is
   already a computed `FocusedSubgraphBuilder` result (or a `TypeGraph` /
   node-list it produced) available somewhere nearby — e.g. cached on
   `MainWindow`, on the canvas, or recomputed fresh each call. Trace the
   actual call chain; do not assume.
2. If a focused-subgraph result is already available at that point, change
   `GetVisibleNodeIds` (or its caller, whichever is more natural given what
   you traced) to return node IDs from that result's node list, instead of
   independently walking 1-hop neighbors.
3. If no such result is conveniently available at that call site, the
   simplest correct fix is to change `FocusNavigator.GetVisibleNodeIds` to
   internally call into `FocusedSubgraphBuilder` (using the same focus
   traversal mode and depth that the rest of the app is currently
   configured with) and return its node ID set, rather than implementing a
   second, simpler, and now-inconsistent traversal itself.
4. Whichever approach you take, the end state should be: there is exactly
   one piece of logic that decides "what counts as visible when focused on
   node X," and both the navigator and the subgraph builder agree with it
   (likely by the navigator simply delegating to the builder).

## Constraints

- Do not change `GraphFocusTraversalMode` or its enum values.
- Do not change the default traversal depth or mode.
- Be careful with performance: if `FocusedSubgraphBuilder`'s build method is
  expensive and was previously not being called by `GetVisibleNodeIds` at
  all, calling it on every navigation might introduce redundant work if it's
  already being called elsewhere in the same frame/operation. Check whether
  the result can be reused/cached rather than recomputed, but do not
  over-engineer a cache if a straightforward call is fast enough — only add
  caching if you find evidence (e.g. existing caching patterns elsewhere in
  the codebase, or an obvious O(n²) risk) that it's needed.

## Verification

1. `dotnet build`.
2. `dotnet test` — specifically look for and run tests with "Focus" in the
   name (`grep -rln "Focus" --include=*.cs . | grep -i test`). The review
   states "FocusedSubgraphBuilder tests cover depth, directionality, and
   multi-seed" — these must still pass.
3. If there is no existing test that exercises `GetVisibleNodeIds` together
   with a depth > 1 traversal mode, consider this a known gap — note it, but
   do not feel obligated to write a new test in this step unless it's quick;
   the testing gaps are formally addressed in steps 19–21.

## Done when

- `FocusNavigator.GetVisibleNodeIds` and `FocusedSubgraphBuilder` (at
  whatever depth/mode is configured) report the same set of visible nodes
  for a given focus.
- Build and existing tests pass.
