Here is the fix plan, written in the same style and format as the provided documentation. You can save this as `FIX_PLAN_REGRESSIONS.md`.

```markdown
# Fix Plan: Scan Hang & Drag Regression

## Why this document exists

A recent refactoring and feature addition introduced several regressions, the most severe of which causes the application to hang indefinitely when scanning a medium-to-large project. This plan provides targeted, low-risk steps to resolve the hangs, fix a visual regression during node dragging, and close a regex security gap.

## Ground rules

1. **Run the existing test suite after every step** (`dotnet test`).
2. **Commit after each numbered step**. If a step introduces a new issue, it is trivial to revert.
3. **Do not change unrelated code**. These are surgical fixes.

## Order of operations

| Phase | Steps | Why this order |
|---|---|---|
| 1. Critical Performance | 01 | The app is completely unusable on real projects until this is fixed. Address the O(N² log N) sorting loop first. |
| 2. UI Responsiveness | 02 | Even with the layout engine fixed, Roslyn scanning and file I/O block the UI thread. |
| 3. Visual Correctness | 03 | Fix the disappearing nodes during drag (Step 12 regression). |
| 4. Security Hardening | 04 | Close the ReDoS gap in the Roslyn scanner. |

---

## Step 01 — Fix O(N²) edge sorting in `LayeredLayoutEngine`

### Problem (in plain language)

When the layout engine assigns vertical ranks to nodes and clusters, it needs to process edges in a specific order (heaviest weight first). To do this, it calls `graph.Edges.OrderByDescending(...)` **inside a `for` loop** that runs up to `nodes.Count * 2` times. 

For a Unity project with 1,000 nodes and 5,000 edges, this results in re-sorting the entire edge collection thousands of times per layout pass, taking 30+ seconds and freezing the app.

### What to find

- File: `src/MermaidDiagramExporter.Gui/Layout/LayeredLayoutEngine.cs`
- Method: `AssignLocalNodeRanks`
- Method: `AssignClusterRanks`

Search for `graph.Edges.OrderByDescending` inside these methods. You will see it positioned directly inside the `foreach` loop of a `for` statement.

### The fix

Hoist the sorting logic outside the loop so it only executes once.

1. In `AssignLocalNodeRanks`, change:
   ```csharp
   for (int i = 0; i < nodes.Count * 2; i++)
   {
       bool changed = false;
       foreach (var edge in graph.Edges.OrderByDescending(e => GetEdgeWeight(e.Kind)))
       {
   ```
   To:
   ```csharp
   var orderedEdges = graph.Edges.OrderByDescending(e => GetEdgeWeight(e.Kind)).ToList();
   
   for (int i = 0; i < nodes.Count * 2; i++)
   {
       bool changed = false;
       foreach (var edge in orderedEdges)
       {
   ```

2. Apply the **exact same fix** to `AssignClusterRanks`.

### Constraints

- Do not change the sorting logic itself (still `OrderByDescending(e => GetEdgeWeight(e.Kind))`).
- Do not change the loop conditions.

### Verification

1. `dotnet build`.
2. `dotnet test`.
3. Manual trace: Confirm `orderedEdges` is evaluated and allocated exactly once per method call, not per loop iteration.

---

## Step 02 — Offload Scan and Layout to Background Thread

### Problem (in plain language)

Even with the layout engine optimized, the `MainWindow.OnScan` method runs everything synchronously on the UI thread. Roslyn compilation, disk I/O for caching, and layout calculation can take 5–10 seconds combined. During this time, the application window is completely frozen (cannot be moved, shows "Not Responding").

### What to find

- File: `src/MermaidDiagramExporter.Gui/MainWindow.axaml.cs`
- Method: `OnScan`

Find the block of code inside `OnScan` that calls `_scanCoordinator.ExecuteScanFlowAsync` and `SetDisplayedGraph`. 

### The fix

Wrap the heavy computational and I/O work in `Task.Run` so the UI thread remains free. Because `OnScan` is already `async void`, you can `await` the background task.

1. Identify the CPU-bound and I/O-bound portion of `OnScan`. It roughly looks like:
   ```csharp
   var graph = await _scanCoordinator.ExecuteScanFlowAsync(folder, PromptForCache);
   if (graph == null) return;

   _currentSettings = _settingsService.LoadSettings(graph.Metadata.SourceDescription);
   _currentGraph = graph;
   // ...
   SetDisplayedGraph(_currentGraph);
   ```

2. Move the scan and layout execution into a background task. Update the UI before and after:
   ```csharp
   // Show a loading indicator
   StatsText.Text = "Scanning...";
   IsEnabled = false; // Optional: disable the scan button to prevent double-clicks

   try
   {
       // Run the scan and layout preparation in the background
       var graph = await Task.Run(() => _scanCoordinator.ExecuteScanFlowAsync(folder, PromptForCache).Result);
       
       if (graph == null) 
       {
           IsEnabled = true;
           return;
       }

       _currentSettings = _settingsService.LoadSettings(graph.Metadata.SourceDescription);
       _currentGraph = graph;
       _focusNavigationController.SetRootGraph(_currentGraph, _currentSettings.SourceFolderPath);
       _seedSelectionState.Clear();

       // SetDisplayedGraph must run on the UI thread because it touches Avalonia controls
       SetDisplayedGraph(_currentGraph);
       GraphCanvasView.WaitForRender();
       UpdateStats(_currentGraph);

       // Auto-save screenshot (keep on UI thread or use Task.Run for file I/O)
       var dir = Path.Combine(AppContext.BaseDirectory, "export");
       Directory.CreateDirectory(dir);
       var path = Path.Combine(dir, $"diagram_{DateTime.Now:yyyyMMdd_HHmmss}.png");
       
       // SaveToPng uses the Skia canvas, run it on UI thread
       GraphCanvasView.SaveToPng(path); 
       StatsText.Text += $" | Saved: {Path.GetFileName(path)}";
   }
   finally
   {
       IsEnabled = true;
   }
   ```

*Note: If `ExecuteScanFlowAsync` contains UI-dependent code (like showing the `CachePromptDialog`), ensure that specific dialog invocation is marshaled back to the UI thread via `Dispatcher.UIThread.InvokeAsync`, or restructure `ScanCoordinator` so it returns a state enum and lets `MainWindow` show the dialog.* (Based on the provided code, `ExecuteScanFlowAsync` takes a `Func<...> promptForCache` delegate, which handles the UI marshaling internally).

### Constraints

- Do not touch Avalonia controls (`GraphCanvasView`, `StatsText`, etc.) inside `Task.Run`. All UI updates must happen after the `await`.
- Ensure exceptions thrown inside the `Task.Run` are caught by the existing `try/catch` block in `OnScan`.

### Verification

1. `dotnet build`.
2. Run the GUI and scan a folder containing 50+ `.cs` files.
3. Confirm that the window can be moved and resized while the scan is in progress.
4. Confirm the graph renders correctly when the scan finishes.

---

## Step 03 — Fix disappearing nodes during drag

### Problem (in plain language)

In an attempt to optimize rendering (Step 12 of the original plan), the code was modified to draw only the dragged node during a drag operation, relying on a cached `SKPicture` for the rest of the graph. However, the cached picture only contains **namespace backgrounds and edges**—it does not contain the nodes. As a result, when a user clicks and drags a node, every other node on the canvas vanishes.

### What to find

- File: `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`
- Method: `RecordStaticContent`

### The fix

The cached static picture must include the non-dragged nodes. 

1. In `RecordStaticContent`, find where it draws edges:
   ```csharp
   DrawNamespaceGroups(canvas);
   DrawEdges(canvas, excludeNodeId: _draggedNodeIdDuringRender);
   ```

2. Immediately after `DrawEdges`, add a call to draw the non-dragged nodes. Because `CanvasRenderer.DrawNodes` doesn't have an `excludeNodeId` parameter, the easiest fix is to temporarily filter the nodes list, or add an optional parameter to `CanvasRenderer.DrawNodes`.

   **Option A (Simplest, modify GraphCanvas):**
   ```csharp
   DrawNamespaceGroups(canvas);
   DrawEdges(canvas, excludeNodeId: _draggedNodeIdDuringRender);
   
   // Draw all nodes EXCEPT the dragged one into the static picture
   var nodesToDraw = _draggedNodeIdDuringRender != null 
       ? _nodes.Where(n => n.Id != _draggedNodeIdDuringRender).ToList()
       : _nodes;
       
   _renderer.DrawNodes(canvas, nodesToDraw, GetViewportState());
   ```

   **Option B (Cleaner, add parameter to `CanvasRenderer`):**
   Add `string? excludeNodeId = null` to `CanvasRenderer.DrawNodes`. Filter out the node inside that method. Then call it from `RecordStaticContent`:
   ```csharp
   _renderer.DrawNodes(canvas, _nodes, GetViewportState(), excludeNodeId: _draggedNodeIdDuringRender);
   ```

3. Verify that `RenderNow` (and `SaveToPng`) still calls `DrawSingleNode` on top of the picture during a drag. The static picture now correctly represents the graph *without* the dragged node, and the dragged node is drawn live on top.

### Constraints

- Do not remove the `DrawSingleNode` call in `RenderNow`; it is still required to show the dragged node moving.
- Ensure hover/selection state is preserved correctly for the static nodes.

### Verification

1. `dotnet build`.
2. Run the GUI, scan a project.
3. Click and hold a node. Drag it around.
4. Confirm all other nodes remain visible on the canvas.
5. Release the node. Confirm the graph returns to normal rendering.

---

## Step 04 — Add ReDoS protection to `RoslynTypeScanner`

### Problem (in plain language)

Step 07 of the original plan added regex timeouts to `CustomStereotypeEngine` to prevent Catastrophic Backtracking (ReDoS). However, the `RoslynTypeScanner` *also* compiles and evaluates user-defined regex patterns during a scan, but it does so **without a timeout or `NonBacktracking` flag**. A bad regex pattern will still hang the scanner.

### What to find

- File: `src/MermaidDiagramExporter/Extraction/RoslynTypeScanner.cs`
- Method: `BuildStereotypes`

Search for `new System.Text.RegularExpressions.Regex`. You will find it inside the `foreach (var config in options.CustomStereotypes...)` loop.

### The fix

Apply the same hardening to the scanner's regex compilation that was applied to `CustomStereotypeEngine`.

1. Update the `Regex` instantiation to include `RegexOptions.NonBacktracking` and a `TimeSpan` timeout:
   ```csharp
   var regex = new System.Text.RegularExpressions.Regex(
       config.Pattern,
       System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.NonBacktracking,
       TimeSpan.FromMilliseconds(500));
   ```

2. Wrap the `IsMatch` call in a `try/catch` to handle timeouts gracefully:
   ```csharp
   try
   {
       bool matched = typeNameChain.Any(name => name != null && regex.IsMatch(name));
       if (matched && seen.Add(config.Label))
       {
           stereotypes.Add(config.Label);
       }
   }
   catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
   {
       // Treat timeout as non-match, continue scanning
       continue;
   }
   ```

### Constraints

- Do not change the logic for hardcoded Unity stereotypes (`MonoBehaviour`, etc.).
- Keep the timeout value consistent with `CustomStereotypeEngine` (500ms).

### Verification

1. `dotnet build`.
2. `dotnet test`.
3. Manual trace: If a user inputs a catastrophic pattern (e.g. `(a+)+`), the scanner will pause for 500ms on affected type names, then skip the rule and continue the scan without crashing.
```