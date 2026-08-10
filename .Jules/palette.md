## 2024-05-19 - Accessible Tooltips for Icon-Only Buttons
**Learning:** In Avalonia UI, icon-only buttons (like `×` or `+`) completely lack context for screen readers and are ambiguous for mouse users if they don't have descriptive text. Unlike the web, which has `aria-label`, Avalonia depends heavily on `ToolTip.Tip` for providing this critical accessibility and UX context while keeping the UI compact.
**Action:** When adding or auditing icon-only buttons in `.axaml` files, always ensure a descriptive `ToolTip.Tip` attribute is present.
