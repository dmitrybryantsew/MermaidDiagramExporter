## 2026-08-21 - Icon-Only Button Accessibility in Avalonia
**Learning:** In Avalonia UI, icon-only buttons often lack accessible names for screen readers and descriptive text for mouse hover, hindering accessibility. The ARIA label equivalent is `AutomationProperties.Name`.
**Action:** Always add both `ToolTip.Tip` (for mouse users) and `AutomationProperties.Name` (for screen readers) to any button that only contains an icon or symbol instead of text.
