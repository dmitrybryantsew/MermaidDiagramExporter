
## 2025-02-18 - Tooltips for icon-only buttons in complex desktop UI
**Learning:** Found that small UI actions like deleting members or jumping between relationships in an inspector view often lack text labels to save space, making them inaccessible to screen readers and confusing to users.
**Action:** Consistently apply `ToolTip.Tip` with descriptive action verbs (e.g. "Delete member", "Jump to class") on all icon-only utility buttons in Avalonia apps.
