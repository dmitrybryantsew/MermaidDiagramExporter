# 01 — Vision and Scope

## What Design Mode is

A visual authoring surface for class diagrams. You draw class rectangles on a
canvas, add members (fields, properties, methods), draw edges between them
(association, inheritance, implements), and export the result as Mermaid
diagrams, JSON, or stub C# source code.

It is the **reverse** of the current Analyze Mode: instead of scanning code
to produce a diagram, you draw a diagram to produce code/spec.

## Who it's for

- **Architects** sketching a class structure before implementation.
- **Designers** communicating a proposed structure to a team.
- **Documentation authors** producing diagrams for READMEs/wikis without
  having the actual code yet.
- **Reverse-engineering sessions** where you want to capture a design
  from a whiteboard discussion.

## What it produces

- **Mermaid `.mmd`** — same export pipeline as Analyze Mode, valid Mermaid
  syntax, renderable on mermaid.ai.
- **JSON** — `DesignGraph` schema, round-trippable, diffable in git.
- **C# stub source** — minimal `class`/`interface` declarations matching the
  drawn diagram, ready to paste into a project as a starting point.
- **PNG/SVG** — same export as Analyze Mode.

## What you can do in Design Mode

- **Add a class**: click on empty canvas → new class rectangle appears with
  default name "NewClass", immediately editable.
- **Edit a class**: double-click header to rename; click "+" to add fields/
  properties/methods; click member to rename; click × to delete.
- **Move a class**: drag the header.
- **Resize a class**: drag bottom-right corner handle.
- **Connect two classes**: drag from a class's edge port to another class's edge
  port ��� edge appears, type selector prompts (association/inheritance/implements).
- **Delete**: select + Delete key, or right-click → Delete.
- **Select multiple**: rubber-band select, or Ctrl+click.
- **Undo/Redo**: Ctrl+Z / Ctrl+Shift+Z.
- **Save/Load**: `.dgraph.json` files in the user's chosen folder.
- **Import from scan**: open a previously-scanned graph as a starting point,
  then edit.

## Explicit non-goals

- **Not a full UML editor.** No support for: notes, stereotypes-as-graphical-
  shapes, package-as-folder-tree, sequence diagrams, state diagrams, swim lanes.
  Scope is class diagrams only.
- **Not a code generator.** The C# stub export is minimal (class declarations
  with member signatures only — no method bodies, no constructors, no
  attributes beyond access modifiers). It is a *starting point*, not a
  complete implementation.
- **Not a round-trip tool.** Editing a design and then scanning an existing
  codebase won't magically merge them — that's a future feature.
- **Not a collaboration tool.** Single-user, single-machine. No real-time
  multi-user editing, no comments, no version history beyond git.
- **Not a replacement for Analyze Mode.** Analyze Mode stays exactly as it is.
  Design Mode is an additional mode, not a replacement.

## Success criteria

Design Mode is "done" when:
1. A user can produce a 10-class diagram from scratch in under 5 minutes.
2. The exported Mermaid renders correctly on mermaid.ai without manual fixes.
3. The exported JSON round-trips: load → save → load produces identical graph.
4. The exported C# stub compiles in a fresh .NET project.
5. Switching modes mid-session does not lose work in either mode.
6. Undo/redo covers all editing operations (add, delete, move, rename, connect,
   disconnect, member edits).

## Out of scope (deferred to future plans)

- Multi-user real-time collaboration
- Round-trip with scanned code (edit design → generate code → scan code → diff)
- Custom themes / skins for the design canvas
- Plugin system for custom member kinds, custom edge types
- Mobile/touch-first interaction model (current plan is mouse-first)
