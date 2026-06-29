# Step 03: Custom Stereotype Rules

## Overview
The `RoslynTypeScanner.BuildStereotypes()` method currently hardcodes Unity type detection (`MonoBehaviour`, `ScriptableObject`, `Component`). Make this configurable via Settings so users can define their own regex-based stereotype rules with custom labels and colors. These stereotypes apply as badges on the canvas nodes.

## Dependencies
- **Step 01** — uses `ProjectSettings.StereotypeRules` and `ProjectSettings.ApplyCustomStereotypes`
- **Step 02** — should be complete (not strictly required but provides the persistence layer for settings)

---

## Part A: Stereotype Rule Matching Engine

### 1. Create `src/MermaidDiagramExporter.Gui/Stereotypes/CustomStereotypeEngine.cs`

This evaluates user-defined regex patterns against type names and base-type chains:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MermaidDiagramExporter.Gui.Settings;

namespace MermaidDiagramExporter.Gui.Stereotypes;

/// <summary>
/// Evaluates custom stereotype rules against type identifiers.
/// Thread-safe; holds compiled regexes internally.
/// </summary>
public sealed class CustomStereotypeEngine
{
    private readonly List<CompiledRule> _rules = new();

    public CustomStereotypeEngine(IEnumerable<StereotypeRule> rules)
    {
        foreach (var rule in rules ?? Enumerable.Empty<StereotypeRule>())
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern))
                continue;
            try
            {
                var regex = new Regex(rule.Pattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);
                _rules.Add(new CompiledRule(regex, rule.Label, rule.ColorHex));
            }
            catch { /* invalid regex — skip */ }
        }
    }

    /// <summary>
    /// Returns all matching stereotypes for a given type name.
    /// </summary>
    public IReadOnlyList<MatchedStereotype> Match(string typeName)
    {
        var results = new List<MatchedStereotype>();
        foreach (var rule in _rules)
        {
            if (rule.Regex.IsMatch(typeName))
            {
                results.Add(new MatchedStereotype(rule.Label, rule.ColorHex));
            }
        }
        return results;
    }

    /// <summary>
    /// Returns all matching stereotypes by testing the type name AND each base type name in the chain.
    /// </summary>
    public IReadOnlyList<MatchedStereotype> MatchChain(params string[] typeNamesInChain)
    {
        var results = new List<MatchedStereotype>();
        foreach (var rule in _rules)
        {
            bool matched = false;
            foreach (var typeName in typeNamesInChain)
            {
                if (typeName != null && rule.Regex.IsMatch(typeName))
                {
                    matched = true;
                    break;
                }
            }
            if (matched)
            {
                results.Add(new MatchedStereotype(rule.Label, rule.ColorHex));
            }
        }
        return results;
    }

    public bool HasRules => _rules.Count > 0;

    private sealed class CompiledRule
    {
        public Regex Regex;
        public string Label;
        public string ColorHex;
        public CompiledRule(Regex regex, string label, string colorHex)
        {
            Regex = regex;
            Label = label;
            ColorHex = colorHex;
        }
    }
}

public sealed class MatchedStereotype
{
    public string Label { get; }
    public string ColorHex { get; }
    public MatchedStereotype(string label, string colorHex)
    {
        Label = label;
        ColorHex = colorHex;
    }
}
```

---

## Part B: Stereotype Editor UI (inside Settings Window)

### 2. Add Stereotype Rule Editor to Settings Window XAML

Open `src/MermaidDiagramExporter.Gui/Settings/SettingsWindow.axaml`. Add this section after the existing "Stereotypes" header checkbox:

```xml
          <!-- Add inside the StackPanel after the ApplyCustomStereotypesCheck -->
          <Border BorderBrush="#3A4250" BorderThickness="1" CornerRadius="4" Padding="8">
            <Grid RowDefinitions="Auto,*" ColumnDefinitions="*,Auto">
              <TextBlock Grid.Row="0" Grid.Column="0" Text="Rules" FontWeight="Bold" Margin="0,0,0,4" />
              <Button Grid.Row="0" Grid.Column="1" x:Name="AddStereotypeRuleButton" Content="+ Add" Padding="8,2" />
              <ListBox Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="2"
                       x:Name="StereotypeRulesList"
                       Height="140"
                       BorderThickness="0"
                       Background="Transparent">
                <ListBox.ItemTemplate>
                  <DataTemplate>
                    <Grid ColumnDefinitions="*,100,80,Auto">
                      <TextBox Grid.Column="0" Text="{Binding Pattern}" Watermark="Regex pattern" Margin="0,0,4,0" />
                      <TextBox Grid.Column="1" Text="{Binding Label}" Watermark="Label" Margin="0,0,4,0" />
                      <TextBox Grid.Column="2" Text="{Binding ColorHex}" Watermark="#RRGGBB" Margin="0,0,4,0" />
                      <Button Grid.Column="3" x:Name="RemoveStereotypeRuleButton"
                              Content="x" Tag="{Binding}" Click="OnRemoveStereotypeRule" />
                    </Grid>
                  </DataTemplate>
                </ListBox.ItemTemplate>
              </ListBox>
            </Grid>
          </Border>
```

### 3. Update `SettingsWindow.axaml.cs`

Add a view-model collection field:

```csharp
private System.Collections.ObjectModel.ObservableCollection<StereotypeRule> _stereotypeRules = new();
```

In `LoadForProject()`, add:
```csharp
_stereotypeRules.Clear();
foreach (var rule in _settings.StereotypeRules)
    _stereotypeRules.Add(new StereotypeRule
    {
        Pattern = rule.Pattern,
        Label = rule.Label,
        ColorHex = rule.ColorHex
    });
StereotypeRulesList.ItemsSource = _stereotypeRules;
```

Wire the Add button in the constructor or `WireEvents()`:
```csharp
AddStereotypeRuleButton.Click += (s, e) =>
{
    _stereotypeRules.Add(new StereotypeRule
    {
        Pattern = ".*",
        Label = "New",
        ColorHex = "#4ECDC4"
    });
};
```

Add the remove handler:
```csharp
private void OnRemoveStereotypeRule(object? sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is StereotypeRule rule)
    {
        _stereotypeRules.Remove(rule);
    }
}
```

In `OnSave()`, replace the simple stereotype assignment with:
```csharp
_settings.StereotypeRules = _stereotypeRules.ToList();
```

---

## Part C: Apply Custom Stereotypes During Scan

### 4. Modify `src/MermaidDiagramExporter/Extraction/RoslynTypeScanner.cs`

The `BuildStereotypes` method needs to accept custom rules. Since the scanner is in the core project (not GUI), the cleanest approach is to pass custom stereotypes through `GraphBuildOptions`.

**Modify `GraphBuildOptions` in `src/MermaidDiagramExporter/Core/TypeGraphModels.cs`:**

Add a simple list of string tuples (or a small DTO) to `GraphBuildOptions`:

```csharp
// Add to the existing GraphBuildOptions class
public List<StereotypeConfig> CustomStereotypes { get; set; } = new();

// Add this class in the same namespace/file
public sealed class StereotypeConfig
{
    public string Pattern { get; set; } = ".*";
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "#4ECDC4";
}
```

**Modify `RoslynTypeScanner.BuildStereotypes()`:**

Change the method signature to accept `GraphBuildOptions` and add custom matching:

```csharp
private List<string> BuildStereotypes(INamedTypeSymbol type, GraphBuildOptions options)
{
    List<string> stereotypes = new();
    HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);

    // --- Existing hardcoded Unity stereotypes ---
    INamedTypeSymbol? current = type.BaseType;
    while (current != null)
    {
        string baseName = current.Name ?? string.Empty;
        string baseFullName = current.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        string? stereotype = null;
        if (baseName == "MonoBehaviour" || baseFullName.Contains("MonoBehaviour"))
            stereotype = "mono-behaviour";
        else if (baseName == "ScriptableObject" || baseFullName.Contains("ScriptableObject"))
            stereotype = "scriptable-object";
        else if (baseName == "Component" || baseFullName.Contains("UnityEngine.Component"))
            stereotype = "component";

        if (stereotype != null && seen.Add(stereotype))
            stereotypes.Add(stereotype);

        current = current.BaseType;
    }

    // --- NEW: User-defined custom stereotypes ---
    // Build the chain of type names to match against
    var typeNameChain = new List<string>();
    typeNameChain.Add(type.Name ?? string.Empty);
    typeNameChain.Add(type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    var baseCurrent = type.BaseType;
    while (baseCurrent != null)
    {
        typeNameChain.Add(baseCurrent.Name ?? string.Empty);
        typeNameChain.Add(baseCurrent.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        baseCurrent = baseCurrent.BaseType;
    }

    foreach (var config in options.CustomStereotypes ?? Enumerable.Empty<StereotypeConfig>())
    {
        if (string.IsNullOrWhiteSpace(config.Pattern) || string.IsNullOrWhiteSpace(config.Label))
            continue;
        try
        {
            var regex = new System.Text.RegularExpressions.Regex(config.Pattern,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            bool matched = typeNameChain.Any(name => name != null && regex.IsMatch(name));
            if (matched && seen.Add(config.Label))
            {
                stereotypes.Add(config.Label);
            }
        }
        catch { /* invalid regex — skip */ }
    }

    return stereotypes;
}
```

**Update the call site in `BuildNode()`:**

```csharp
// In BuildNode(), change:
//   List<string> stereotypes = BuildStereotypes(type);
// To:
List<string> stereotypes = BuildStereotypes(type, resolvedOptions);
```

---

## Part D: Stereotype Badges on Canvas

### 5. Modify `GraphNode` in `src/MermaidDiagramExporter.Gui/GraphCanvas.cs`

Add a stereotype display property:

```csharp
public class GraphNode
{
    // ... existing properties ...

    /// <summary>
    /// Stereotype labels with their display colors.
    /// Populated by the LayoutEngine from TypeNodeData.Stereotypes + custom rules.
    /// </summary>
    public List<GraphStereotypeBadge> StereotypeBadges { get; set; } = new();
}

public class GraphStereotypeBadge
{
    public string Label { get; set; } = "";
    public string ColorHex { get; set; } = "#4ECDC4";
}
```

### 6. Modify `LayoutEngine` in `src/MermaidDiagramExporter.Gui/LayoutEngine.cs`

Populate `StereotypeBadges` when building `GraphNode` objects. Map the string stereotypes to colored badges:

In the node-building loop, after `Kind = nd.Kind.ToString(),`, add:

```csharp
// Build stereotype badges
var badges = new List<GraphStereotypeBadge>();
foreach (var stereotype in nd.Stereotypes)
{
    string color = stereotype switch
    {
        "mono-behaviour" => "#4CAF50",
        "scriptable-object" => "#FF9800",
        "component" => "#2196F3",
        _ => "#9E9E9E" // default gray for unknown
    };
    badges.Add(new GraphStereotypeBadge { Label = stereotype, ColorHex = color });
}
// ... later assigned to node.StereotypeBadges = badges;
```

### 7. Modify `DrawNodes()` in `GraphCanvas.cs`

After drawing the main badge (IF/EN/ST/etc.), draw custom stereotype badges as small pills below the type badge:

Inside `DrawNodes()`, after the existing badge drawing block (`if (badge != null) { ... }`), add:

```csharp
// Draw custom stereotype badges
if (node.StereotypeBadges.Count > 0)
{
    float badgeSpacing = 4;
    float badgeH = 14;
    float currentBadgeY = y + 6;
    // Place to the left of the type badge if present, otherwise right side
    float currentBadgeX = x + w - 6;

    foreach (var stBadge in node.StereotypeBadges)
    {
        using var stPaint = new SKPaint
        {
            Color = SKColor.TryParse(stBadge.ColorHex, out var c) ? c : SKColor.Parse("#9E9E9E"),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        using var stTextPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            TextSize = 8
        };
        float stBadgeW = stTextPaint.MeasureText(stBadge.Label) + 10;
        currentBadgeX -= stBadgeW;
        canvas.DrawRoundRect(currentBadgeX, currentBadgeY, stBadgeW, badgeH, 3, 3, stPaint);
        canvas.DrawText(stBadge.Label, currentBadgeX + 5, currentBadgeY + 10, stTextPaint);
        currentBadgeX -= badgeSpacing;
    }
}
```

### 8. Wire Custom Stereotypes from Settings into the Scan

In `MainWindow.axaml.cs`, modify `OnScan()` so that `_scanner.ScanFolder()` receives the custom stereotypes:

```csharp
// Before calling _scanner.ScanFolder(), populate the build options:
var buildOptions = new GraphBuildOptions();
if (_currentSettings.ApplyCustomStereotypes && _currentSettings.StereotypeRules.Count > 0)
{
    foreach (var rule in _currentSettings.StereotypeRules)
    {
        buildOptions.CustomStereotypes.Add(new StereotypeConfig
        {
            Pattern = rule.Pattern,
            Label = rule.Label,
            ColorHex = rule.ColorHex
        });
    }
}

_currentGraph = _scanner.ScanFolder(folder, buildOptions);
```

---

## Testing Checklist

1. Open Settings, add a rule: Pattern=`.*Controller$`, Label=`MVC-Controller`, Color=`#FF6B6B`. Save.
2. Create a test `.cs` file with a class named `PlayerController`. Scan it. The node should show a red "MVC-Controller" pill badge.
3. Add another rule: Pattern=`.*Repository$`, Label=`Repository`, Color=`#4ECDC4`. Create a `UserRepository` class. Scan. Should show a teal "Repository" badge.
4. The existing Unity stereotypes (`mono-behaviour`, etc.) should still work when scanning Unity projects.
5. Uncheck "Apply custom stereotype rules" in Settings and rescan — custom badges should not appear (Unity ones still do).
6. Test invalid regex patterns (e.g., `[unclosed`) — should be silently skipped without crashing.
