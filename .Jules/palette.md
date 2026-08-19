## 2024-08-19 - Accessible ToolTips for Symbol Buttons
**Learning:** Avalonia XAML interfaces often use simple Unicode characters (like `+`, `−`, `×`, `→`) or empty content for icon buttons to save space in tight layouts (like toolbars or inspector lists). However, these are completely opaque to screen readers and offer poor discoverability for keyboard users without proper tooltips.
**Action:** Always ensure any symbol-only or icon-only `Button` element in Avalonia includes a descriptive `ToolTip.Tip` attribute explaining its function.
