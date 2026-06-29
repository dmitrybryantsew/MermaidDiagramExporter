# 10 — Inspector Panel and Relation UX (Design Mode)

> **Status**: Written against the shipped code on `feature/design-mode`
> (latest commit `2d04e65`). All references verified against the source.
> Companion to `09-bugs-and-architecture-findings.md` — this doc covers the
> UI redesign that fixes GAP-1 (missing inspector) and builds on the
> BUG-1/BUG-2/BUG-3 fixes from doc 09.

This document covers two related UI surfaces:

1. **Inspector panel** (right column) — selection-driven property editor
2. **Relation UX** — how the user creates, sees, and manages edges between
   classes, because the current "drag from port to port" flow is invisible
   to users who don't know to try it

The goal: make Design Mode feel like a real authoring tool (draw.io,
Lucidchart, StarUML style) instead of a canvas where you can only add
rectangles.

---

## Why this matters

User reports: "no fields to connect." The user expects to be able to:

- Click a class and see/edit its members in a side panel
- Change a class's kind (Class/Interface/Enum/Struct) after creation
- Change a class's namespace after creation
- See which classes a selected class connects to (outgoing + incoming)
- Click on a relation to jump to the other class
- Change an edge's kind (Association/Inheritance/Implements) after creation

None of this is currently possible because:

1. The inspector panel exists in `MainWindow.axaml:203-225` but has static
   placeholder text (`InspectorNodeText` is empty, `InspectorMembersList`
   has no binding source)
2. `DesignCanvasController.SelectionChanged` has no subscriber
   (doc 09 GAP-1)
3. No `ChangeClassKind` or `ChangeNamespace` operations exist on
   `DesignCanvasController`, even though `DesignClass.Kind` and
   `DesignClass.Namespace` are settable fields
4. No outgoing/incoming relation view exists
5. No inline edge-kind change exists

This doc fixes all five.

---

## Inspector panel architecture

### Location and structure

The inspector panel already exists at `MainWindow.axaml:203-225` (right
column, Grid.Column="4", 300px wide). It's a `ScrollViewer` containing
a `Grid` with `RowDefinitions="Auto,Auto,Auto,Auto"`.

The shipped structure has:
- Row 0: "Inspector" header
- Row 1: Node Details (single `TextBlock` named `InspectorNodeText`)
- Row 2: Members (`ItemsControl` named `InspectorMembersList`)
- Row 3: (empty)

The redesign replaces this with a **selection-driven view model** that
swaps between four states based on `DesignCanvasController.Selection`:

| State | When | Content |
|-------|------|---------|
| **Nothing** | 0 classes selected | Summary stats + "unnamed classes" warning |
| **Single class** | Exactly 1 class selected | Class properties + members + relations |
| **Single member** | 1 class selected AND member right-clicked | Class state + member expanded inline |
| **Multi-select** | 2+ classes selected | Bulk operations (gated on GAP-2 fix) |

### Selection-driven view model

Create a new file `src/MermaidDiagramExporter.Gui/Design/DesignInspectorViewModel.cs`:

```csharp
public sealed class DesignInspectorViewModel : INotifyPropertyChanged
{
    private DesignSelection _selection;
    private DesignGraph? _graph;

    public DesignInspectorViewModel(DesignCanvasController controller, DesignGraph graph)
    {
        _graph = graph;
        controller.SelectionChanged += (_, sel) => { _selection = sel; RaiseAll(); };
    }

    public string StateKind => _selection.SelectedClassIds.Count switch
    {
        0 => "Nothing",
        1 => "SingleClass",
        _ => "MultiSelect"
    };

    public DesignClass? SelectedClass => _selection.SelectedClassIds.Count == 1
        ? _graph.Classes.FirstOrDefault(c => c.Id == _selection.SelectedClassIds[0])
        : null;

    public IReadOnlyList<DesignEdge> OutgoingEdges =>
        SelectedClass == null ? Array.Empty<DesignEdge>()
        : _graph.Edges.Where(e => e.FromClassId == SelectedClass.Id).ToList();

    public IReadOnlyList<DesignEdge> IncomingEdges =>
        SelectedClass == null ? Array.Empty<DesignEdge>()
        : _graph.Edges.Where(e => e.ToClassId == SelectedClass.Id).ToList();

    public IReadOnlyList<DesignClass> UnnamedClasses =>
        _graph.Classes.Where(c => string.IsNullOrWhiteSpace(c.Name)).ToList();

    // INotifyPropertyChanged boilerplate
    public event PropertyChangedEventHandler? PropertyChanged;
    private void RaiseAll() { /* raise StateKind, SelectedClass, etc. */ }
}
```

Subscribe in `MainWindow` constructor:

```csharp
_designCanvasController.SelectionChanged += (_, _) =>
{
    _inspectorVm = new DesignInspectorViewModel(_designCanvasController, _designGraph);
    InspectorPanel.DataContext = _inspectorVm;
};
```

### Why a view model, not direct binding to `DesignClass`

`DesignClass` is a mutable POCO. Binding directly to it means Avalonia
re-renders on every property change, but the inspector also needs to
respond to selection changes (which are on the controller, not the
class). A view model mediates between selection events and class property
changes, and keeps the rendering logic in one place.

---

## State 1: Nothing selected

When the user clicks empty canvas (or hasn't selected anything), show:

```
┌─────────────────────────────────────┐
│ Inspector                           │
├─────────────────────────────────────┤
│ Design Summary                      │
│                                     │
│ Classes: 5                          │
│ Edges: 3                            │
│                                     │
│ ⚠ 2 unnamed classes                 │
│ (click a class to edit)              │
│                                     │
│ Click a class to edit its properties │
└─────────────────────────────────────┘
```

The "unnamed classes" warning is the direct fix for the silent failure
mode where users add classes via right-click "Add Class Here" and forget
to rename them. Currently there's no way to tell which classes need
attention.

### Implementation

```xml
<StackPanel IsVisible="{Binding StateKind, Converter={StaticResource EqualsConverter}, ConverterParameter=Nothing}">
  <TextBlock Text="Design Summary" FontWeight="Bold"/>
  <TextBlock Text="{Binding Graph.Classes.Count, StringFormat='Classes: {0}'}"/>
  <TextBlock Text="{Binding Graph.Edges.Count, StringFormat='Edges: {0}'}"/>
  <TextBlock Text="{Binding UnnamedClasses.Count, StringFormat='⚠ {0} unnamed classes'}"
             IsVisible="{Binding UnnamedClasses.Count, Converter={StaticResource GreaterThanZeroConverter}}"/>
  <TextBlock Text="Click a class to edit its properties" Opacity="0.6"/>
</StackPanel>
```

---

## State 2: Single class selected (the main state)

```
┌─────────────────────────────────────┐
│ Class: Animal                       │
├─────────────────────────────────────┤
│ Name    [Animal________________]    │
│ Kind    [Class      ▼]              │
│ Namespace [Zoo_____________]        │
│                                     │
│ Members                       [+ ▼] │
│ ┌─────────────────────────────────┐ │
│ │ + Name : string           [×]  │ │ ← click to edit inline, × to delete
│ │ + Speak() : void          [×]  │ │ ← drag handle on left for reorder
│ │ + Eat(food : string) : void[×] │ │
│ │ ...                             │ │
│ └─────────────────────────────────┘ │
│                                     │
│ Outgoing Relations (2)              │
│ • Dog ──inheritance──> Animal  [→]  │ ← click to jump to Dog
│ • Bird ──association──> Animal [→]  │ ← click → to change kind
│                                     │
│ Incoming Relations (1)              │
│ • Zoo ──association──> Animal  [→]  │
└─────────────────────��───────────────┘
```

Each section is the **direct fix** for specific user pain points:

- **Name field**: replaces the double-click-to-rename flow with a
  always-visible editor. Still committed via `DesignCommands.RenameClass`
  on focus loss (not on every keystroke — that would flood the undo stack)
- **Kind dropdown**: the missing ClassKind change operation
- **Namespace field**: the missing Namespace change operation
- **Members list with [×]**: replaces the context-menu-only flow with
  always-visible delete buttons
- **Drag handle on left**: replaces the context-menu-only reorder with
  drag-to-reorder
- **Outgoing/Incoming lists**: the "fields to connect" answer — user
  sees exactly what this class connects to and can jump to the other end
- **[→] on relations**: inline kind change (Association → Inheritance)
  via `DesignCommands.ChangeEdgeType`

### Implementation: class properties

```xml
<StackPanel IsVisible="{Binding StateKind, Converter={StaticResource EqualsConverter}, ConverterParameter=SingleClass}">
  <TextBlock Text="Class:" FontWeight="Bold"/>
  <TextBlock Text="{Binding SelectedClass.Name}" FontSize="14" FontWeight="SemiBold"/>

  <Grid Margin="0,8,0,0">
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="80"/>
      <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <Grid.RowDefinitions>
      <RowDefinition/><RowDefinition/><RowDefinition/>
    </Grid.RowDefinitions>

    <TextBlock Grid.Row="0" Grid.Column="0" Text="Name" VerticalAlignment="Center"/>
    <TextBox  Grid.Row="0" Grid.Column="1" Text="{Binding SelectedClass.Name, Mode=TwoWay}"/>

    <TextBlock Grid.Row="1" Grid.Column="0" Text="Kind" VerticalAlignment="Center"/>
    <ComboBox  Grid.Row="1" Grid.Column="1" SelectedValue="{Binding SelectedClass.Kind, Mode=TwoWay}">
      <ComboBoxItem Content="Class"/>
      <ComboBoxItem Content="Interface"/>
      <ComboBoxItem Content="Enum"/>
      <ComboBoxItem Content="Struct"/>
      <ComboBoxItem Content="Static Class"/>
      <ComboBoxItem Content="Abstract Class"/>
    </ComboBox>

    <TextBlock Grid.Row="2" Grid.Column="0" Text="Namespace" VerticalAlignment="Center"/>
    <TextBox  Grid.Row="2" Grid.Column="1" Text="{Binding SelectedClass.Namespace, Mode=TwoWay}"/>
  </Grid>
</StackPanel>
```

### WARNING: do NOT replicate BUG-2

The `TwoWay` binding above will mutate `SelectedClass.Name`, `Kind`, and
`Namespace` directly when the user types. **This is exactly BUG-2** from
doc 09 — direct mutation bypasses the undo system and auto-save dirty
flag.

The fix: every mutation must go through `ExecuteCommand`:

```csharp
// Subscribe to TextBox.PropertyChanged (LostFocus)
nameTextBox.LostFocus += (_, _) =>
{
    if (nameTextBox.Text == vm.SelectedClass?.Name) return;
    var cmd = new DesignCommands.RenameClass(
        vm.SelectedClass.Id,
        oldName: vm.SelectedClass.Name,
        newName: nameTextBox.Text);
    _designCanvasController.ExecuteCommand(cmd, _designGraph);
    RenderDesignModeGraph();
};

// Subscribe to ComboBox.SelectionChanged
kindComboBox.SelectionChanged += (_, _) =>
{
    if (kindComboBox.SelectedValue == vm.SelectedClass?.Kind) return;
    var cmd = new DesignCommands.ChangeClassKind(
        vm.SelectedClass.Id,
        oldKind: vm.SelectedClass.Kind,
        newKind: (ClassKind)kindComboBox.SelectedValue);
    _designCanvasController.ExecuteCommand(cmd, _designGraph);
};
```

This requires adding two new operations to `DesignCanvasController` (and
corresponding `DesignCommands` classes) — see "Missing operations" below.

### Implementation: members list

```xml
<ItemsControl ItemsSource="{Binding SelectedClass.Members}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
    <Grid ColumnDefinitions="Auto,*,Auto" Margin="0,2">
      <!-- Drag handle (left) -->
      <TextBlock Grid.Column="0" Text="⋮⋮" Margin="2,0" Opacity="0.5"
                 PointerPressed="OnMemberDragHandlePressed"/>
      <!-- Member text (middle, click to edit inline) -->
      <TextBlock Grid.Column="1" Text="{Binding DisplayText}"
                 FontFamily="Consolas" FontSize="11"
                 PointerPressed="OnMemberInlineEdit"/>
      <!-- Delete button (right) -->
      <Button Grid.Column="2" Content="×" MinWidth="24"
              Click="OnMemberDeleteClick"/>
    </Grid>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

The drag handle uses Avalonia's drag-and-drop API. The inline edit
reuses the same TextBox overlay pattern from the class rename feature
(commit `1091ec2`).

### Implementation: relations lists

```xml
<ItemsControl ItemsSource="{Binding OutgoingEdges}">
  <ItemsControl.ItemTemplate>
    <DataTemplate>
    <Grid ColumnDefinitions="*,Auto,Auto">
      <TextBlock Grid.Column="0" Text="{Binding DisplayText}" FontSize="11"/>
      <ComboBox Grid.Column="1" SelectedValue="{Binding Kind, Mode=TwoWay}"
                SelectionChanged="OnEdgeKindChanged">
        <ComboBoxItem Content="Association"/>
        <ComboBoxItem Content="Inheritance"/>
        <ComboBoxItem Content="Implements"/>
      </ComboBox>
      <Button Grid.Column="2" Content="→" Click="OnEdgeJumpClick"/>
    </Grid>
    </DataTemplate>
  </ItemsControl.ItemTemplate>
</ItemsControl>
```

The `→` button calls `_designCanvasController.Select(edge.ToClassId)` to
jump to the other class. The kind ComboBox uses the same
`ExecuteCommand` pattern as the class kind dropdown.

---

## State 3: Single member selected (optional refinement)

When the user right-clicks a member row in the inspector (or
double-clicks it), the inspector expands that member inline:

```
┌─────────────────────────────────────┐
│ Class: Animal                       │
├─────────────────────────────────────┤
│ ... (class properties collapsed)    │
│                                     │
│ Members                       [+ ▼] │
│ ┌─────────────────────────────────┐ │
│ │ Name    [Name_________________] │ │ ← edit member name
│ │ Type    [string_______________] │ │ ← edit member type
│ │ Vis     [Public ▼]              │ │ ← cycle visibility
│ │ Kind    [Field ▼]               │ │ ← change kind
│ │           [Save] [Cancel] [×]    │ │
│ └─────────────────────────────────┘ │
└─────────────────────────────────────┘
```

This is **optional** for v1 — the context menu already covers member
edit/delete/cycle-visibility. If skipped, the inspector stays simpler
and only shows State 1, 2, and 4.

### Implementation

Only build this if the inspector proves too cramped for member editing
in practice. Defer until user feedback says it's needed.

---

## State 4: Multi-select (gated on GAP-2 fix)

```
┌─────────────────────────────────────┐
│ 3 classes selected                  │
├─────────────────────────────────────┤
│ [Apply Layout] [Align ▼]            │
│                                     │
│ Bulk operations:                     │
│ • Move to namespace: [____] [Apply] │
│ • Change kind:       [▼] [Apply]    │
│ • Delete all                       │
│                                     │
│ Selected:                            │
│ • Animal                             │
│ • Dog                                │
│ • Bird                               │
└─────────────────────────────────────┘
```

This state is **blocked by GAP-2** (doc 09). The fix order from doc 09
puts GAP-2 after GAP-1, but the inspector should be designed to support
this state from the start — just don't enable it until GAP-2 lands.

### Implementation

```xml
<StackPanel IsVisible="{Binding StateKind, Converter={StaticResource EqualsConverter}, ConverterParameter=MultiSelect}">
  <TextBlock Text="{Binding Selection.SelectedClassIds.Count, StringFormat='{}{0} classes selected'}"/>
  <Button Content="Apply Layout" Click="OnBulkApplyLayout"/>
  <Button Content="Delete All" Click="OnBulkDelete"/>
  <ItemsControl ItemsSource="{Binding SelectedClasses}">
    <ItemsControl.ItemTemplate>
      <DataTemplate>
        <TextBlock Text="{Binding Name}" Margin="4,1"/>
      </DataTemplate>
    </ItemsControl.ItemTemplate>
  </ItemsControl>
</StackPanel>
```

---

## Missing operations (must add to `DesignCanvasController`)

The data model supports these (all fields are settable) but no operations
exist. Add them:

### `ChangeClassKind`

```csharp
// In DesignCanvasController
public bool ChangeClassKind(DesignGraph graph, string classId, ClassKind newKind)
{
    var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
    if (cls == null) return false;
    if (cls.Kind == newKind) return false;
    var cmd = new DesignCommands.ChangeClassKind(classId, cls.Kind, newKind);
    UndoManager.Execute(cmd, graph);
    GraphMutated?.Invoke(this, EventArgs.Empty);
    return true;
}
```

```csharp
// In DesignCommands
public sealed class ChangeClassKind : DesignCommand
{
    private readonly string _classId;
    private readonly ClassKind _oldKind, _newKind;
    public ChangeClassKind(string classId, ClassKind oldKind, ClassKind newKind)
    {
        _classId = classId; _oldKind = oldKind; _newKind = newKind;
    }
    public override string Description => $"Change class kind";
    public override void Apply(DesignGraph graph)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
        if (cls != null) cls.Kind = _newKind;
    }
    public override void Undo(DesignGraph graph)
    {
        var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
        if (cls != null) cls.Kind = _oldKind;
    }
}
```

### `ChangeNamespace`

Same pattern. Captures old/new namespace, applies/undoes on `cls.Namespace`.

### `BulkDelete`

For multi-select. Captures the list of class IDs and their full state at
construction time (deep copy, same fix as `RemoveClass` in M6).

### `MoveMember` (already exists in M3)

Verify the existing `MoveMember` works with drag-and-drop on the
inspector list. If not, add a `MoveMemberBy` variant.

### `ChangeMemberName` and `ChangeMemberType`

Already exist in M3 (`RenameMember` and `ChangeMemberType`). Verify they
work via the inspector (not just the context menu).

---

## Relation UX: beyond the inspector

The inspector shows outgoing/incoming relations, but creating relations
still requires the user to know about the invisible edge ports. Three
improvements:

### 1. Visible edge creation hint

When the user hovers over a class with the Alt key held, highlight the
edge ports (currently invisible). This makes the drag-from-port flow
discoverable without adding new UI.

### 2. Relations panel summary

In the "Nothing selected" state, add a "Recent Relations" section that
shows the last 5 edges created, with click-to-jump. This helps users
verify their work without selecting each class individually.

### 3. Edge type picker on creation

Currently `AddEdge` defaults to `EdgeKind.Association`. After the user
drags from port to port, show a small floating picker near the new edge
asking "What kind?" (Association/Inheritance/Implements/Dependency/
Aggregation/Composition). One click to confirm, Escape to cancel.

The picker is a `Popup` with `PlacementMode = Pointer` so it appears
where the user released the mouse.

---

## Wiring instructions (step-by-step)

1. **Add missing operations** to `DesignCanvasController`:
   - `ChangeClassKind(graph, classId, newKind)`
   - `ChangeNamespace(graph, classId, newNamespace)`
   - `BulkDelete(graph, IEnumerable<string> classIds)`
   - `Select(string classId)` (public version of the private Select method)

2. **Add corresponding commands** to `DesignCommands`:
   - `ChangeClassKind`
   - `ChangeNamespace`
   - `BulkDelete` (with deep-copy snapshot like `RemoveClass`)

3. **Create `DesignInspectorViewModel`** in `Design/DesignInspectorViewModel.cs`
   (see code above)

4. **Replace inspector XAML** in `MainWindow.axaml:203-225` with the
   state-driven layout (four `StackPanel`s, each `IsVisible` bound to
   `StateKind` equality)

5. **Subscribe to selection** in `MainWindow` constructor (after the
   existing `GraphCanvasView.SelectionChanged` subscription):
   ```csharp
   _designCanvasController.SelectionChanged += (_, _) =>
   {
       _inspectorVm = new DesignInspectorViewModel(_designCanvasController, _designGraph);
       InspectorPanel.DataContext = _inspectorVm;
   };
   ```

6. **Wire inspector mutations** through `ExecuteCommand` — NEVER direct
   mutation (BUG-2 lesson). Every `TextBox.LostFocus` and
   `ComboBox.SelectionChanged` must go through the undo system.

7. **Add value converters** for the `IsVisible` bindings:
   - `EqualsConverter` (string equality)
   - `GreaterThanZeroConverter` (int > 0)

8. **Test** with at least 5 unit tests covering:
   - Inspector shows correct state for each selection scenario
   - All mutations go through `ExecuteCommand` (verify by checking undo stack)
   - `ChangeClassKind` / `ChangeNamespace` undo correctly

---

## Acceptance criteria

### Inspector panel

- [ ] Click empty canvas → shows "Nothing selected" state with summary
- [ ] Click a class → shows its name, kind, namespace, members, relations
- [ ] Edit name in inspector → Ctrl+Z reverts
- [ ] Change kind dropdown → Ctrl+Z reverts
- [ ] Change namespace → Ctrl+Z reverts
- [ ] Delete a member via [×] → Ctrl+Z restores
- [ ] Click [→] on outgoing relation → that class becomes selected
- [ ] Change edge kind in relations list → Ctrl+Z reverts
- [ ] Shift+click two classes → multi-select state appears (after GAP-2 fix)
- [ ] Inspector never bypasses undo (verified by code review + tests)

### Relation UX

- [ ] Alt+hover on a class shows edge ports
- [ ] Drag from port to port + choose kind → edge appears with chosen kind
- [ ] Recent Relations section shows last 5 edges
- [ ] No regression: existing drag-from-port flow still works

### Functional gap (ClassKind/Namespace)

- [ ] Can change ClassKind after class creation (was impossible before)
- [ ] Can change Namespace after class creation (was impossible before)
- [ ] Both changes undoable via Ctrl+Z

---

## What this doc does NOT cover

- **Drag-to-reorder members on canvas** — members are shown as text rows
  in the inspector; reorder happens via drag handles in the list, not
  on the canvas. (Canvas member reorder is a separate feature.)
- **Visual edge kind indicators** — the canvas already draws different
  arrow styles per `EdgeKind`. This doc doesn't change that.
- **Bulk undo** — undoing a multi-select delete undoes the whole bulk
  operation as one step (via `BulkDelete` command). Per-class undo is
  not supported.

---

## Related docs

- `09-bugs-and-architecture-findings.md` — BUG-2 must be fixed before this
  panel is built (the panel will replicate BUG-2's anti-pattern if not
  careful)
- `05-data-model-and-persistence.md` — `DesignClass.Kind` and
  `DesignClass.Namespace` are settable fields; the missing operations
  are the only gap
- `07-implementation-phases.md` M3 — existing member operations that
  this panel reuses
- `08-risks-and-decisions.md` D6 — Design Mode positions are authoritative
  (relevant for the inspector showing positions in State 2)
