# Agent Implementation Checklist

This checklist is written for an implementation agent that has little context. Work from top to bottom. Do not start layout pipeline work before the GUI focus controls are complete.

## Rules

- Keep changes small and testable.
- Do not rewrite unrelated files.
- Do not delete the old `FocusNavigator.cs` until the new focus UI is fully working. It is unused now, but harmless.
- Run tests after every major section if `dotnet` is available.
- If `dotnet` is not available, do source-level checks and clearly report that tests were not run.

## Step 1: Add Focus UI Controls

Files:

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`

Add controls near the existing Back/Forward/Reset row:

- Buttons: `D1`, `D2`, `D3`
- Buttons: `Add Seed`, `Remove Seed`, `Clear Seeds`
- ComboBox for traversal mode:
  - `Undirected`
  - `Outgoing`
  - `Incoming`
  - `All Visible`

In `MainWindow.axaml.cs`, add fields:

```csharp
private readonly GraphSeedSelectionState _seedSelectionState = new();
private GraphFocusTraversalMode _currentTraversalMode = GraphFocusTraversalMode.UndirectedAssociations;
private int _focusDepth = 1;
private string _currentSelectedNodeId = string.Empty;
```

Update `OnCanvasSelectionChanged`:

- Set `_currentSelectedNodeId = node?.Id ?? string.Empty;`
- Update seed button states.

Add helper:

```csharp
private IReadOnlyList<string> ResolveFocusSeedIds()
{
    if (_seedSelectionState.HasSeeds)
        return _seedSelectionState.SeedNodeIds;

    return string.IsNullOrEmpty(_currentSelectedNodeId)
        ? Array.Empty<string>()
        : new[] { _currentSelectedNodeId };
}
```

Add helper:

```csharp
private void FocusCurrentSelection(int depth)
{
    IReadOnlyList<string> seeds = ResolveFocusSeedIds();
    if (!_focusNavigationController.CanFocusSelection(seeds))
        return;

    TypeGraph? focused = _focusNavigationController.FocusSelection(seeds, depth, _currentTraversalMode);
    if (focused != null)
        SetDisplayedGraph(focused, seeds[0]);
}
```

Acceptance check:

- Clicking a node and pressing `D1` focuses it.
- Pressing `D2` includes one more association hop.

## Step 2: Add Search

Files:

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`
- `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`

Add a search `TextBox` above the class list.

In `GraphCanvas.cs`, add:

```csharp
private string _searchText = string.Empty;

public void SetSearchText(string searchText)
{
    _searchText = searchText ?? string.Empty;
    Invalidate();
}
```

In `DrawNodes`, compute:

```csharp
bool searchActive = !string.IsNullOrWhiteSpace(_searchText);
bool searchMatch =
    node.DisplayName.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
    || node.Namespace.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
    || node.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
```

If search is active and node matches, use a bright stroke color. Do not hide nodes yet.

Acceptance check:

- Search highlights matching nodes.
- Pan/zoom remains unchanged while typing.

## Step 3: Add Edge Filters

Files:

- `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`
- `src/MermaidDiagramExporter.Gui/LayoutEngine.cs`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`

Update `GraphEdge` in `GraphCanvas.cs`:

```csharp
public Core.TypeEdgeKind Kind { get; set; }
public string Label { get; set; } = "";
```

Update `LayoutEngine.Layout`:

```csharp
Kind = e.Kind,
Label = e.Label,
```

Add visibility fields to `GraphCanvas`:

```csharp
private bool _showInheritanceEdges = true;
private bool _showImplementsEdges = true;
private bool _showAssociationEdges = true;
```

Add method:

```csharp
public void SetEdgeVisibility(bool inheritance, bool implements, bool associations)
{
    _showInheritanceEdges = inheritance;
    _showImplementsEdges = implements;
    _showAssociationEdges = associations;
    Invalidate();
}
```

In `DrawEdges`, skip hidden kinds.

Acceptance check:

- Each edge type can be independently hidden and shown.

## Step 4: Add Inspector Details

Files:

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`

Replace or expand the current `SelectedNodeText` area.

Show:

- Node display name
- Namespace
- Kind
- File path
- Members
- Outgoing relations
- Incoming relations

Use `_focusNavigationController.CurrentGraph` as the source graph so the inspector matches the displayed graph.

Acceptance check:

- Clicking a node updates members and relations.
- Focused graphs show only focused graph relations.

## Step 5: Export `.mmd` And Markdown From CLI

Files:

- `src/MermaidDiagramExporter/Program.cs`
- `tests/MermaidDiagramExporter.Tests/CliAndEndToEndTests.cs`

Add CLI option:

```text
--format md|mmd|both
```

Default can remain `md` to avoid breaking existing behavior.

Implementation:

- `md`: existing Markdown output.
- `mmd`: raw Mermaid output.
- `both`: write both files.

Acceptance check:

```bash
dotnet run --project src/MermaidDiagramExporter -- <folder> -o <out> --format both
```

Expected:

- `<title>.md`
- `<title>.mmd`

## Step 6: Port Layout Pipeline

Do this only after Steps 1-5.

Source Unity files:

- `/media/sf_importedUnityPackagesToAIWith/MermaidClassDiagramExporter-main/Assets/Plugins/MermaidClassDiagramExporter/Editor/Layout`

Target:

- `src/MermaidDiagramExporter.Gui/Layout`

Order:

1. Port `IGraphLayoutEngine`, `ILayoutPass`, `IPostLayoutPass`.
2. Expand `LayoutModels.cs` to match Unity fields.
3. Port `LayoutOptions.cs`.
4. Port `LayoutCloneUtility.cs`.
5. Port `LayoutMeasurementService.cs`.
6. Port `LayoutPipeline.cs` and `PostLayoutPipeline.cs`.
7. Port `Passes/*`.
8. Port `Post/*`.
9. Port `Routing/*`.
10. Update `GraphLayoutCoordinator.cs` to match Unity coordinator.
11. Update `GraphCanvas.cs` to draw `LayoutEdgePath` routes.

Important:

- Unity uses `UnityEngine.Rect`, `UnityEngine.Vector2`, and `Mathf`.
- Standalone replacements already exist in `LayoutModels.cs`.
- Adapt syntax carefully. Do not import Unity packages.

Acceptance check:

- Build succeeds.
- Existing layout tests still pass.
- Visual graph has fewer overlapping clusters and better edge routes.

## Step 7: Scanner Improvements

Files:

- `src/MermaidDiagramExporter/Extraction/RoslynTypeScanner.cs`
- `tests/MermaidDiagramExporter.Tests/RoslynTypeScannerTests.cs`

Tasks:

- Implement inherited members when `IncludeDeclaredMembersOnly == false`.
- Decide whether non-private fields should be listed. Match Unity if strict parity is desired.
- Improve Unity stereotype detection through base-type chain.
- Consider `.csproj` or `.sln` based reference loading.

Acceptance check:

- Tests cover declared-only true and false.
- Tests cover indirect `MonoBehaviour` or `ScriptableObject` inheritance when references can be resolved.

## Step 8: Final Manual Test Script

After all changes:

1. Open GUI.
2. Scan a folder with at least 20 classes.
3. Verify pan/zoom.
4. Select a class.
5. Focus D1, D2, D3.
6. Try outgoing/incoming/all-visible traversal.
7. Add two seeds and focus.
8. Back, Forward, Reset.
9. Search for a class.
10. Toggle edge types.
11. Save PNG.
12. Export `.mmd` and `.md`.
13. Run:

```bash
dotnet test MermaidDiagramExporter.sln
dotnet build MermaidDiagramExporter.sln
```

