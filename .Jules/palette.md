## 2024-05-18 - Missing ARIA labels in dynamic lists
**Learning:** Found several icon-only buttons lacking `ToolTip.Tip` and `AutomationProperties.Name` inside Avalonia `DataTemplate` definitions, which makes them inaccessible to screen readers traversing dynamic lists.
**Action:** When inspecting DataTemplates, always check that icon-only buttons have descriptive labels mapped correctly or provided explicitly.
