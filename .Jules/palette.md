## 2024-08-07 - Add tooltips to icon-only buttons
**Learning:** Found an accessibility issue pattern in MainWindow.axaml where several icon-only buttons (like "+", "−", "×", "→") lacked tooltips. This is critical for screen reader users and helps clarify intent for mouse users.
**Action:** Always check `Button` elements with short or symbol-based `Content` and add `ToolTip.Tip` properties to ensure accessibility in Avalonia UI applications.
