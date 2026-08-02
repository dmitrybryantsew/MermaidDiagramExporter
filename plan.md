# Plan: Add TypeScript scanning support

1. **Extract `ITypeScanner` interface**:
   - Create `src/MermaidDiagramExporter/Core/ITypeScanner.cs` inside the Core namespace (as instructed).
   - Move `TypeGraph ScanFolder(string folderPath, GraphBuildOptions options)` from `RoslynTypeScanner` to the `ITypeScanner` interface.
   - Make `RoslynTypeScanner` implement `ITypeScanner`.

2. **Implement TypeScript Subprocess Bridge (`TypeScriptSubprocessScanner.cs`)**:
   - Create `src/MermaidDiagramExporter/Extraction/TypeScriptSubprocessScanner.cs`
   - Implement `ITypeScanner`.
   - Spawns process: `node ts-scanner.js <folder>`. It needs to figure out the path to `ts-scanner.js`. We can include it in the project and ensure it's copied to the output directory using `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>` in `MermaidDiagramExporter.csproj` and GUI.
   - Use `System.Text.Json` to deserialize JSON stdout into `TypeGraph` (`TypeGraph` has constructors or we might need to deserialize into DTOs if required, but it looks like standard serialization could work, wait `TypeGraph` constructor requires nodes, edges, etc., might need a builder or specific deserialization logic).
   - We will verify serialization maps correctly. `TypeGraph` properties like `Title`, `Nodes`, `Edges`, `Groups`, `Metadata` might need to be populated.

3. **Write `ts-scanner.js`**:
   - Create `src/MermaidDiagramExporter/Extraction/ts-scanner.js`
   - We will write a pure JS script that uses the standard Typescript compiler API or basic regex / AST parser if `typescript` node module is not guaranteed? The instruction says: "using the official TypeScript Compiler API... Requires Node.js as an external dependency."
   - We will use `require('typescript')`. The user must run `npm install typescript` in their environment or globally, or we can check if it exists or use `npx typescript`. Wait, actually we can just bundle it or `require('typescript')` and let user handle TS. A better way is using `npx --no-install -c "node ts-scanner.js ..."` or just expect `typescript` to be installed locally/globally. But wait! We can just use `require('typescript')` because TS projects already have `typescript` in their `node_modules`! We can search for `typescript` or tell node to resolve it. If we run `node /path/to/ts-scanner.js /path/to/target/folder`, we can use `require(require.resolve('typescript', { paths: [folderPath] }))`.

4. **Add `TypeScannerFactory` or switch logic**:
   - Create `ScannerFactory.cs` (or just inline it). The instruction says:
   ```csharp
   ITypeScanner scanner = fileType == ProjectType.TypeScript
       ? new TypeScriptSubprocessScanner()
       : new RoslynTypeScanner();
   ```
   - We will replace `RoslynTypeScanner` with `ITypeScanner` in `ScanCoordinator`, `Program`, `MainWindow.axaml.cs`, `App.axaml.cs`.
   - For detection: if there are `.ts` or `.tsx` files in the folder (and no `.cs` files, or majority, or check `tsconfig.json`), use TS scanner. We'll use a simple heuristic: `File.Exists(Path.Combine(folder, "tsconfig.json"))` or looking for `.ts` files.

5. **Pre commit instructions**:
   - Complete pre-commit steps (ensure proper testing, verifications, review, etc.).
