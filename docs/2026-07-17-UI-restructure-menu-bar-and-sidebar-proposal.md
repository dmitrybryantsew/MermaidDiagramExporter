# UI Restructure Proposal — Menu Bar, Toolbar, and Sidebar Cleanup

Date: 2026-07-17
Status: **Implemented** (phases 1–4; see "Implementation notes" at the bottom)
Scope: `MainWindow.axaml` and surrounding GUI shell. Complements `docs/Done/UIContract.md`
(which covers canvas *interaction* design for Design Mode — this doc covers *app shell* layout).

---

## 1. What's wrong today

The current shell crams **every command into a single 320px scrolling sidebar** plus two
floating button stacks on the canvas. Concrete problems, grounded in `MainWindow.axaml`:

### 1.1 The sidebar is a control dump
The Analyze panel alone is a ~10-section scrolling StackPanel with ~30 controls, all at
equal visual weight: folder picker, 6 navigation buttons, focus depth, traversal, seeds,
edge filters, move scope, namespace focus (with a *second* set of D1/D2/D3 buttons), two
separate search UIs, a design-mode bridge, a class list, and stats. There is no hierarchy
between "actions I click constantly" and "settings I touch once".

### 1.2 Canvas overlays are used as menus
Five export buttons (`Save PNG`, `Save .mmd`, `Save .md`, `Copy Mermaid`, `Open Live
Editor`) are stacked bottom-left **on top of the diagram**, permanently occluding canvas
content. These are textbook menu/toolbar commands — they have no reason to float over the
workspace. (Zoom controls top-right are a legitimate overlay convention; see §6.)

### 1.3 Duplicated controls across modes
`Settings`, `Reset Layout`, and `Move Scope` exist in *both* the Analyze and Design
panels. App-global commands should live in one app-global place.

### 1.4 Features with no UI surface
- `DesignRecentFiles` (`MainWindow.axaml.cs:42`) tracks recent designs on save/open —
  but nothing in the UI ever shows them. A `File → Recent Designs` menu is the natural home.
- Several keyboard shortcuts (Alt+←/→, Ctrl+R, Ctrl+Shift+C, F, tool keys V/C/I/E/…)
  are invisible — only documented in README and inline hint labels. Menus make shortcuts
  discoverable for free (the gesture shows next to the item).

### 1.5 Two parallel search experiences
"Search — Type to highlight..." (a TextBox) and "Symbol Search" (a full panel with query +
Focus + results list) sit adjacent in the sidebar doing overlapping jobs.

### 1.6 Inline help text eats space
"Drag LMB on empty canvas to marquee-select…" and the Design "Tips" block are permanent
sidebar residents. This content belongs in tooltips, the status bar, and a Help menu.

---

## 2. Design principles

1. **Separate verbs from state.** *Actions* (scan, export, reset, undo) go to the menu bar
   / toolbar / keyboard. *State that shapes the view* (focus depth, traversal, filters)
   stays in the sidebar — that *is* the app's core workflow and deserves to be visible.
2. **Three-tier command exposure** (standard desktop contract):
   - **Menu bar** — the *complete* command inventory. Every action discoverable, every
     shortcut visible. Nothing lives *only* in a menu (menus are slow); nothing important
     is *absent* from a menu (menus are searchable documentation).
   - **Toolbar** — the 6–10 actions used every session.
   - **Sidebar** — contextual panels and lists, not button storage.
3. **Nothing floats over the canvas except view controls.** Canvas overlays limited to
   zoom/fit (and transient UI like the minimap, matrix, inline-edit box).
4. **One home per command.** No control duplicated across modes; app-global commands live
   in app-global chrome (menu/toolbar/status bar).
5. **Additive migration.** The menu can be introduced *without removing anything* first
   (see §9), so there's no big-bang breakage.

---

## 3. Proposed layout

```
┌─────────────────────────────────────────────────────────────────────────────┐
│ File   Edit   View   Navigate   Design   Help                               │  ← Menu bar (new)
├─────────────────────────────────────────────────────────────────────────────┤
│ [Analyze|Design]  📂 Scan   ← →   D1 D2 D3   Fit   │ 🔍 Search symbols…     │  ← Toolbar (new, slim)
├──────────┬──────────────────────────────────────────────────────┬───────────┤
│ SIDEBAR  │                                                      │ INSPECTOR │
│ (task-   │                 CANVAS                               │ (as-is,   │
│ focused  │                                                      │  maybe    │
│ panels,  │        ┌─────┐                                       │  collaps- │
│ no       │        │ Fit │  ← zoom cluster only (top-right)      │  ible     │
│ button   │        │  +  │                                       │  sections)│
| dumps)   │        │  −  │                                       │           │
│          │        └─────┘                        ┌──────────┐   │           │
│          │                                       │ minimap  │   │           │
│          │                                       └──────────┘   │           │
├──────────┴──────────────────────────────────────────────────────┴───────────┤
│ Analyze │ Tool: Select │ Sel: 3 classes │ Zoom 100% ▾ │ 142 types, 310 edges│ ← Status bar (enriched)
└─────────────────────────────────────────────────────────────────────────────┘
```

Root layout change: `Grid RowDefinitions="*,Auto"` → `RowDefinitions="Auto,Auto,*,Auto"`
(menu, toolbar, content, status).

---

## 4. Menu structure (full command inventory)

Every existing handler is mapped — **nothing is lost**. Items marked *(Analyze)* /
*(Design)* disable when the other mode is active, with a tooltip explaining why
(static menus that enable/disable are less disorienting than menus that appear/vanish).

### File
| Item | Shortcut | Current location / handler |
|---|---|---|
| Open Source Folder… | Ctrl+Shift+O | `OnBrowse` (sidebar "Browse…") |
| Rescan | F5 | `OnScan` (sidebar "Scan") — keep on toolbar too |
| ── | | |
| New Design | Ctrl+N | `OnDesignNew` |
| Open Design… | Ctrl+O | `OnDesignOpen` |
| **Recent Designs →** | | **`DesignRecentFiles` — currently no UI at all** |
| Save | Ctrl+S | `OnDesignSave` *(Design)* |
| Save As… | Ctrl+Shift+S | `OnDesignSaveAs` *(Design)* |
| ── | | |
| Export → | | submenu below |
| &nbsp;&nbsp;&nbsp;&nbsp;PNG Image… | | `OnSavePng` (canvas overlay) |
| &nbsp;&nbsp;&nbsp;&nbsp;Mermaid (.mmd)… | | `OnSaveMmd` (canvas overlay) |
| &nbsp;&nbsp;&nbsp;&nbsp;Markdown (.md)… | | `OnSaveMd` (canvas overlay) |
| &nbsp;&nbsp;&nbsp;&nbsp;Copy Mermaid to Clipboard | Ctrl+Shift+M | `OnCopyMermaid` (canvas overlay) |
| &nbsp;&nbsp;&nbsp;&nbsp;Open in Mermaid Live Editor | | `OnOpenLiveEditor` (canvas overlay) |
| &nbsp;&nbsp;&nbsp;&nbsp;── | | |
| &nbsp;&nbsp;&nbsp;&nbsp;C# Stub… *(Design)* | | `OnDesignExportCSharp` |
| &nbsp;&nbsp;&nbsp;&nbsp;JSON… *(Design)* | | `OnDesignExportJson` |
| ── | | |
| Project Settings… | Ctrl+, | `OnOpenSettings` (currently duplicated in both panels) |
| Exit | Alt+F4 | (implicit) |

### Edit
| Item | Shortcut | Current location |
|---|---|---|
| Undo | Ctrl+Z | `OnDesignUndo` *(Design)* |
| Redo | Ctrl+Y | `OnDesignRedo` *(Design)* |
| ── | | |
| Rename | F2 | keyboard only *(Design)* |
| Delete Selection | Del | keyboard only *(Design)* |
| ── | | |
| Copy Class Code | Ctrl+Shift+C | keyboard only *(Analyze)* — currently only hinted by a tiny label |
| ── | | |
| Add Class | C | `OnDesignAddClass` *(Design)* |
| Connect → | | `OnDesignConnect` + edge-type combo *(Design)* — submenu of 6 edge types mirrors `DesignEdgeCombo` |

### View
| Item | Shortcut | Current location |
|---|---|---|
| Zoom In / Zoom Out | + / − | canvas overlay `OnZoomIn/Out` |
| Fit to Screen | F | canvas overlay `OnFit` |
| ── | | |
| ✓ Matrix | Ctrl+M | `OnToggleMatrix` (sidebar button → checkable menu item) |
| ✓ Minimap | | `MinimapView` visibility (currently buried) |
| ✓ Inspector Panel | Ctrl+Shift+I | new — toggle right panel |
| ✓ Sidebar | Ctrl+B | new — toggle left panel (VS Code muscle memory) |
| ── | | |
| Edge Filters → | | submenu of 3 checkable items: |
| &nbsp;&nbsp;&nbsp;&nbsp;✓ Inheritance | | `ShowInheritanceCheck` |
| &nbsp;&nbsp;&nbsp;&nbsp;✓ Implements | | `ShowImplementsCheck` |
| &nbsp;&nbsp;&nbsp;&nbsp;✓ Associations | | `ShowAssociationsCheck` |
| ✓ Auto-redraw Edges on Move | | `AutoRedrawCheck` |
| Redraw Edges Now | Ctrl+R | keyboard only, invisible today |
| ── | | |
| Reset Layout | | `OnResetLayout` / `OnDesignResetLayout` (currently duplicated per mode → one item, dispatches per mode) |

### Navigate *(Analyze)*
| Item | Shortcut | Current location |
|---|---|---|
| Back | Alt+← | `OnBack` (sidebar) |
| Forward | Alt+→ | `OnForward` (sidebar) |
| Reset View | | `OnReset` (sidebar) |
| ── | | |
| Focus Current Selection | | `OnFocusCurrent` (big blue sidebar button) |
| Focus Depth → D1 / D2 / D3 | 1 / 2 / 3 | radio submenu, replaces the two sets of D-buttons |
| Seeds → Add / Remove / Clear | | `OnAddSeed` / `OnRemoveSeed` / `OnClearSeeds` |
| ── | | |
| Find Symbol… | Ctrl+K | unifies Symbol Search (see §7) |

### Design *(Design)*
| Item | Shortcut | Current location |
|---|---|---|
| Generate from LLM… | | `OnDesignLlmGenerate` (purple button) |
| Validate Diagram | | `DesignValidator` — exists in code, worth surfacing |
| ── | | |
| Tools → Select / Class / Interface / … | V C I E S A T N | keyboard-only today (README table) — menu makes them discoverable |
| Edge Tools → Inherit / Impl / Assoc / … | H M L D G O | same |
| ── | | |
| Import Scanned Architecture | | `OnEditInDesignMode` (the orange "Edit in Design Mode" bridge) |

### Help
| Item | | |
|---|---|---|
| Keyboard Shortcuts… | | renders the README shortcut table + the inline "Tips"/marquee text currently taking sidebar space |
| About | | version, links |

**Result:** the five canvas-overlay export buttons, six sidebar nav buttons, Settings (×2),
Reset Layout (×2), Matrix, LLM-generate, and all file ops leave the sidebar/canvas and gain
visible shortcuts. The orphaned Recent Files feature gets a UI.

---

## 5. What stays in the sidebar (and how it's reorganized)

The sidebar keeps only *stateful, workflow* controls, grouped into collapsible sections
(Avalonia `Expander`) so the panel stops scrolling:

**Analyze sidebar (top → bottom):**
1. **Project** — folder path (read-only) + primary `Scan` button. (Browse moves to File
   menu, but keep a small "…" button beside the path for discoverability.)
2. **Focus** — depth D1/D2/D3 (segmented control instead of 3 buttons), traversal combo,
   seeds (Add/Remove/Clear), `Focus Current Selection`. *This is the app's signature
   workflow — it stays visible, just compacted.*
3. **Namespace Filter** — namespace combo + "Show connected" + its depth control.
   (Move Scope combo moves here too — it filters drag behavior, one instance, not two.)
4. **Classes** — the list (primary navigation surface).
5. **Stats** — one gray line, as today.

Gone from the sidebar: Back/Forward/Reset, Settings, Reset Layout, Matrix, edge-filter
checkboxes (menu), auto-redraw (menu), the two help-text paragraphs (Help menu/tooltips),
the highlight-search box (merged — §7), the design bridge (Design menu + a compact link
under Stats if desired).

**Design sidebar:** per `docs/Done/UIContract.md` §3, this should become a *toolbox*
(element/edge tools) — that doc already covers it; the File/Edit/Export/LLM/Tips sections
it proposed to keep in the sidebar now move to menus instead, leaving room for the toolbox.

---

## 6. Canvas overlays — what moves, what stays

| Overlay | Verdict |
|---|---|
| Export stack (5 buttons, bottom-left) | **Remove entirely** → File → Export + toolbar/shortcuts. Biggest visual win. |
| Zoom cluster (Fit/+/−, top-right) | **Keep.** Universal convention (draw.io, Figma, Miro); eyes/hands already expect it. Optionally shrink to icon buttons. |
| Minimap (bottom-right) | Keep; add View-menu toggle. |
| Matrix / inline-edit box | Keep (transient). |

---

## 7. Unify the two searches

Today: "Search — Type to highlight…" TextBox **and** the Symbol Search panel sit side by
side. Proposal:

- **One search box in the toolbar** (right-aligned, Ctrl+K). Typing shows a dropdown of
  symbol results (reuse `SearchResultViewModel` items). `Enter` = focus first result on
  canvas; `Esc` clears.
- A toggle in the dropdown (or two result sections): **Highlight matches** (current
  `OnSearchTextChanged` behavior) vs **Focus/filter canvas** (current SearchPanel "Focus"
  behavior). Same index (`SymbolIndex`), two render verbs.
- The dedicated SearchPanel can remain as a sidebar section for power users, but the
  toolbar box covers the 95% case and kills the duplication.

Stretch goal (separate doc): a real Ctrl+K command palette that also searches *commands*
("export png", "reset layout") — the menu inventory from §4 makes this trivial later.

---

## 8. Status bar — from label to control strip

Today it's a single `StatusBarText`. Enrich into VS Code-style clickable segments:

```
[Analyze ▾] │ Tool: Select │ Sel: Customer +2 │ Zoom: 100% ▾ │ Edges: all shown ▾ │ 142 types · 310 edges │ Code→Clipboard
```

- Mode segment doubles as the mode switch (redundant with toolbar toggle — fine).
- Zoom segment: click → menu (Fit / 100% / +/-) — lets you delete the canvas zoom cluster later if wanted.
- Edge segment: quick access to the 3 edge-filter toggles (mirrors View menu).
- The `CodeOutputIndicator` text ("Code → Clipboard (Ctrl+Shift+C)") moves here from the sidebar.

---

## 9. Toolbar contents (keep it honest — ≤ 10 items)

`[Analyze|Design] segmented toggle │ Scan │ ← → │ D1 D2 D3 │ Fit │────────│ search box`

- **Mode toggle moves here** from the sidebar-top. It's app-level state; a segmented
  control in the toolbar (à la Xcode/Figma) is clearer than two colored buttons and frees
  the sidebar to be purely mode-specific content.
- Everything on it also exists in a menu. It's an accelerator strip, not a second inventory.

Alternative considered: put the mode toggle in the *status bar* only — rejected; mode is
too fundamental to hide at the bottom.

---

## 10. Avalonia implementation notes (for later)

- Use the in-window `Menu` control as first row of the root grid (cross-platform,
  styleable to match the dark canvas). `NativeMenu` (OS-integrated, macOS global menu bar)
  can come later via `NativeMenuBar` — the app is Windows-first today.
- `MenuItem HotKey="Ctrl+S"` displays *and* dispatches gestures — this alone removes the
  need for several of the manual `KeyDown` branches in `MainWindow.axaml.cs` (file ops,
  export). Canvas-dependent keys (tool letters, nudge) stay in `KeyDown`.
- Checkable items: `ToggleType="CheckBox"` (edge filters, minimap) and `"Radio"` (focus depth).
- Enable/disable per mode: bind `IsEnabled` to a tiny shell view-model exposing
  `IsAnalyzeMode` — currently mode state lives in code-behind; the menu is a good forcing
  function to extract a small `ShellViewModel` (the god-class split in `docs/Done/18` noted
  this need already).
- Commands: start with `Click` handlers calling the existing `OnX` methods (zero-risk);
  migrate to `ICommand` only if/when the palette or context menus need them.

---

## 11. Alternatives considered

| Option | Verdict |
|---|---|
| **Menu bar + slim toolbar (this proposal)** | ✅ Best fit: desktop-conventional, discoverable, incremental migration, low risk. |
| VS Code-style **activity bar / icon rail** (far-left icons switching sidebar panels: Project / Focus / Search / Settings) | Elegant for many panels, but we only have ~4 — overkill now. Revisit if panels grow (AI chat, validation, history…). Mentioned as a future option; menu bar still needed under it anyway. |
| **Ribbon** (Office-style) | ❌ Too heavy for ~40 commands; wastes vertical space; unidiomatic in Avalonia. |
| **Command palette only** (Ctrl+K everything, no menu) | ❌ Great *addition*, terrible *only* mechanism — zero discoverability for new users. Build it later on top of the menu inventory (§7). |
| Do nothing, just tidy sidebar | ❌ Doesn't solve canvas occlusion, invisible shortcuts, or orphaned features. |

---

## 12. Phased migration (each phase independently shippable)

1. **Add the menu bar** wired to existing handlers (pure addition — nothing removed).
   Includes Recent Designs (first-ever UI for it) and Help → Shortcuts.
2. **Add the slim toolbar** with mode toggle + top actions. Keep sidebar buttons for now.
3. **Remove migrated controls**: canvas export stack, sidebar nav row, Settings/Reset
   Layout duplicates, inline help text. Regroup sidebar into `Expander` sections (§5).
4. **Unify search** into the toolbar box (§7); enrich the status bar (§8).
5. **Polish**: tooltips with disabled-reasons ("Switch to Design mode to use this"),
   icon pass on toolbar, `ShellViewModel` extraction.

Phases 1–2 are pure win with zero removal-risk; phase 3 is where the visual declutter
actually lands.

---

## 13. Open questions

1. **Windows-only, or macOS/Linux too?** If cross-platform matters, plan `NativeMenu`
   from the start (changes how File/Exit/Settings are conventionally placed).
2. **Toolbar: yes or menu-only?** Proposal includes a slim one; a purist could skip it and
   rely on menu + shortcuts. Leaning yes — Scan and Back/Forward are every-session actions.
3. **Keep zoom cluster on canvas, or move to status bar only?** Proposal keeps both.
4. **Does "Move Scope" belong per-mode or global?** It's currently duplicated with two
   combos (`AnalyzeMoveScopeCombo`, `DesignMoveScopeCombo`) — merge to one setting, or do
   the modes genuinely need independent values?
5. **Mode-specific menus disable vs hide?** Proposal: disable with reason-tooltips.

---

## 14. Implementation notes (2026-07-17)

Phases 1–4 are implemented in `MainWindow.axaml` / `MainWindow.axaml.cs`. Build is green
and all 317 tests pass; the app starts cleanly (all `HotKey` strings parse at XAML load).

**Deviations from the proposal**
- **Edge filters and Auto-redraw stayed in the sidebar** (they are *state*, per principle
  #1) and are additionally mirrored as checkable View-menu items. The sidebar CheckBoxes
  remain the single source of truth; `SyncViewMenuChecks()` keeps both directions in sync.
- **Status bar**: only the zoom-percentage readout was added (§8's full clickable-segment
  strip is deferred — it requires restructuring `UpdateStatusBar`).
- **Toolbar search** is the highlight-as-you-type box *moved* from the sidebar (Ctrl+K
  focuses it). The dropdown-results/command-palette part of §7 is deferred; the Symbol
  Search panel stays in the sidebar as the advanced/focus search.
- **Matrix** stayed always-available rather than mode-gated; in Design Mode the menu
  handler only allows turning it *off*, never on.

**Behavior changes beyond the proposal**
- `New Design` / `Open Design` (menu) now work from **any** mode and switch the UI to
  Design Mode (`SwitchToDesignModeUi()`). Previously they were only reachable inside the
  Design panel.
- Bug fix: `OnDesignNew` now clears `_designFilePath` — previously a *New* followed by
  Ctrl+S would silently overwrite the previously opened design file.
- `Open Design` is wrapped in try/catch and prunes missing files from Recent Designs.
- `UpdateNavigationButtons()` is now mode-aware (Back/Forward/Reset + their menu items
  disable in Design Mode; Alt+←/→ no longer navigates the Analyze graph while designing).
- `Scan` (F5/toolbar/menu) is gated to Analyze Mode.
- New global keys (TextBox-safe): `+`/`−` zoom and `F` fit now work in **both** modes;
  `F2` rename was added (the README advertised it but no handler existed) using the
  double-click inline-edit path.
- HotKey dispatch: `MenuItem.HotKey` is used only for gestures with **no** existing
  `OnKeyDown` branch (F5, Ctrl+Shift+O, Ctrl+K, Ctrl+M, Ctrl+B, Ctrl+Shift+I,
  Ctrl+Shift+M, Ctrl+,), avoiding double-dispatch. Existing shortcuts keep dispatching via
  `OnKeyDown` and show their gesture in menus via custom two-column headers.
- `Validate Diagram` (Design menu) is the first UI surface for `DesignValidator`.

**Not implemented (deferred)**
- §5 sidebar regroup into `Expander` collapsible sections (sections were trimmed of
  migrated actions but remain a plain StackPanel).
- §7 dropdown search results / command palette.
- §8 full segmented status bar.
- Design Mode sidebar toolbox per `docs/Done/UIContract.md` §3 (the Design sidebar now
  holds Move Scope + Add tools only, ready for it).
