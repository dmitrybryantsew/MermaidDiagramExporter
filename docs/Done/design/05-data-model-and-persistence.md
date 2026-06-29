# 05 — Data Model and Persistence

## The principle

The Design Mode data model is a **clean, serializable, diffable** representation
of a class diagram. It is independent of the runtime layout coordinates, the
rendering state, and the scan output. It maps cleanly to and from the existing
`TypeGraph` so that:
- A scanned graph can be converted to a `DesignGraph` (import as starting point).
- A `DesignGraph` can be converted to a `TypeGraph` (export, layout, render).

## Data model

```csharp
namespace MermaidDiagramExporter.Gui.Design;

public sealed class DesignGraph
{
    public string Title { get; set; } = "Untitled Design";
    public string Version { get; set; } = "1";  // schema version for forward compat
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedUtc { get; set; } = DateTime.UtcNow;
    public List<DesignClass> Classes { get; set; } = new();
    public List<DesignEdge> Edges { get; set; } = new();
    public List<DesignNamespace> Namespaces { get; set; } = new();
}

public sealed class DesignClass
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "NewClass";
    public string Namespace { get; set; } = "";  // empty = global namespace
    public ClassKind Kind { get; set; } = ClassKind.Class;
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; } = 200f;
    public float Height { get; set; } = 100f;
    public List<DesignMember> Members { get; set; } = new();
    public string? Stereotype { get; set; }  // optional custom label
}

public enum ClassKind { Class, Interface, Enum, Struct, StaticClass, AbstractClass }

public sealed class DesignMember
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public MemberKind Kind { get; set; } = MemberKind.Field;
    public string Name { get; set; } = "NewMember";
    public string TypeName { get; set; } = "object";
    public Visibility Visibility { get; set; } = Visibility.Public;
    public List<DesignParameter> Parameters { get; set; } = new();  // methods only
}

public enum MemberKind { Field, Property, Method, Constructor, Event }

public enum Visibility { Public, Private, Protected, Internal }

public sealed class DesignParameter
{
    public string Name { get; set; } = "param";
    public string TypeName { get; set; } = "object";
}

public sealed class DesignEdge
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FromClassId { get; set; } = "";
    public string ToClassId { get; set; } = "";
    public EdgeKind Kind { get; set; } = EdgeKind.Association;
    public string? Label { get; set; }
}

public enum EdgeKind { Association, Inheritance, Implements, Dependency, Aggregation, Composition }

public sealed class DesignNamespace
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "MyNamespace";
    public string? ParentNamespaceId { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
}
```

## Design decisions

### Why a new model instead of reusing `TypeGraph`?

`TypeGraph` is the **scan output** model — it's shaped by what Roslyn produces.
It has fields like `AssemblyName`, `SourceFilePath`, `IsExternal`, `Members`
with `IsStatic`/`IsReadOnly`/`IsAbstract`/`IsOverride` flags. These don't
make sense for a hand-drawn design.

`DesignGraph` is the **authoring input** model — minimal, predictable, with
exactly the fields a user would care about when drawing a diagram. The two
models are related (both describe class diagrams) but have different shapes.

A `DesignGraphToTypeGraphConverter` (and its inverse) bridges the two. See
doc 06 for the export pipeline.

### Why GUIDs for IDs?

`TypeGraph` uses human-readable IDs like `T_global__MyApp_Foo` (derived from
the full type name). For Design Mode, the user might create two classes with
the same name in different namespaces, or rename a class. GUIDs avoid
collision and make rename operations trivial (no need to update IDs).

The `DesignClass.Name` is still the human-readable identifier used in exports.
GUIDs are internal.

### Why a schema version field?

JSON serialization needs forward compatibility. When the format changes
(e.g. a new field is added), old files should still load. The `Version`
field lets the deserializer handle migrations:

```csharp
if (graph.Version == "1") {
    // current schema
} else if (graph.Version == "0") {
    // migrate from pre-versioning schema
} else {
    throw new InvalidDataException($"Unsupported design graph version: {graph.Version}");
}
```

### Why store X/Y/Width/Height on classes?

The user can manually position classes. These positions are part of the
design intent ("I want ClassA above ClassB"). The layout engine can override
them when "Apply Layout" is clicked, but the user's manual positions are
preserved in the JSON.

When loading a saved design, the saved positions are used as-is. When
applying layout, the positions are updated in-place in the `DesignGraph`,
and the user can undo with Ctrl+Z.

### Position authority: Design Mode vs. `ManualLayoutOverrides`

The rest of the app uses `ManualLayoutOverrides` to persist user-dragged
positions as **deltas from the engine-computed position**. This works for
Analyze Mode because every class has an engine-computed position (the layout
engine ran on the scanned graph) — the override is "nudge ClassA 10px right
from where the engine put it."

Design Mode breaks that assumption: a freshly-added class has **no
engine-computed position**. Its X/Y is authoritative, not a delta from
anything. So Design Mode does **not** use `ManualLayoutOverrides`. Instead:

- `DesignClass.X/Y/Width/Height` are the single source of truth for position.
- When "Apply Layout" is clicked, the layout engine's output is **written
  directly** into `DesignClass.X/Y`, replacing the user's positions. This
  is one undo step (the entire layout operation), not per-class.
- When the design is exported to `TypeGraph` for rendering, the positions
  come directly from `DesignClass.X/Y`. No delta layer.

This is a **deliberate divergence** from Analyze Mode's position-authority
model. It is documented here so future implementers don't try to unify the
two models prematurely.

**Why this matters for the user**: in Design Mode, Ctrl+Z after "Apply
Layout" restores the user's manual positions exactly. In Analyze Mode,
Ctrl+Z (if it existed) would restore the deltas, which depend on the engine
position being stable. The two modes have different undo semantics for
position changes, and that's correct given their different data models.

## JSON serialization

Use `System.Text.Json` (already used elsewhere in the codebase):

```csharp
public static class DesignSerialization
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public static void Save(DesignGraph graph, string filePath)
    {
        graph.ModifiedUtc = DateTime.UtcNow;
        string json = JsonSerializer.Serialize(graph, Options);
        File.WriteAllText(filePath, json);
    }

    public static DesignGraph Load(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var graph = JsonSerializer.Deserialize<DesignGraph>(json, Options)
            ?? throw new InvalidDataException("Failed to deserialize design graph");
        return graph;
    }

    public static DesignGraph? TryLoad(string filePath)
    {
        try { return Load(filePath); }
        catch { return null; }
    }
}
```

File extension: `.dgraph.json` (design graph JSON).

## Save/load UI

In Design Mode, the toolbar has:
- **New** — clear current design, start blank
- **Open...** — file picker, load `.dgraph.json`
- **Save** — save to current file (or prompt if untitled)
- **Save As...** — file picker, save with new name
- **Import from Scan...** — convert current scanned `TypeGraph` to `DesignGraph`,
  open in Design Mode for editing

Keyboard shortcuts:
- Ctrl+S — Save
- Ctrl+Shift+S — Save As

The "current file" is tracked in `DesignModeController.CurrentFilePath`.
When the design is dirty and the user tries to close the app or switch modes,
prompt to save.

## Round-trip with scanned graphs

Two converters bridge `TypeGraph` and `DesignGraph`:

### Scan → Design (import)

```csharp
public static class DesignGraphImporter
{
    public static DesignGraph FromTypeGraph(TypeGraph scan, string? title = null)
    {
        var design = new DesignGraph
        {
            Title = title ?? scan.Title,
            Classes = scan.Nodes.Select(ToDesignClass).ToList(),
            Edges = scan.Edges.Where(e => /* both endpoints exist */)
                              .Select(ToDesignEdge).ToList()
        };
        return design;
    }

    private static DesignClass ToDesignClass(TypeNodeData node)
    {
        return new DesignClass
        {
            Name = node.DisplayName,
            Namespace = node.Namespace,
            Kind = ToClassKind(node.Kind),
            Members = node.Members.Select(ToDesignMember).ToList()
            // X/Y/Width/Height set by layout pass
        };
    }
}
```

After import, the user's manual positions are empty (X/Y = 0). The layout
engine runs once to position them, then the user can adjust.

### Design → Scan (export for layout/render)

```csharp
public static class DesignGraphExporter
{
    public static TypeGraph ToTypeGraph(DesignGraph design)
    {
        var nodes = design.Classes.Select(ToTypeNode).ToList();
        var edges = design.Edges.Select(ToTypeEdge).ToList();
        var groups = BuildNamespaceGroups(design);

        return new TypeGraph(
            title: design.Title,
            nodes: nodes,
            edges: edges,
            groups: groups,
            metadata: new TypeGraphMetadata { /* ... */ }
        );
    }
}
```

This is what `Apply Layout` and the export buttons call.

## Versioning and migration

Schema version "1" is the initial format. Future changes:

- Adding optional fields: backward-compatible, no migration needed.
- Removing fields: requires major version bump.
- Renaming fields: use `[JsonPropertyName]` attribute to map old name to new.
- Changing field types: requires migration code in the loader.

When the loader sees an unknown version, it throws `InvalidDataException`
with a clear message. The UI catches this and shows an error dialog.

## Storage locations

- **Default folder**: `%USERPROFILE%/.mermaid-diagram-exporter/designs/`
- **Recent files**: stored in `ProjectSettings.RecentDesignFiles` (a list of
  paths, max 10 entries, MRU order).
- **Auto-save**: every 30 seconds if dirty, to `%TEMP%/.mermaid-diagram-exporter/autosave-{guid}.dgraph.json`.
  On startup, if an autosave file exists and the user hasn't dismissed it,
  offer to recover.

## Risks

- **Concurrent saves**: if the user has the same file open in two windows,
  last-write-wins. Could detect via file hash on load and warn on save if
  changed externally. Decision: out of scope for v1.
- **Large designs**: a 1000-class design could produce a 5MB JSON file. The
  serializer handles this fine, but the canvas might lag. Mitigated by
  keeping hit-tests O(n) linear scans (acceptable at 50–100 classes; spatial
  pruning is a future optimization, not a current capability).
- **Namespace nesting**: the `DesignNamespace` model supports parent namespaces,
  but the UI for creating nested namespaces (drag namespace into another) is
  not in scope for v1. Namespaces are flat for now.
