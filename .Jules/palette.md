## 2024-05-18 - Added Tooltips and ARIA Labels to Icon-Only Buttons
**Learning:** Icon-only buttons in Avalonia DataTemplates (like those in dynamic lists for members and relationships) are completely invisible to screen readers without AutomationProperties.Name, and confusing for sighted users without ToolTip.Tip.
**Action:** Always add ToolTip.Tip and AutomationProperties.Name to any button that uses symbols (like '×', '→', or '+') as its content.
