## 2026-08-25 - Accessible Icon Buttons in Avalonia DataTemplates
**Learning:** Icon-only buttons embedded in DataTemplates (like list items for members, stereotype rules, or attached context files) lack hover context and are invisible to screen readers without specific attributes.
**Action:** Always add both `ToolTip.Tip` (for mouse hover clarity) and `AutomationProperties.Name` (for screen reader accessibility acting as ARIA label equivalent) to icon-only buttons (`<Button Content="x" />` or `<Button Content="→" />`), especially inside dynamic lists.
