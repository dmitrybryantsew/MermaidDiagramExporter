## 2024-11-20 - Accessible Icon-Only Buttons in Avalonia
**Learning:** In Avalonia UI desktop apps, standard `aria-label` HTML attributes don't work. The correct ARIA equivalent for icon-only controls to ensure screen reader compatibility is `AutomationProperties.Name`.
**Action:** Always combine `ToolTip.Tip` (for mouse hover clarity) with `AutomationProperties.Name` (for screen readers) on any icon-only buttons in `.axaml` files.
