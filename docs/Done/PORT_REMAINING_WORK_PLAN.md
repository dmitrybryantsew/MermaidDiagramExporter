# MermaidClassDiagramExporter Port Remaining Work

This document lists the remaining work to bring `MermaidDiagramExporter` closer to feature parity with the Unity plugin at:

`/media/sf_importedUnityPackagesToAIWith/MermaidClassDiagramExporter-main/Assets/Plugins/MermaidClassDiagramExporter`

The standalone app already has:

- Core graph model in `src/MermaidDiagramExporter/Core/TypeGraphModels.cs`
- Roslyn source scanner in `src/MermaidDiagramExporter/Extraction/RoslynTypeScanner.cs`
- Mermaid export in `src/MermaidDiagramExporter/Export/MermaidGraphExporter.cs`
- Basic Avalonia GUI in `src/MermaidDiagramExporter.Gui`
- Skia graph canvas with pan/zoom in `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`
- Initial port of the Unity focus subsystem in `src/MermaidDiagramExporter/Focus`

## Priority 1: GUI Controls For Focus System

The focus engine is now available, but the GUI only uses fixed depth `1` and `UndirectedAssociations`.

Add controls equivalent to the Unity viewer:

- Focus depth buttons: `D1`, `D2`, `D3`
- Traversal selector:
  - `UndirectedAssociations`
  - `OutgoingAssociationsOnly`
  - `IncomingAssociationsOnly`
  - `AllVisibleRelations`
- Seed controls:
  - Add selected node as seed
  - Remove selected seed
  - Clear seeds
  - Focus current seed set

Main files:

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`
- `src/MermaidDiagramExporter/Focus/GraphSeedSelectionState.cs`
- `src/MermaidDiagramExporter/Focus/FocusedGraphNavigationController.cs`

Implementation notes:

- Add fields in `MainWindow.axaml.cs`:
  - `GraphFocusTraversalMode _currentTraversalMode = GraphFocusTraversalMode.UndirectedAssociations;`
  - `GraphSeedSelectionState _seedSelectionState = new();`
  - `int _focusDepth = 1;`
- When focusing, call:
  - `_focusNavigationController.FocusSelection(seedIds, _focusDepth, _currentTraversalMode)`
- If no seeds are selected, use the currently selected node ID.
- Update `StatsText` to show focus depth, traversal mode, and seed count.
- Keep Back/Forward/Reset wired through `FocusedGraphNavigationController`.

Acceptance checks:

- Selecting a class and pressing `D1` shows immediate association neighbors.
- Pressing `D2` includes neighbors two association hops away.
- Outgoing mode only follows association edges from selected node to target nodes.
- Incoming mode only follows association edges pointing into selected node.
- Back and Forward restore previous focused graphs.
- Reset returns to the full scanned graph.

## Priority 2: Search And Highlighting

The Unity plugin has a toolbar search field. The Avalonia GUI does not.

Add:

- Search textbox in the sidebar or top toolbar.
- Matching by `DisplayName`, `FullName`, and namespace.
- Visual highlight for matching nodes.
- Optional: dim non-matching nodes instead of hiding them.

Main files:

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`
- `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`

Implementation notes:

- Add `SearchTextBox` with `TextChanged`.
- Add `GraphCanvas.SetSearchText(string text)`.
- In `GraphCanvas`, store `_searchText`.
- In `DrawNodes`, if search is active:
  - Matching nodes get a brighter stroke or fill.
  - Non-matching nodes can be drawn normally at first. Do not hide nodes in the first pass.
- Do not trigger graph relayout on search changes.

Acceptance checks:

- Typing a class name highlights matching nodes.
- Clearing the search restores normal rendering.
- Search does not reset pan/zoom.

## Priority 3: Edge Type Visibility Filters

The Unity viewer can toggle inheritance, implements, and association edges independently.

Add checkboxes/toggles:

- Inheritance
- Implements
- Associations

Main files:

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`
- `src/MermaidDiagramExporter.Gui/LayoutEngine.cs`
- `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`

Implementation notes:

- `GraphEdge` currently only has `IsStrongRelation`. Add:
  - `TypeEdgeKind Kind`
  - `string Label`
- In `LayoutEngine.Layout`, copy `e.Kind` and `e.Label` into `GraphEdge`.
- Add `GraphCanvas.SetEdgeVisibility(bool inheritance, bool implements, bool associations)`.
- In `DrawEdges`, skip hidden edge kinds.
- Use different colors/markers for inheritance vs implements if possible.

Acceptance checks:

- Turning off associations hides only association edges.
- Turning off inheritance hides only inheritance edges.
- Turning off implements hides only interface implementation edges.
- Toggling filters does not relayout the graph and does not reset pan/zoom.

## Priority 4: Inspector Panel

The current selected-node panel only shows name, kind, and file.

Port the Unity inspector behavior:

- Graph summary
- Selected node details
- Members list
- Outgoing relations
- Incoming relations
- Open script button

Source reference:

- Unity: `Editor/Viewer/TypeGraphInspectorView.cs`

Main files:

- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`

Implementation notes:

- Keep it simple in Avalonia first. Use a right-side panel or expand the existing sidebar.
- Preserve `TypeGraph? _currentGraph` for relation lookup.
- For outgoing relations:
  - `graph.Edges.Where(e => e.FromNodeId == selectedNode.Id)`
- For incoming relations:
  - `graph.Edges.Where(e => e.ToNodeId == selectedNode.Id)`
- Resolve related node labels through `graph.Nodes`.

Acceptance checks:

- Clicking a node shows all visible members.
- Incoming/outgoing relation lists match the currently displayed focused graph.
- Resetting to root updates the inspector relation lists.

## Priority 5: Full Layout Pipeline Port

The current standalone layout port is much smaller than the Unity plugin.

Unity coordinator:

- `Editor/Layout/GraphLayoutCoordinator.cs`
- Runs preparation passes.
- Runs layered or fallback layout.
- Runs post-layout passes.
- Routes edge paths.

Current standalone coordinator:

- `src/MermaidDiagramExporter.Gui/Layout/GraphLayoutCoordinator.cs`
- Only creates `LayoutGraph` and runs `LayeredLayoutEngine`.

Missing Unity files/classes to port:

- `IGraphLayoutEngine.cs`
- `ILayoutPass.cs`
- `IPostLayoutPass.cs`
- `LayoutOptions.cs`
- `LayoutPipeline.cs`
- `PostLayoutPipeline.cs`
- `LayoutCloneUtility.cs`
- `LayoutMeasurementService.cs`
- `SimpleColumnLayoutEngine.cs`
- `ClusterBoundaryEdgeNormalizer.cs`
- `Passes/*`
- `Post/*`
- `Routing/*`

Main standalone files to update:

- `src/MermaidDiagramExporter.Gui/Layout/LayoutModels.cs`
- `src/MermaidDiagramExporter.Gui/Layout/GraphLayoutCoordinator.cs`
- `src/MermaidDiagramExporter.Gui/Layout/LayeredLayoutEngine.cs`
- `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`

Implementation notes:

- Do not copy Unity `Vector2`, `Rect`, or `Mathf` directly unless adapted. The standalone port already has replacements in `LayoutModels.cs`.
- Port one group of classes at a time:
  1. Interfaces and options.
  2. Layout models fields missing from Unity model.
  3. Clone and measurement utilities.
  4. Pipeline classes.
  5. Passes.
  6. Post passes.
  7. Edge routing.
- After `EdgeRoutingService` is ported, change `GraphCanvas` to draw routed `LayoutEdgePath` points instead of simple cubic edges.

Acceptance checks:

- Large graphs have fewer cluster overlaps.
- Namespace/group boxes include proper title spacing.
- Edges route around or clip to clusters better than current direct curves.
- Self-loop or same-cluster relationships do not collapse into unreadable lines.

## Priority 6: Export Parity

Unity export writes `.mmd` and `.md`, reveals output, copies Mermaid text to clipboard, and opens Mermaid Live Editor.

Current CLI writes only `.md`. GUI saves PNG only.

Add:

- CLI option `--format md|mmd|both`
- CLI writes `.mmd` when requested.
- GUI buttons:
  - Save Mermaid `.mmd`
  - Save Markdown `.md`
  - Copy Mermaid to clipboard
  - Open Mermaid Live Editor

Main files:

- `src/MermaidDiagramExporter/Program.cs`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml`
- `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`

Implementation notes:

- Use `MermaidGraphExporter.BuildDiagram(graph)`.
- Markdown wrapper:
  - `# {title}`
  - fenced ```mermaid block
- For clipboard in Avalonia, use TopLevel clipboard APIs if available.

Acceptance checks:

- `--format mmd` writes raw Mermaid only.
- `--format md` writes Markdown only.
- `--format both` writes both.
- GUI export uses currently displayed graph, not always root graph.

## Priority 7: Scanner Parity And Options

The standalone Roslyn scanner is better suited for a non-Unity app, but behavior differs from Unity reflection.

Known differences:

- `IncludeDeclaredMembersOnly` is effectively ignored because Roslyn `type.GetMembers()` returns declared members only.
- Unity listed public fields only; Roslyn currently includes non-private accessible fields.
- Unity could resolve loaded assembly inheritance/interfaces; Roslyn currently has limited references.
- Unity `IsMonoBehaviour` and `IsScriptableObject` became `Stereotypes`, but GUI does not display them.

Main files:

- `src/MermaidDiagramExporter/Extraction/RoslynTypeScanner.cs`
- `src/MermaidDiagramExporter/Core/TypeGraphModels.cs`
- `tests/MermaidDiagramExporter.Tests/RoslynTypeScannerTests.cs`

Recommended work:

- Decide intended member visibility behavior and document it.
- Implement inherited member collection if `IncludeDeclaredMembersOnly == false`.
- Add optional project/solution scanning:
  - Read `.csproj`
  - Add package/project references to Roslyn compilation when possible
- Improve Unity stereotype detection:
  - Walk full base type chain, not just direct base type.
  - Detect `MonoBehaviour`, `ScriptableObject`, `Component` by full metadata name when references exist.

Acceptance checks:

- Tests cover declared-only false.
- Tests cover protected/internal methods.
- Tests cover Unity stereotype detection through indirect inheritance.

## Priority 8: Test Coverage

Existing tests cover scanner/export basics and new focus behavior.

Add tests for:

- Focus multi-seed behavior.
- Focus `AllVisibleRelations`.
- Edge filter conversion in `LayoutEngine`.
- Search matching logic, if search logic is separated from rendering.
- CLI `.mmd` and `both` export formats.
- Scanner inherited members when implemented.

Commands to run:

```bash
dotnet test MermaidDiagramExporter.sln
dotnet build MermaidDiagramExporter.sln
```

If GUI changes are made, manually test:

- Scan a medium Unity project folder.
- Pan and zoom after scan.
- Focus D1/D2/D3.
- Back, Forward, Reset.
- Search.
- Edge filters.
- PNG/Mermaid/Markdown export.

