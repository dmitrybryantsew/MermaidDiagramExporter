# MermaidDiagramExporter

A .NET 9 application that generates [Mermaid](https://mermaid.js.org/) class diagrams from C# source code using Roslyn. Ships as both a CLI tool and an Avalonia GUI.

## What It Does

- Scans a folder of `.cs` files (recursively) using the Roslyn compiler
- Extracts types (classes, structs, interfaces, enums), their members, and relationships (inheritance, interfaces, associations)
- Produces a Mermaid class diagram grouped by namespace
- CLI: writes `.md` or `.mmd` files
- GUI: interactive canvas with focus-based navigation, minimap, and namespace filtering

## Projects

| Project | Type | Description |
|---|---|---|
| `MermaidDiagramExporter` | Console / CLI | Entry point for command-line usage |
| `MermaidDiagramExporter.Gui` | Avalonia UI | Desktop GUI with interactive graph canvas |
| `MermaidDiagramExporter.Tests` | xUnit | Unit and integration tests |

## CLI Usage

```bash
dotnet run --project src/MermaidDiagramExporter -- <folder> [options]
```

### Arguments

- `<folder>` — folder containing `.cs` files (recursive scan)

### Options

| Option | Description |
|---|---|
| `-o, --output <path>` | Output directory (default: `./docs/mermaid`) |
| `--format <md\|mmd\|both>` | Output format: Markdown, Mermaid, or both (default: `md`) |
| `--no-fields` | Exclude fields from diagram nodes |
| `--no-properties` | Exclude properties from diagram nodes |
| `--no-methods` | Exclude methods from diagram nodes |
| `--no-interfaces` | Exclude interface implementation edges |
| `--no-associations` | Exclude association edges |
| `--max-members <N>` | Truncate members per node |
| `--open` | Open output file after export |
| `-h, --help` | Show help |

### Examples

```bash
# Basic: scan current folder, output markdown to docs/mermaid
dotnet run --project src/MermaidDiagramExporter -- ./src

# Export raw .mmd file, open when done
dotnet run --project src/MermaidDiagramExporter -- ./src --format mmd --open

# Only inheritance, no members
dotnet run --project src/MermaidDiagramExporter -- ./src --no-fields --no-properties --no-methods --no-associations
```

## GUI

Launch the Avalonia desktop app:

```bash
dotnet run --project src/MermaidDiagramExporter.Gui
```

Features:

- Folder picker to select source directory
- Interactive graph canvas with pan/zoom
- Focus navigation — select a node to see its neighborhood (configurable depth and traversal mode)
- Minimap for large diagrams
- Namespace-based filtering
- Export to PNG

## Architecture

```
src/
  MermaidDiagramExporter/            CLI entry + Roslyn scanning + Mermaid export
    Extraction/RoslynTypeScanner.cs  Roslyn-based type/member/edge extraction
    Core/TypeGraphModels.cs          Graph data models (nodes, edges, groups)
    Export/MermaidGraphExporter.cs   Builds Mermaid output via FoggyBalrog.MermaidDotNet
    Focus/                           Focus-based subgraph navigation
  MermaidDiagramExporter.Gui/        Avalonia GUI
    MainWindow.axaml.cs              Main window logic
    GraphCanvas.cs                   Custom graph canvas control
    FocusNavigator.cs                Canvas focus navigation
    Layout/                          Graph layout engine (layered Sugiyama-style)
      LayeredLayoutEngine.cs         Main layout coordinator
      Passes/                        Layout pipeline passes
      Post/                          Post-layout polish passes
      Routing/                       Edge routing with cluster clipping
tests/
  MermaidDiagramExporter.Tests/       xUnit test suite
    MermaidExporterTests.cs          Export logic tests
    RoslynTypeScannerTests.cs        Scanner tests
    TypeGraphTests.cs                Graph model tests
    FocusTests.cs                    Focus navigation tests
    CliAndEndToEndTests.cs           CLI + end-to-end tests
    NewFeatureTests.cs               Integration tests for newer features
```

### Key Design Decisions

- **Roslyn over reflection**: Source-level analysis means no build required; works on any `.cs` files
- **Pipeline layout**: Sugiyama-style layered layout with separate passes for cycle removal, layer assignment, crossing reduction, and coordinate assignment
- **Focus navigation**: Select a node and the UI shows only its N-hop neighborhood, with configurable traversal modes (undirected, inheritance-only, etc.)

## Building

Requirements: .NET 9 SDK

```bash
# Restore + build
dotnet build

# Run tests
dotnet test

# Run tests with verbosity
dotnet test --verbosity normal
```

## Dependencies

- [Microsoft.CodeAnalysis.CSharp](https://www.nuget.org/packages/Microsoft.CodeAnalysis.CSharp/) — Roslyn compiler
- [FoggyBalrog.MermaidDotNet](https://www.nuget.org/packages/FoggyBalrog.MermaidDotNet/) — Mermaid diagram builder
- [Avalonia](https://avaloniaui.net/) — Cross-platform GUI (GUI project only)
- [xunit](https://xunit.net/) — Test framework (test project only)
- [YamlDotNet](https://www.nuget.org/packages/YamlDotNet/) — Settings serialization
