# Theme System + UI Fix Implementation Plan

Date: 2026-07-17
Audience: an LLM (or human) implementer. Self-contained: exact files, current values,
target values, order, verification, pitfalls.
Builds on: `2026-07-17-UI-restructure-evaluation.md` (the defects this fixes),
`2026-07-17-UI-restructure-menu-bar-and-sidebar-proposal.md` (what was implemented).

---

## 0. Ground truth — how color works in this app TODAY

Three separate color domains. Any theme implementation must handle all three or it will
look broken in exactly the ways the evaluation screenshot shows.

### Domain A — Fluent controls (menus, buttons, combos, textboxes, dialogs, context menus)
Themed by `Application.RequestedThemeVariant` (`App.axaml`, currently `"Default"` =
follow OS). Avalonia 12 re-themes at runtime when this property changes. **This is the
single biggest lever: one property themes every stock control.**

### Domain B — Chrome XAML hex (hardcoded, theme-blind)
| Current value | Where |
|---|---|
| `#F0F0F0` | sidebar bg, toolbar bg (`MainWindow.axaml`) |
| `#F5F5F5` | inspector bg |
| `#2A2E34` | status bar bg |
| `#3A4250` | status bar border, zoom overlay buttons, inspector member buttons, mode-toggle inactive |
| `#889098` | status bar text |
| `#D0D0D0` | GridSplitters, toolbar border |
| `#FFA040` | inline-edit border |
| `#1E2329`, `#3A4250` | `SearchPanel.axaml` (dark panel inside light sidebar — the clash) |
| `#15191E`, `#3A4250`, `#FFE040`, `#FFE04020` | `MinimapControl.axaml` |
| Accents (both themes): `#4CAF50` green, `#2196F3` blue, `#FF9800` orange, `#9C27B0` purple, `#FF8C00` selection | various |
| **`Foreground="White"` × 44** | toolbar buttons, sidebar combos/checkboxes/textboxes, inspector buttons — the legibility bug (evaluation §2.1) |
| Code-behind: `Brush.Parse("#3A4250"/"#4CAF50")` | `MainWindow.axaml.cs` `UpdateModeUi()` (mode toggle) |

### Domain C — Skia pixels (canvas renderers, constants in C#)
| File | Constants |
|---|---|
| `GraphCanvas.cs` | `ColorBg` (canvas clear color, dark navy) |
| `CanvasRenderer.cs` | cluster fill `#252A32`, cluster stroke `#3A4250`, labels `#708090`/`#889098`, node fill `#2D333F`, title text `#E0E6EC`, member text `#889098`, selection `#FF8C00`, hover `#60A0E0`, drop-target `#40B070`, marquee `#FFA040` |
| `MinimapControl.axaml.cs` | bg `#15191E`, node `#2D333F`, stroke `#4A6A8A`, edge `#3A4250`, viewport `#FFE040` |
| `NamespaceMatrixView.axaml.cs` | 7 brushes: header `#252B33`, header text `#B0B8C4`, cell border `#3A4250`, text `#E0E4EA`, cells `#1E242C`/`#1A1F26`, hot cell `#FF6040` |

### User-owned colors — DO NOT TOUCH
- `EdgeStyleSettings` (per-project edge colors in `ProjectSettings`) — user-configured.
- Stereotype badge colors (`CustomStereotypeEngine`, user regex rules with `ColorHex`).
The theme system must leave both alone.

### Persistence today
`SettingsService` writes **per-project** JSON to `%LocalAppData%/MermaidDiagramExporter/`
(`GetAppDataDirectory()`). ⚠️ `SaveSettings` returns early when `SourceFolderPath` is
empty — so app-global settings **cannot** reuse it. A separate tiny service is required.

### Startup today
`App.OnFrameworkInitializationCompleted` (`App.axaml.cs`) news up `SettingsService`,
`LayoutEngine`, `RoslynTypeScanner`, then `MainWindow(...)`. No DI container — new
services follow the same constructor-passing pattern.

---

## 1. Target architecture

```
App.axaml.cs startup:
  AppSettingsService.Load() ──► AppSettings { Theme: Dark|Light|System }
  ThemeService.Apply(settings.Theme)   // BEFORE MainWindow is created
       │
       ├─► Application.RequestedThemeVariant = Dark|Light|Default   (Domain A)
       ├─► rewrites brush resources in App.axaml ResourceDictionary  (Domain B)
       │      consumed via {DynamicResource XyzBrush} in XAML
       └─► RenderPalette.Current = Dark/Light instance               (Domain C)
              + raises ThemeChanged → canvas/minimap/matrix redraw
```

**New files** (all in `src/MermaidDiagramExporter.Gui/`):
| File | Contents |
|---|---|
| `Settings/AppSettings.cs` | `public sealed class AppSettings { public UiTheme Theme { get; set; } = UiTheme.System; }` |
| `Settings/AppSettingsService.cs` | Load/Save JSON at `GetAppDataDirectory()/app.settings.json`. Reuse `SettingsService.GetAppDataDirectory()`. Never throws; defaults on any error. |
| `Theming/UiTheme.cs` | `public enum UiTheme { System, Dark, Light }` |
| `Theming/UiPalette.cs` | Avalonia-bridge colors (brushes) for chrome. Static `Dark` and `Light` instances. |
| `Theming/RenderPalette.cs` | SKColor fields for Skia. Static `Dark`/`Light` instances + `static RenderPalette Current`. |
| `Theming/ThemeService.cs` | `Apply(UiTheme)`, `Current`, `event Action? ThemeChanged`. Rewrites app resources + RenderPalette.Current + RequestedThemeVariant. |

**Edited files**: `App.axaml` (resource dictionary), `App.axaml.cs` (startup),
`MainWindow.axaml` + `.cs`, `SearchPanel.axaml`, `MinimapControl.axaml` + `.cs`,
`GraphCanvas.cs`, `CanvasRenderer.cs`, `NamespaceMatrixView.axaml.cs`.

**New tests**: `tests/MermaidDiagramExporter.Tests/AppSettingsTests.cs` (roundtrip,
corrupt-file tolerance), `ThemeServiceTests.cs` (palette mapping — pure logic, no UI).

---

## 2. Steps in order (build + verify after EACH)

### Step 1 — Batch-1 quick fixes (independent of theming; do first, ~1 h)
From the evaluation doc. These stand alone even if theming slips.

1. **Strip `Foreground="White"`** from all 44 occurrences in `MainWindow.axaml`
   *except* buttons with explicit colored backgrounds (`#4CAF50`, `#2196F3`, `#FF9800`,
   `#9C27B0`, `#3A4250` zoom-overlay/member buttons, and the two mode-toggle buttons).
   After Step 4, ALL of these become theme tokens anyway — this step is the minimal
   legibility patch.
2. **Minimap flood fix** (`MinimapControl.axaml.cs` `UpdateViewportRect`): if the
   viewport rect covers ≥ 90% of the minimap in both axes → keep the border but set the
   fill transparent; also clamp rect to minimap bounds.
3. **"Open in Explorer"** (`MainWindow.axaml.cs`): disable the button unless
   `GraphCanvasView.SelectedNode != null`; enable in `OnCanvasSelectionChanged`.

Verify: build; run; checkbox labels and toolbar buttons readable in the current (light)
chrome; minimap shows content at 8% zoom.

### Step 2 — AppSettings persistence (~30 min)
- `UiTheme` enum, `AppSettings`, `AppSettingsService` (Load/Save, corrupt-tolerant).
- Tests: roundtrip; missing file → defaults; corrupt JSON → defaults.

Verify: `dotnet test` green.

### Step 3 — ThemeService + View → Theme menu; Domain A only (~2 h)
- `ThemeService.Apply(theme)`:
  - `System` → `RequestedThemeVariant = ThemeVariant.Default`
  - `Dark` → `ThemeVariant.Dark`; `Light` → `ThemeVariant.Light`
  - (palette/resource rewrite added in Step 4 — for now Domain A only)
- `App.axaml.cs`: load settings, `new ThemeService()`, `Apply(settings.Theme)` **before**
  `new MainWindow(...)`; pass ThemeService into MainWindow ctor (matches existing pattern).
- `MainWindow.axaml` View menu: **Theme →** three items (System / Dark / Light), radio-
  style `ToggleType="CheckBox"` synced in a `SyncThemeMenu()` (same pattern as
  `SyncViewMenuChecks`). Click handler: `ThemeService.Apply(x)` + save + re-sync.
  ⚠️ No `HotKey` on these (consistent with the existing HotKey constraint: only gestures
  with no `OnKeyDown` branch get HotKey).

Verify: run; View → Theme → Dark: **every stock control** (menus, buttons, combos,
textboxes, dialogs, context menus) turns dark instantly. Chrome hex areas (sidebar,
status bar) stay hardcoded light — *expected and fine at this step*; contrast actually
improves because the white-text buttons sit on the now-dark Fluent button backgrounds.

### Step 4 — Domain B: chrome XAML → DynamicResource (~2 h)
1. `App.axaml`: add `<Application.Resources>` with one `SolidColorBrush` per token,
   initialized with **Dark** values (table below). Keys:
   `ChromeBgBrush, ChromeBgAltBrush, ChromeBorderBrush, ChromeTextBrush,
   ChromeTextMutedBrush, StatusBarBgBrush, StatusBarTextBrush, AccentPrimaryBrush,
   AccentSecondaryBrush, AccentWarningBrush, AccentAiBrush, SelectionBrush`.
2. `MainWindow.axaml`, `SearchPanel.axaml`, `MinimapControl.axaml`: replace every hex
   from the Domain B table with `{DynamicResource XyzBrush}`. ⚠️ Must be
   **DynamicResource** (StaticResource won't update on theme swap).
3. `ThemeService.Apply` now also rewrites each resource:
   `Application.Current.Resources["ChromeBgBrush"] = palette.ChromeBgBrush;` etc.
4. `UpdateModeUi()` mode-toggle brushes: replace `Brush.Parse` with palette fields
   (`UiPalette.Current.ModeActiveBrush` / `ModeInactiveBrush`) — or better, drop the
   code-behind recolor entirely and give the buttons a `Classes="active"` style.
   Simplest acceptable: palette fields.

Chrome token values:

| Token | Dark | Light |
|---|---|---|
| ChromeBg | `#1E2329` | `#F0F0F0` |
| ChromeBgAlt (panels/cards) | `#252B33` | `#FFFFFF` |
| ChromeBorder | `#3A4250` | `#D0D0D0` |
| ChromeText | `#E0E6EC` | `#1A1A1A` |
| ChromeTextMuted | `#889098` | `#666666` |
| StatusBarBg | `#15191E` | `#E4E7EA` |
| StatusBarText | `#889098` | `#555555` |
| Accents (primary/secondary/warning/ai/selection) | unchanged in both: `#4CAF50 #2196F3 #FF9800 #9C27B0 #FF8C00` | same |

Verify: flip theme at runtime — sidebar/inspector/toolbar/status bar follow. No stale
light panels in Dark. SearchPanel no longer clashes.

### Step 5 — Domain C: Skia RenderPalette (~2 h)
1. `RenderPalette`: fields mirroring the Domain C constants above. `Dark` = current
   values (screenshot look). `Light` = the designed values below — **not** inversions:

| Render token | Dark | Light |
|---|---|---|
| CanvasBg | `#1E2329` | `#F7F8FA` |
| ClusterFill | `#252A32` | `#EDEFF3` |
| ClusterStroke | `#3A4250` | `#C8CFD8` |
| ClusterLabel | `#708090` | `#6B7684` |
| NodeFill | `#2D333F` | `#FFFFFF` |
| NodeStroke | `#3A4250` | `#B8C0C8` |
| NodeTitleText | `#E0E6EC` | `#1A1A1A` |
| NodeMemberText | `#889098` | `#5A6470` |
| Selection / Hover / DropTarget / Marquee | `#FF8C00 / #60A0E0 / #40B070 / #FFA040` | same |
| MinimapBg | `#15191E` | `#ECEFF1` |
| MinimapNode | `#2D333F` | `#C8D0D8` |
| MinimapEdge | `#3A4250` | `#B0B8C0` |
| MatrixHeaderBg | `#252B33` | `#E4E7EA` |
| MatrixHeaderText | `#B0B8C4` | `#3A4250` |
| MatrixCellBg / MatrixCellAlt | `#1E242C / #1A1F26` | `#FFFFFF / #F2F4F6` |
| MatrixHotCell | `#FF6040` | `#E8532F` |

2. Replace constants in `GraphCanvas.cs` (`ColorBg`), `CanvasRenderer.cs`,
   `MinimapControl.axaml.cs`, `NamespaceMatrixView.axaml.cs` with
   `RenderPalette.Current.X`. For `CanvasRenderer`'s cached `SKPaint` statics: convert to
   instance members rebuilt on theme change, or re-set `.Color` on the existing static
   paints in a `ReloadFromPalette()` called from `ThemeChanged`.
3. MainWindow subscribes `ThemeService.ThemeChanged` → `GraphCanvasView.ForceRedraw()`,
   re-run `MinimapView.SetGraph(...)`, `MatrixView.SetGraph(...)` if visible.
   ⚠️ Set `RenderPalette.Current` BEFORE raising `ThemeChanged`.

Verify: flip to Light with a scanned graph — canvas, minimap, matrix all light and
readable; selection/hover accents unchanged; user edge colors untouched.

### Step 6 — Dedupe + hierarchy fixes (evaluation Batches 2–3, ~2 h)
1. Remove toolbar **Fit** (canvas overlay stays).
2. Remove toolbar **D1–D3** (sidebar Focus Depth stays; toolbar space returns).
3. Remove sidebar **Scan** duplication → keep sidebar Scan next to the folder box, remove
   the toolbar one (F5/menu remain global paths).
4. Namespace depth buttons: prefix label "Depth:" or switch to a 3-item combo; no bare
   identical D-buttons.
5. Mode toggle: neutral segmented look (both buttons `ChromeBgAlt`; active gets
   `AccentPrimary` border or text, not two competing greens).
6. Move "Code → Clipboard (Ctrl+Shift+C)" text into the status bar (right side, before
   the zoom readout).
7. Sidebar sections → `Expander` (Focus / Filters / Search), Classes list takes remaining
   height.
8. Analyze-inspector empty state: show graph summary + hints instead of empty
   Members/Outgoing/Incoming headers (mirror `InspectorNothingState`).

Verify: single Scan, single Fit, single focus-depth control; sidebar fits on 1080p
without scrolling; status bar shows the code-output target.

### Step 7 (optional) — Settings UI + polish
- "Application" section in `SettingsWindow` with a Theme combo (app-level; saves via
  AppSettingsService, not ProjectSettings).
- Disabled menu items get reason tooltips ("Design Mode only").
- Theme change also flips `InlineEditTextBox` colors via tokens.

---

## 3. Pitfalls & constraints (learned from the code — read before editing)

1. **`SettingsService.SaveSettings` no-ops on empty `SourceFolderPath`** — app-global
   settings MUST use the new `AppSettingsService`, never the per-project one.
2. **`MenuItem.HotKey` double-dispatch**: only gestures without an `OnKeyDown` branch may
   use `HotKey`. Everything else displays gestures via the existing custom Grid headers.
3. **`_initialized` guard**: XAML-fired handlers run during `InitializeComponent`; new
   sync helpers must early-return when `_initialized == false` (pattern exists in
   `SyncViewMenuChecks`).
4. **DynamicResource, not StaticResource** — static lookups freeze at load.
5. **Two color types**: Avalonia `IBrush` (chrome) vs `SKColor` (Skia). Keep two palette
   classes; do not try to unify.
6. **`SKPaint` caching in `CanvasRenderer`** (docs/Done/10 added caching deliberately) —
   re-color the cached paints; don't recreate them per frame.
7. **Order at startup**: `ThemeService.Apply` before `MainWindow` is constructed so the
   first frame is already themed (avoids a light→dark flash).
8. **User colors are sacred**: `EdgeStyleSettings`, stereotype badge `ColorHex`. The
   palette never overrides them.
9. **`canvas.Clear(ColorBg)` runs twice per frame path** (`GraphCanvas.cs` ~398 and ~643,
   partial-redraw path) — both must use the palette.
10. **Don't add MVVM/Rx** — the codebase is deliberately code-behind + manual services.
    Follow it (see `docs/Done/16-introduce-dependency-injection.md` — services are
    constructor-injected by hand in `App.axaml.cs`).

---

## 4. Will it be good? (honest assessment)

**Yes — if executed in layers, and the layering is the important part.**

- **Step 3 alone (~2 h) delivers ~70% of the perceived win.** `RequestedThemeVariant`
  re-themes every stock control — menus, buttons, combos, textboxes, dialogs, context
  menus, scrollbars — in one property change. That's the part users actually notice.
- Steps 4–5 are mechanical but wide (~8 files, ~60 color sites). Each individual change
  is trivial; the risk is missing a spot, not breaking logic. The color inventory in §0
  is the checklist — if a hex from the table remains after Step 5, it's a miss.
- The Light canvas needs the **designed** palette in Step 5 (provided), not color
  inversion — inverted dark themes look terrible.
- What this will NOT fix: the graph hairball (layout/density), the sidebar's structural
  length (Step 6's Expanders address it), the deferred status-bar segments.
- Total estimate: **~1 working day** including tests. Steps 1–3 are shippable alone;
  Steps 4–6 are incremental polish on top.

The trap to avoid: trying to make one "perfect" PR. Ship Step 1 (legibility) and Step 3
(Fluent variant) independently — the app already looks 80% fixed at that point.
