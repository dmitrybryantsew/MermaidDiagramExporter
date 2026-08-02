# 08 — Risks and Decisions

## Open questions deferred to implementation time

These are questions that don't have a clear answer until we start building.
They are documented here so the implementer knows what to watch for.

### Q1: Should Design Mode have its own minimap?

**Status**: Open. **Default assumption**: No. The minimap is useful when you
have many classes spread across a large canvas. In Design Mode, users
typically work with 5–20 classes in a focused area. A minimap adds visual
clutter without much benefit. If user feedback says otherwise, add it in a
later phase.

### Q2: Should the mode toggle be persistent across app restarts?

**Status**: Open. **Default assumption**: Yes — store the last mode in
`ProjectSettings` (or a new `AppSettings`). When the app starts, restore the
last mode. When switching modes, save the choice.

### Q3: Can the user have multiple designs open at once (tabs)?

**Status**: Open. **Default assumption**: No for v1. Single design at a time.
Tabs are a future enhancement. The data model supports multiple designs
(multiple `.dgraph.json` files on disk), but the UI only shows one.

### Q4: Should Design Mode auto-apply layout when adding a class?

**Status**: Open. **Default assumption**: No. New classes are added at the
click position with a default size. The user clicks "Apply Layout" (or
Ctrl+L) when they want the layout engine to reposition everything. This
avoids jarring jumps when adding multiple classes in quick succession.

### Q5: Should the C# stub export include `[Serializable]` attributes?

**Status**: Open. **Default assumption**: No for v1. The stub is a starting
point for the user to fill in. Adding serialization attributes is premature
optimization. If users need it, add it later.

### Q6: How do we handle very large designs (100+ classes)?

**Status**: Open. **Default assumption**: Performance will degrade. Mitigations:
- Acceptable at 50–100 classes with current O(n) hit-testing (both
  `HitTestService` and `DesignHitTestService` are linear scans).
- Virtual scrolling for the class list (future)
- Layout engine is O(N²) for some operations — may need optimization for
  large N (future)
- Spatial pruning for hit-testing (future optimization, not current capability)

For v1, we accept that designs over ~50 classes may feel sluggish.

### Q7: Should we support undo across mode switches?

**Status**: Open. **Default assumption**: No. Undo is scoped to Design Mode
only. Switching modes clears the undo/redo stack. This avoids confusing
"undo a scan" scenarios.

### Q8: Should the export include a "preview" before saving?

**Status**: Open. **Default assumption**: Yes for C# stub (show in a text
viewer with syntax highlighting). No for Mermaid/JSON (the user knows what
they're getting).

## Risks

### R1: Avalonia TextBox focus stealing during inline edit

**Risk**: When entering inline edit mode (double-click class name), the
TextBox must receive focus immediately. If focus is delayed (e.g. due to
visual tree update timing), the user has to click again to start typing.

**Mitigation**: Use `Dispatcher.UIThread.Post(() => textBox.Focus(), ...)` to
defer focus until after the visual tree update.

**Severity**: Medium. Affects UX but not correctness.

### R2: Pointer event capturing during drag

**Risk**: When dragging a class, the pointer events must be captured by the
class rectangle even if the mouse moves outside its bounds. If not, the drag
ends prematurely when the mouse leaves the class.

**Mitigation**: Use `PointerCapture` API on the class rectangle when drag
starts. Release on drag end.

**Severity**: High. Affects core interaction.

### R3: Concurrent edits in undo/redo stack

**Risk**: If the user starts a drag, then triggers undo (e.g. via keyboard
shortcut), the undo could fire while the drag is in progress, leading to
inconsistent state.

**Mitigation**: Disable keyboard shortcuts (especially Ctrl+Z) while a drag
is in progress. Re-enable on drag end.

**Severity**: Medium. Edge case but confusing if it happens.

### R4: JSON file corruption

**Risk**: If a `.dgraph.json` file is corrupted (truncated, manually edited
badly), the loader crashes or produces an invalid graph.

**Mitigation**: `TryLoad` catches all exceptions and returns null. The UI
shows an error dialog with the file path. The user can either fix the file
manually or start over.

**Severity**: Low. JSON corruption is rare.

### R5: Mermaid export syntax errors

**Risk**: If the user creates a class with a name that Mermaid rejects (e.g.
containing special characters), the export produces invalid Mermaid that
doesn't render.

**Mitigation**: The existing `MermaidGraphExporter` sanitization (added in
the previous plan) handles most cases. For class names specifically, the
inline edit validates against C# identifier rules, which are a superset of
Mermaid's allowed identifiers. Edge cases (e.g. names that are valid C# but
invalid Mermaid) should be caught by the `DesignValidator` and reported.

**Severity**: Low. Sanitization covers most cases.

### R6: Performance with many classes

**Risk**: With 100+ classes, the canvas becomes slow (many hit-tests per
mouse move, many layout calculations).

**Mitigation**:
- Acceptable at 50–100 classes with current O(n) hit-testing (both
  `HitTestService` and `DesignHitTestService` are linear scans).
- Limit layout engine iterations (already done in the previous plan).
- Consider virtualization for the class list (future).
- Consider LOD (level-of-detail) rendering — don't draw member text for
  classes smaller than a threshold (future).
- Spatial pruning for hit-testing (future optimization, not current capability).

**Severity**: Medium. Affects usability for large designs.

### R7: C# stub compilation failures

**Risk**: The generated C# stub might not compile due to subtle syntax issues
(generic constraints, partial classes, etc.).

**Mitigation**: Validate the generated stub by parsing with Roslyn
(`CSharpSyntaxTree.ParseText`) before writing the file. If parsing fails,
show an error dialog.

**Severity**: Low. The stub is simple enough to be reliable.

### R8: Loss of work on app crash

**Risk**: If the app crashes while the user is editing, all unsaved work is
lost.

**Mitigation**: Auto-save every 30 seconds to a temp file. On startup,
check for the temp file and offer to recover. This is implemented in M6.

**Severity**: Medium. Affects user trust.

### R9: Namespace handling in C# stub

**Risk**: The C# stub generator needs to produce valid `namespace` blocks.
Nested namespaces, partial classes, and using directives are all edge cases.

**Mitigation**: v1 supports flat namespaces only. Nested namespaces are
documented as out of scope. The generator emits `using System;` by default.

**Severity**: Low for v1. Higher if users request nested namespace support.

### R10: Layout engine interaction with manual positions

**Risk**: When the user clicks "Apply Layout", the layout engine repositions
all classes. The user's manual positions are lost (unless they undo).

**Mitigation**: Undo/redo covers this — the user can undo the layout
application. The undo is scoped to the entire layout operation (one undo
step, not per-class).

**Severity**: Low. Undo handles it.

### R11: Edge port discovery on small classes

**Risk**: For very small classes (e.g. 50x30px), the edge ports might
overlap with the class body or be hard to click.

**Mitigation**: Minimum class size is 180x42px (enforced in the resize
handle). Ports are 10px circles at the vertical center, well within bounds.

**Severity**: Low. Min size prevents the issue.

### R12: Concurrent file saves

**Risk**: If the user has the same file open in two windows, saving in one
window overwrites the other's changes without warning.

**Mitigation**: Out of scope for v1. Future: detect external file changes
via file hash on load, warn on save if changed.

**Severity**: Low. Uncommon usage pattern.

## Decisions log

### D0 (post-review correction): `ClassRectangle` is a plain data object, not an Avalonia Control

**Status**: Decided after architecture review. **Rationale**: The existing
rendering pipeline is immediate-mode Skia into a single bitmap
(`GraphCanvas.RenderNow()` → `CanvasRenderer.DrawNodes` → blit). An
Avalonia `Control`-based `ClassRectangle` would need its own render path,
its own pointer-event coordination with `GraphCanvas`'s existing handlers,
and its own hit-tester finer-grained than `HitTestService`. The cleaner
shape is a plain C# class that `DesignCanvasController` consults during
pointer handling and `CanvasRenderer` (extended) draws into the existing
bitmap. The one exception is the inline-edit `TextBox` overlay, which is
the only real Avalonia Control added (Skia can't host a live text widget).

This correction is documented in doc 04 and ripples through doc 02 (input
routing — `GraphCanvas` pointer handlers DO change), doc 07 (M2's
"modified files" list now includes `GraphCanvas.cs` and `CanvasRenderer.cs`),
and doc 08 (corrected the false "HitTestService spatial pruning" claim).

### D1: Dual mode architecture (Analyze + Design)

**Status**: Decided. **Rationale**: Cleanest separation. Shared infrastructure
(canvas, layout, export) is reused. Mode toggle is simple UI. Each mode has
its own toolbar that's visibility-toggled.

**Alternatives considered**:
- Single unified mode (no toggle) — rejected: too much UI clutter, two
  different mental models.
- Plugin-based extensibility — rejected: over-engineering for v1.

### D2: ClassRectangle is an interaction wrapper, not a visual wrapper

**Status**: Decided. **Rationale**: Reuses existing `CanvasRenderer` for
visual rendering. Single source of truth for class appearance. Avoids
duplication and drift.

**Alternatives considered**:
- Separate visual rendering for Design Mode — rejected: duplicates code,
  drifts over time.
- Replace `CanvasRenderer` entirely — rejected: too invasive, breaks
  Analyze Mode.

### D3: Undo/redo scoped to Design Mode only

**Status**: Decided. **Rationale**: Analyze Mode is read-only (no mutations
to undo). Design Mode has mutations (add/delete/move/rename). Mixing them
would be confusing.

**Alternatives considered**:
- Global undo across modes — rejected: confusing semantics.
- No undo at all — rejected: users expect undo in any modern editor.

### D4: GUIDs for class IDs, not human-readable names

**Status**: Decided. **Rationale**: Avoids ID collisions when classes are
renamed or have duplicate names. Makes rename operations trivial.

**Alternatives considered**:
- Use names as IDs — rejected: rename is expensive (must update all edges).
- Use composite `(Namespace.Name)` as ID — rejected: still breaks on rename.

### D5: JSON schema versioning from day 1

**Status**: Decided. **Rationale**: Forward compatibility is cheap to add
now, expensive to retrofit later. The `Version` field is one line.

**Alternatives considered**:
- No versioning, fix issues by breaking changes — rejected: loses user data.

### D6: Defer layout in Design Mode

**Status**: Decided. **Rationale**: User draws freely, layout runs on demand.
Avoids jarring jumps when adding multiple classes in quick succession.

**Alternatives considered**:
- Auto-layout on every add — rejected: too jarring.
- Auto-layout on mode switch — rejected: still jarring if user adds many
  classes before switching.

### D7: Reuse existing `MermaidGraphExporter` via TypeGraph conversion

**Status**: Decided. **Rationale**: Guarantees identical Mermaid output
between Analyze Mode and Design Mode. Reuses all sanitization logic.

**Alternatives considered**:
- Separate Mermaid generator for Design Mode — rejected: duplicates logic,
  drifts over time.

### D8: C# stub is minimal (no method bodies, no attributes)

**Status**: Decided. **Rationale**: The stub is a starting point, not a
complete implementation. Users fill in the details.

**Alternatives considered**:
- Full implementation generation — out of scope, requires code analysis
  beyond class diagrams.

### D9: No tabs in v1 (single design at a time)

**Status**: Decided. **Rationale**: Tabs add UI complexity. Single design
covers the v1 use case. The data model supports multiple designs on disk.

**Alternatives considered**:
- Tabs from day 1 — rejected: premature complexity.

### D10: Rubber-band select uses simple rectangle intersection

**Status**: Decided. **Rationale**: Good enough for v1. Full polygon lasso
is more complex and rarely needed.

**Alternatives considered**:
- Polygon lasso — deferred: not needed for typical use.

## Deferred to future plans

- Multi-user real-time collaboration
- Round-trip with scanned code (edit design → generate code → scan code → diff)
- Custom themes / skins for the design canvas
- Plugin system for custom member kinds, custom edge types
- Mobile/touch-first interaction model
- Nested namespaces in Design Mode
- Polygon lasso selection
- Tabs for multiple open designs
- Concurrent file save detection
- LOD rendering for large designs
