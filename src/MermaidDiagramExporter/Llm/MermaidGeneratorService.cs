using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MermaidDiagramExporter.Llm;

/// <summary>
/// Uses an LLM to generate a class diagram description in Mermaid syntax,
/// then parses that Mermaid text into a <see cref="DesignGraph"/>-compatible
/// set of classes, edges, and namespaces.
///
/// The flow:
///   1. User provides a natural-language prompt (e.g. "Design a C# app
///      for managing chemical calculations with a CalculationService,
///      ChemicalRepository, and CalculationViewModel").
///   2. We send the prompt to the configured LLM with a system message
///      instructing it to output ONLY a Mermaid classDiagram block.
///   3. We parse the Mermaid text into DesignClass/DesignEdge objects.
///   4. The caller merges them into the active DesignGraph.
/// </summary>
public sealed class MermaidGeneratorService
{
    private readonly LlmService _llmService;

    public MermaidGeneratorService(LlmSettings settings) : this(new LlmService(settings)) { }

    public MermaidGeneratorService(LlmService llmService)
    {
        _llmService = llmService;
    }

    /// <summary>System prompt that constrains the LLM to Mermaid classDiagram output.</summary>
    private const string SystemPrompt = @"You are a C# software architect. The user will describe a C# application, and you must respond with ONLY a Mermaid class diagram. Follow these rules strictly:

1. Output ONLY a single ```mermaid code block containing a classDiagram. No explanations, no markdown outside the code block.
2. Use proper Mermaid classDiagram syntax:
   - `class ClassName` for each class/interface/struct/enum
   - `<<interface>> ClassName` for interfaces
   - `<<abstract>> ClassName` for abstract classes
   - `<<static>> ClassName` for static classes
   - `<<struct>> ClassName` for structs
   - `<<enumeration>> ClassName` for enums
3. Include members with proper UML notation:
   - `+PublicField : string` for public fields
   - `-_privateField : int` for private fields
   - `+Property : string` for properties (add `«property»` stereotype hint)
   - `+Method(param1 : Type, param2 : Type) : ReturnType` for methods
4. Use classDiagram relationship syntax:
   - `ClassA --|> ClassB` for inheritance
   - `ClassA ..|> InterfaceB` for implements
   - `ClassA --> ClassB` for association/dependency
   - `ClassA o--> ClassB` for aggregation
   - `ClassA *--> ClassB` for composition
5. Group classes by namespace using `namespace MyNamespace { ... }` blocks
6. Make the design realistic for C# — use proper C# types (string, int, bool, List<T>, Dictionary<K,V>, Task, etc.)
7. Include enough members to make the design useful as a starting point for code generation";

    /// <summary>
    /// Generates a DesignGraph from a user prompt by calling the LLM and
    /// parsing the Mermaid response. Non-streaming (simpler, waits for full
    /// response).
    /// </summary>
    public async Task<MermaidGenerationResult> GenerateAsync(
        string userPrompt,
        CancellationToken ct = default)
    {
        var messages = new List<LlmChatMessage>
        {
            LlmChatMessage.System(SystemPrompt),
            LlmChatMessage.User(userPrompt)
        };

        string rawResponse;
        try
        {
            rawResponse = await _llmService.ChatAsync(messages, ct);
        }
        catch (Exception ex)
        {
            return MermaidGenerationResult.Failed($"LLM request failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(rawResponse))
            return MermaidGenerationResult.Failed("LLM returned an empty response");

        // Extract the Mermaid block from the response
        string mermaidText = ExtractMermaidBlock(rawResponse);
        if (string.IsNullOrWhiteSpace(mermaidText))
            return MermaidGenerationResult.Failed("LLM response did not contain a valid Mermaid classDiagram block");

        // Parse the Mermaid text into design objects
        try
        {
            var (classes, edges, namespaces) = ParseMermaidClassDiagram(mermaidText);
            return MermaidGenerationResult.Generated(mermaidText, classes, edges, namespaces);
        }
        catch (Exception ex)
        {
            return MermaidGenerationResult.Generated(mermaidText, new(), new(), new(),
                $"Mermaid parsing partially failed: {ex.Message}. Raw text is available for manual import.");
        }
    }

    /// <summary>
    /// Generates a DesignGraph from a user prompt with streaming output.
    /// Yields each token delta. The caller accumulates and can display
    /// real-time progress. The final yield is a special token that
    /// signals completion.
    /// </summary>
    public async IAsyncEnumerable<MermaidStreamChunk> GenerateStreamAsync(
        string userPrompt,
        [System.Runtime.CompilerServices.EnumeratorCancellation]
        CancellationToken ct = default)
    {
        var messages = new List<LlmChatMessage>
        {
            LlmChatMessage.System(SystemPrompt),
            LlmChatMessage.User(userPrompt)
        };

        var accumulated = new StringBuilder();

        await foreach (var token in _llmService.ChatStreamAsync(messages, ct))
        {
            accumulated.Append(token);
            yield return new MermaidStreamChunk { Kind = MermaidStreamChunkKind.Token, Text = token };
        }

        // Parse the full accumulated response
        string rawResponse = accumulated.ToString();
        string mermaidText = ExtractMermaidBlock(rawResponse);

        MermaidGenerationResult result;
        if (!string.IsNullOrWhiteSpace(mermaidText))
        {
            result = ParseWithFallback(mermaidText);
        }
        else
        {
            result = MermaidGenerationResult.Failed("No valid Mermaid block found in LLM response");
        }

        yield return new MermaidStreamChunk
        {
            Kind = MermaidStreamChunkKind.Complete,
            Text = mermaidText,
            Result = result
        };
    }

    /// <summary>
    /// Wraps the parse call with a try/catch, returning a partial-success
    /// result on parse failure. Separated from the yield path so C# allows
    /// the try/catch (no yield inside try-with-catch).
    /// </summary>
    private static MermaidGenerationResult ParseWithFallback(string mermaidText)
    {
        try
        {
            var (classes, edges, namespaces) = ParseMermaidClassDiagram(mermaidText);
            return MermaidGenerationResult.Generated(mermaidText, classes, edges, namespaces);
        }
        catch (Exception ex)
        {
            return MermaidGenerationResult.Generated(mermaidText, new(), new(), new(),
                $"Parse error: {ex.Message}");
        }
    }

    // ── Mermaid block extraction ──

    /// <summary>
    /// Extracts the Mermaid classDiagram content from a raw LLM response.
    /// Handles: ```mermaid ... ```, ``` ... ```, and bare classDiagram ... text.
    /// </summary>
    internal static string ExtractMermaidBlock(string rawResponse)
    {
        // Try fenced code block: ```mermaid ... ```
        var fencedMatch = Regex.Match(rawResponse,
            @"```mermaid\s*\n(.*?)```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (fencedMatch.Success)
            return fencedMatch.Groups[1].Value.Trim();

        // Try generic fenced block: ``` ... ```
        var genericMatch = Regex.Match(rawResponse,
            @"```\s*\n(.*?)```",
            RegexOptions.Singleline);
        if (genericMatch.Success)
        {
            var content = genericMatch.Groups[1].Value.Trim();
            if (content.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase))
                return content;
        }

        // Try bare: text starting with "classDiagram"
        var bareMatch = Regex.Match(rawResponse,
            @"classDiagram\b.*",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        if (bareMatch.Success)
            return bareMatch.Value.Trim();

        return "";
    }

    // ── Mermaid classDiagram parser ──

    /// <summary>
    /// Parses a Mermaid classDiagram text into DesignClass, DesignEdge,
    /// and DesignNamespace objects. This is a lightweight parser that
    /// handles the most common Mermaid classDiagram syntax:
    ///
    /// - class declarations (with stereotypes)
    /// - member declarations (visibility + name + type)
    /// - relationship lines (--|>, ..|>, -->, o-->, *-->)
    /// - namespace blocks
    ///
    /// Does NOT handle: generic types with angle brackets in annotations,
    /// annotations, notes, or click directives.
    /// </summary>
    internal static (List<DesignClassDto> classes, List<DesignEdgeDto> edges, List<string> namespaces)
        ParseMermaidClassDiagram(string mermaidText)
    {
        var classes = new List<DesignClassDto>();
        var edges = new List<DesignEdgeDto>();
        var namespaces = new List<string>();
        var classMap = new Dictionary<string, DesignClassDto>(StringComparer.OrdinalIgnoreCase);

        string currentNamespace = "";
        var lines = mermaidText.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (line.StartsWith("%%")) continue; // comment
            if (line.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase)) continue;

            // Namespace block start
            var nsMatch = Regex.Match(line, @"^namespace\s+(\S+)\s*\{?$");
            if (nsMatch.Success)
            {
                currentNamespace = nsMatch.Groups[1].Value;
                if (!namespaces.Contains(currentNamespace))
                    namespaces.Add(currentNamespace);
                continue;
            }

            // Namespace block end
            if (line == "}" && currentNamespace != "")
            {
                currentNamespace = "";
                continue;
            }

            // Stereotype annotation: <<interface>> ClassName or ClassName : <<interface>>
            var stereoClassMatch = Regex.Match(line, @"^<<(\w+)>>\s+(\w+)$");
            if (stereoClassMatch.Success)
            {
                var stereo = stereoClassMatch.Groups[1].Value;
                var name = stereoClassMatch.Groups[2].Value;
                GetOrCreateClass(classMap, classes, name, currentNamespace).Stereotype = stereo;
                continue;
            }

            // Class declaration with stereotype: class ClassName { ... } or class ClassName
            var classMatch = Regex.Match(line, @"^class\s+(\w+)(?:\s*\{?)?$");
            if (classMatch.Success)
            {
                var name = classMatch.Groups[1].Value;
                GetOrCreateClass(classMap, classes, name, currentNamespace);
                continue;
            }

            // Class with stereotype annotation inside: class ClassName:::stereotype
            var classStereoMatch = Regex.Match(line, @"^class\s+(\w+):::(\w+)");
            if (classStereoMatch.Success)
            {
                var name = classStereoMatch.Groups[1].Value;
                var stereo = classStereoMatch.Groups[2].Value;
                var cls = GetOrCreateClass(classMap, classes, name, currentNamespace);
                cls.Stereotype = stereo;
                continue;
            }

            // Member declaration: visibility name : Type or visibility Method(params) : ReturnType
            // Mermaid classDiagram members are inside class blocks or after ClassName : member
            // Inline: ClassName : +field : Type  or  ClassName : +method(param) : ReturnType
            var inlineMemberMatch = Regex.Match(line, @"^(\w+)\s*:\s*([+\-#~])(\w+(?:\([^)]*\))?)\s*:\s*(.+)$");
            if (inlineMemberMatch.Success)
            {
                var className = inlineMemberMatch.Groups[1].Value;
                var visChar = inlineMemberMatch.Groups[2].Value[0];
                var memberSig = inlineMemberMatch.Groups[3].Value;
                var typeName = inlineMemberMatch.Groups[4].Value.Trim();

                var cls = GetOrCreateClass(classMap, classes, className, currentNamespace);
                var member = ParseMemberSignature(visChar, memberSig, typeName);
                cls.Members.Add(member);
                continue;
            }

            // Indented member: +field : Type  or  +method(param) : ReturnType  (inside class block)
            var memberMatch = Regex.Match(line, @"^([+\-#~])(\w+(?:\([^)]*\))?)\s*:\s*(.+)$");
            if (memberMatch.Success)
            {
                var visChar = memberMatch.Groups[1].Value[0];
                var memberSig = memberMatch.Groups[2].Value;
                var typeName = memberMatch.Groups[3].Value.Trim();

                // Add to the most recently created class
                if (classes.Count > 0)
                {
                    var lastClass = classes[classes.Count - 1];
                    var member = ParseMemberSignature(visChar, memberSig, typeName);
                    lastClass.Members.Add(member);
                }
                continue;
            }

            // Relationship lines:
            //   ClassA --|> ClassB       (inheritance)
            //   ClassA ..|> ClassB       (implements)
            //   ClassA --> ClassB         (association)
            //   ClassA --> ClassB : label (association with label)
            //   ClassA o--> ClassB       (aggregation)
            //   ClassA *--> ClassB       (composition)
            //   ClassA ..> ClassB        (dependency)
            var edgeMatch = Regex.Match(line,
                @"^(\w+)\s+" +                       // source class
                @"(.*?)" +                            // relationship pattern
                @"\s+(\w+)" +                          // target class
                @"(?:\s*:\s*(.+))?$",                  // optional label
                RegexOptions.IgnoreCase);
            if (edgeMatch.Success)
            {
                var sourceName = edgeMatch.Groups[1].Value;
                var relPattern = edgeMatch.Groups[2].Value.Trim();
                var targetName = edgeMatch.Groups[3].Value;
                var label = edgeMatch.Groups[4].Success ? edgeMatch.Groups[4].Value.Trim() : null;

                // Ensure both classes exist
                GetOrCreateClass(classMap, classes, sourceName, currentNamespace);
                GetOrCreateClass(classMap, classes, targetName, currentNamespace);

                var edgeKind = ParseEdgeKind(relPattern);
                edges.Add(new DesignEdgeDto
                {
                    FromClassName = sourceName,
                    ToClassName = targetName,
                    Kind = edgeKind,
                    Label = label
                });
                continue;
            }
        }

        return (classes, edges, namespaces);
    }

    private static DesignClassDto GetOrCreateClass(
        Dictionary<string, DesignClassDto> classMap,
        List<DesignClassDto> classes,
        string name,
        string currentNamespace)
    {
        if (classMap.TryGetValue(name, out var existing))
            return existing;

        var cls = new DesignClassDto
        {
            Name = name,
            Namespace = currentNamespace,
            Kind = "Class",
            Stereotype = ""
        };
        classMap[name] = cls;
        classes.Add(cls);
        return cls;
    }

    /// <summary>
    /// Parses a member signature like "Calculate" or "Calculate(param1, param2)"
    /// with its visibility and type into a DesignMemberDto.
    /// </summary>
    private static DesignMemberDto ParseMemberSignature(char visChar, string memberSig, string typeName)
    {
        var member = new DesignMemberDto
        {
            Visibility = visChar switch
            {
                '+' => "Public",
                '-' => "Private",
                '#' => "Protected",
                '~' => "Internal",
                _ => "Public"
            },
            TypeName = typeName,
            Parameters = new List<DesignParameterDto>()
        };

        // Check if it's a method (has parentheses)
        var methodMatch = Regex.Match(memberSig, @"^(\w+)\(([^)]*)\)$");
        if (methodMatch.Success)
        {
            member.Name = methodMatch.Groups[1].Value;
            member.Kind = "Method";

            // Parse parameters
            var paramsStr = methodMatch.Groups[2].Value.Trim();
            if (!string.IsNullOrWhiteSpace(paramsStr))
            {
                foreach (var param in paramsStr.Split(','))
                {
                    var paramTrimmed = param.Trim();
                    var parts = paramTrimmed.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        member.Parameters.Add(new DesignParameterDto
                        {
                            Name = parts[0].Trim(),
                            TypeName = parts[1].Trim()
                        });
                    }
                    else
                    {
                        member.Parameters.Add(new DesignParameterDto
                        {
                            Name = paramTrimmed,
                            TypeName = "object"
                        });
                    }
                }
            }
        }
        else
        {
            member.Name = memberSig;
            // Heuristic: if name starts with uppercase and typeName looks like a return type, it's a method
            // Otherwise, if it has get/set or looks like a property, mark as Property
            // Default to Property for C# style (more common than raw fields in class diagrams)
            if (typeName.Contains("get") || typeName.Contains("set") ||
                char.IsUpper(memberSig[0]) && memberSig.Length > 1 && !memberSig.StartsWith("_"))
            {
                member.Kind = "Property";
            }
            else
            {
                member.Kind = "Field";
            }
        }

        return member;
    }

    /// <summary>
    /// Determines the edge kind from the Mermaid relationship pattern.
    /// </summary>
    private static string ParseEdgeKind(string pattern)
    {
        // Normalize whitespace
        var p = Regex.Replace(pattern, @"\s+", "");

        if (p.Contains("--|>") || p.Contains("--|>")) return "Inheritance";
        if (p.Contains("..|>")) return "Implements";
        if (p.Contains("..>")) return "Dependency";
        if (p.Contains("o-->") || p.Contains("o--") || p.Contains("--o")) return "Aggregation";
        if (p.Contains("*-->") || p.Contains("*--") || p.Contains("--*")) return "Composition";
        if (p.Contains("-->")) return "Association";

        return "Association"; // default
    }
}

// ── DTO types for the parser output (no dependency on Gui.Design) ──

public sealed class DesignClassDto
{
    public string Name { get; set; } = "";
    public string Namespace { get; set; } = "";
    public string Kind { get; set; } = "Class";
    public string Stereotype { get; set; } = "";
    public List<DesignMemberDto> Members { get; set; } = new();
}

public sealed class DesignMemberDto
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "object";
    public string Kind { get; set; } = "Field";
    public string Visibility { get; set; } = "Public";
    public List<DesignParameterDto> Parameters { get; set; } = new();
}

public sealed class DesignParameterDto
{
    public string Name { get; set; } = "param";
    public string TypeName { get; set; } = "object";
}

public sealed class DesignEdgeDto
{
    public string FromClassName { get; set; } = "";
    public string ToClassName { get; set; } = "";
    public string Kind { get; set; } = "Association";
    public string? Label { get; set; }
}

// ── Result types ──

public sealed class MermaidGenerationResult
{
    /// <summary>True when the LLM returned a parseable Mermaid class diagram; false on request or parse failure.</summary>
    public bool GeneratedOk { get; init; }
    public string? Error { get; init; }
    public string? Warning { get; init; }
    public string MermaidText { get; init; } = "";
    public List<DesignClassDto> Classes { get; init; } = new();
    public List<DesignEdgeDto> Edges { get; init; } = new();
    public List<string> Namespaces { get; init; } = new();

    public static MermaidGenerationResult Failed(string error) => new()
    {
        GeneratedOk = false,
        Error = error
    };

    public static MermaidGenerationResult Generated(
        string mermaidText,
        List<DesignClassDto> classes,
        List<DesignEdgeDto> edges,
        List<string> namespaces,
        string? warning = null) => new()
    {
        GeneratedOk = true,
        Warning = warning,
        MermaidText = mermaidText,
        Classes = classes,
        Edges = edges,
        Namespaces = namespaces
    };
}

public sealed class MermaidStreamChunk
{
    public MermaidStreamChunkKind Kind { get; set; }
    public string Text { get; set; } = "";
    public MermaidGenerationResult? Result { get; set; }
}

public enum MermaidStreamChunkKind
{
    Token,
    Complete
}
