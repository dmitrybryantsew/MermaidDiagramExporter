# MermaidDiagramExporter Performance Research: 14k Nodes

## Overview
*Note for Reviewers: Despite the name `MermaidDiagramExporter` (which sounds like a CLI-only export tool), this repository actually contains a full desktop application in `src/MermaidDiagramExporter.Gui/` built with Avalonia, which uses SkiaSharp (`CanvasRenderer.cs`) and custom graph layout engines (`LayeredLayoutEngine.cs`). This research report is based directly on the actual codebase.*

When rendering extremely large graphs (e.g., 14,000 nodes), the application currently experiences severe slow-downs across three primary domains: Parsing, Layout, and Rendering.

Below is an analysis of the bottlenecks based on the current architecture (located in `src/`), followed by actionable solutions to mitigate these issues and enable scaling to 14,000+ nodes.

---

## 1. Parsing Bottlenecks (`src/MermaidDiagramExporter/Extraction/RoslynTypeScanner.cs`)

### Current State
- The scanner evaluates the `CSharpCompilation` by iterating through `compilation.SyntaxTrees` sequentially.
- For each syntax tree, it queries `GetSemanticModel` on the main loop.
- It calculates associations and edges by inspecting all fields and properties of every `INamedTypeSymbol`, doing synchronous mapping.
- With 14,000 types, evaluating full semantic models synchronously over tens of thousands of members will freeze the CPU on a single core for a significant amount of time.

### Proposed Solutions
1. **Parallel Execution**: `CSharpCompilation` operations like `GetSemanticModel` and type extraction are largely thread-safe. Wrapping the syntax tree iteration in a `Parallel.ForEach` will distribute the semantic model calculation across all available CPU cores.
2. **Incremental Parsing**: By caching a serialized version of the `TypeGraph` alongside a file hash map of the scanned directory, the engine can parse only files that were modified.
3. **Syntax-first Heuristics**: If full semantic accuracy for associations isn't strictly necessary for a "bird's eye" view of 14,000 nodes, use syntax-only parsing for basic edges (regex or `SyntaxWalker`), falling back to `SemanticModel` only for resolving ambiguity.

---

## 2. Layout Bottlenecks (`src/MermaidDiagramExporter.Gui/Layout/LayeredLayoutEngine.cs`)

### Current State
- The `LayeredLayoutEngine` uses a Sugiyama-style layered approach, typical for Directed Acyclic Graphs.
- Steps include cycle removal, node rank assignment, crossing reduction, and coordinate assignment.
- The time complexity of crossing reduction heuristics (like barycenter or median passes) is often O(V^2) or O(|V| * |E|). Running this globally on 14,000 nodes and their edges will result in exponential compute times.

### Proposed Solutions
1. **Cluster-First Divide and Conquer**: Currently, all components are pushed to the global engine. We should instead group the nodes by their Namespace (which already acts as a cluster). Layout the inner contents of each namespace *independently*. Then, treat each namespace as a single "mega-node" and layout the macro-graph. This transforms the math from O(V_total^2) to sum(O(V_namespace^2)), which is astronomically faster.
2. **Asynchronous Layout processing**: Currently, running layout blocks the main UI thread. Layout calculation should be moved to a background `Task.Run` with a loading overlay, ensuring the GUI doesn't appear frozen.
3. **Alternative Layout Algorithms**: Sugiyama is highly structured but slow. Switching to an O(V log V) multi-pole Force-Directed algorithm (like Barnes-Hut) or an Orthogonal Grid layout for graphs larger than ~1,000 nodes will provide immediate rendering without the excessive computation time.

---

## 3. Rendering Bottlenecks (`src/MermaidDiagramExporter.Gui/CanvasRenderer.cs`)

### Current State
- When the canvas renders, it iterates over all nodes (`foreach (var node in nodes)`) and all edges (`foreach (var edge in edges)`) regardless of whether they are visible on the screen.
- `CanvasRenderer` issues SkiaSharp draw calls for text, borders, headers, and badges for every single element on the canvas. With 14,000 nodes, this results in hundreds of thousands of individual draw commands per frame.
- There is no Level of Detail (LOD); zooming out to view all 14,000 nodes still attempts to render 10pt text for class members.

### Proposed Solutions
1. **Spatial Culling (View Frustum Culling)**: Implement a spatial indexing structure (e.g., an R-Tree, QuadTree, or simple Spatial Grid). When a render is requested, only query the nodes and edges that intersect the current viewport boundary (`ViewportState.PanX`, `PanY`, `Zoom`).
2. **Level of Detail (LOD)**:
   - *Micro View (Zoom > 0.8x)*: Render fully with text, properties, and methods.
   - *Macro View (Zoom 0.2x - 0.8x)*: Render node headers and badges only; hide member text.
   - *Bird's Eye (Zoom < 0.2x)*: Render nodes as simple colored rectangles. Hide text completely. Disable edge rendering entirely, or only render thick "highways" between namespaces.
3. **Skia Batching**: If many nodes must be drawn, group the draw calls by `SKPaint`. Draw all node backgrounds at once, then all borders, etc., to minimize state changes in the graphics pipeline. Use `canvas.DrawPositionedText` for batch text rendering.