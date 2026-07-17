# UI Restructure — Evaluation of the Implemented Result

Date: 2026-07-17 (follow-up to `2026-07-17-UI-restructure-menu-bar-and-sidebar-proposal.md`)
Status: Evaluation / critique. No code changes in this doc.
Basis: screenshot of the running app after phases 1–4, plus code inspection.

---

## 1. What works (proposal goals achieved)

- Menu bar exists and is complete: File / Edit / View / Navigate / Design / Help.
- Canvas is free of the export button stack — the single biggest visual win.
- Toolbar holds the every-session actions; the mode toggle reads clearly (green = active).
- Zoom % readout in the status bar works (shows "8%").
- Recent Designs, Validate Diagram, Help → Shortcuts have UI for the first time.
- Panels toggle (Ctrl+B / Ctrl+Shift+I), matrix check-syncs with Ctrl+M.

The skeleton is right. The problems below are about **skin and hierarchy**, not structure.

---

## 2. Defects (visual bugs — highest priority)

### 2.1 White text on light chrome (legibility bug)
44 controls carry `Foreground="White"` while sitting on explicit light backgrounds
(`#F0F0F0` sidebar, `#F5F5F5` inspector, light toolbar). In the screenshot you can see:
- Toolbar: Back / Forward / Reset / D1–D3 / Fit — white text on light-gray buttons.
- Sidebar: Traversal and Move Scope combo text washed out; edge-filter checkbox labels
  ("Inheritance", "Implements", "Associations") **nearly invisible** (white on #F0F0F0).
- Inspector: "Open in Explorer" white-on-light.
- The toolbar search box renders dark with white text — inverted vs. everything around it.

Root cause: these attributes date from when someone expected a dark theme; the chrome was
later painted light, and the attributes survived both the original layout and my move of
the controls into the toolbar. **Fix: strip `Foreground="White"` from every control on a
light surface** (keep it only on colored accent buttons: Scan green, Focus blue,
Edit-in-Design orange, mode toggle).

### 2.2 Three competing themes
Light menu/toolbar/sidebar · dark canvas + status bar + Symbol Search panel · plus the
dark-inverted search box. The app reads as two apps stitched together. Pick one:

- **Option A — Dark chrome (recommended).** Most diagram/code tools (VS Code, Rider,
  draw.io dark) run dark; the canvas, status bar, minimap, and Symbol Search panel are
  *already* dark. Making menu/toolbar/sidebar/inspector dark is mostly re-painting
  backgrounds and deleting light overrides.
- **Option B — Light chrome.** Cheaper: restyle the Symbol Search panel light, fix the
  search box, done. But the app keeps a "2010 WinForms" look next to its dark canvas.

### 2.3 Minimap floods orange when zoomed out
`MinimapControl.axaml`: the viewport rectangle has `Background="#FFE04020"` and is sized
`(viewW / zoom) * minimapScale`. At 8% zoom the rect covers (and overflows) the entire
minimap → the solid orange block in the screenshot that hides all map content.
**Fix:** when the viewport covers > ~90% of the world, draw the border only (no fill),
and clamp the rect to the minimap bounds.

---

## 3. Duplication inventory (still redundant after the restructure)

| Control | Copies | Where | Verdict |
|---|---|---|---|
| D1/D2/D3 (focus depth) | 2 | Toolbar **and** sidebar Focus Depth | Drop one — keep sidebar (state), drop toolbar row, or vice versa. |
| D1/D2/D3 (namespace depth) | 1 | Sidebar Namespace Focus | Different semantics, **same labels** — confusing. Rename to "Depth" combo or prefix "NS". |
| Scan | 2 | Toolbar + sidebar | Keep sidebar (contextual, next to folder path), drop toolbar's, or make toolbar's the only one. Two green primary buttons compete. |
| Fit | 2 | Toolbar + canvas overlay | Drop from toolbar — the canvas zoom cluster is the convention (§6). |
| Search | 2 | Toolbar (highlight) + Symbol Search panel | Intended split, but see 4.2 — the panel is starved. |
| Edge filters / Auto-redraw | 2 | Sidebar + View menu | Intended mirror — OK. |
| Mode toggle color | — | Analyze green **and** Scan green = three green buttons | Use one accent color for primary action only; mode toggle should be a neutral segmented control. |

---

## 4. Information-hierarchy problems

### 4.1 The sidebar is still one long scrolling column
We removed ~30% of the controls but kept the flat stack. On a 1080p screen the Symbol
Search panel lands at the fold and its results list is unusable without scrolling.
The deferred §5 `Expander` regrouping is now clearly the next structural step:
Focus (depth+traversal+seeds), Filters (namespace+edges+move scope), Search, Classes.

### 4.2 The Classes list — the main navigation surface — is starved
`MinHeight="150"` shows ~2 rows. For a scanned project with hundreds of types, the list
is the primary way to navigate, yet it gets leftover space while D-buttons appear three
times. Give it flexible height (`*` row or put it in an Expander that defaults open and
takes remaining space).

### 4.3 Inspector empty state wastes the right column
"No node selected" + an enabled **Open in Explorer** button (no-ops when nothing is
selected, but *looks* clickable) + three empty section headers (Members/Outgoing/Incoming).
Fix: (a) disable Open in Explorer when `SelectedNode == null`; (b) show a summary in the
empty state (type/edge counts, hints: "Click a class", "Ctrl+K to search") instead of
empty headers — the Design Mode inspector already does this (`InspectorNothingState`);
Analyze Mode deserves the same.

### 4.4 Orphan status line
"Code → Clipboard (Ctrl+Shift+C)" floats under Move Scope — it is status, not a control.
Move it to the status bar (§8's deferred segment) or into the Edit menu item's tooltip.

### 4.5 Toolbar dead space + wrong tenant
The toolbar's center/right is empty while "Fit" (redundant) occupies a slot. Better use:
the **Traversal** combo or **Namespace Focus** combo are the two most-adjusted view-shaping
controls — either could live in the toolbar's empty middle, freeing sidebar space.

---

## 5. Smaller polish items

- Disabled menu items give no reason ("Design Mode only") — deferred phase-5 polish.
- Menu gesture headers are fixed-width grids (220/240) — fine, but won't localize well.
- `Fit` on canvas overlay + toolbar (see §3).
- Namespace depth buttons unlabeled (see §3).
- Status bar segments aren't clickable (deferred §8).
- Toolbar buttons inconsistent heights/paddings (mode toggle vs. rest).

---

## 6. Recommended fix batches

**Batch 1 — Legibility (do first, ~1 h, zero risk)**
1. Strip `Foreground="White"` from all controls on light chrome (keep colored accents).
2. Minimap: border-only viewport rect when it covers >90% of the world; clamp to bounds.
3. Disable "Open in Explorer" when nothing is selected.

**Batch 2 — Dedupe (~1 h)**
4. Remove toolbar **Fit** (keep canvas overlay).
5. Remove one **Scan** (recommend: keep sidebar, drop toolbar's — or make toolbar Scan the
   only one and turn the sidebar one into a text link).
6. Remove toolbar D1–D3 **or** sidebar Focus-Depth D1–D3; rename Namespace depth buttons.
7. Neutralize mode-toggle colors (segmented control, one accent).

**Batch 3 — Hierarchy (half day)**
8. `Expander` sections in the sidebar (Focus / Filters / Search), Classes list gets
   remaining space.
9. Analyze-inspector empty state with summary + hints.
10. Move the code-output indicator into the status bar.

**Batch 4 — Theme decision (½–1 day)**
11. Dark chrome everywhere (recommended), including Symbol Search panel already dark;
    or full light. Either way: one theme, no `Foreground="White"` crutches.

---

## 7. Verdict

The restructure achieved its structural goals (menus, toolbar, decluttered canvas,
discoverable shortcuts, orphaned features surfaced). What the screenshot reveals is that
the app now needs a **visual pass**, not another structural one: fix the white-on-light
legibility bug (2.1) immediately — it's the kind of defect that makes the whole app feel
broken — then dedupe the remaining ×2/×3 controls, then give the Classes list and Symbol
Search their space back via the already-planned `Expander` regrouping. The menu/toolbar
skeleton should not change; it held up.
