using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Extraction;

public class TypeScriptSubprocessScanner : ITypeScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public TypeGraph ScanFolder(string folderPath, GraphBuildOptions options)
    {
        if (!IsNodeInstalled())
        {
            throw new InvalidOperationException("Node.js is not installed or not in PATH, which is required to scan TypeScript projects.");
        }

        string executablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Extraction", "ts-scanner.js");
        if (!File.Exists(executablePath))
        {
            // Fallback for when running in different context or nested bin folders
            executablePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ts-scanner.js");
        }
        if (!File.Exists(executablePath))
        {
            throw new FileNotFoundException($"Cannot find ts-scanner.js at {executablePath}");
        }

        string optionsJson = JsonSerializer.Serialize(options, JsonOptions);

        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            ArgumentList = { executablePath, folderPath, optionsJson },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new Exception("Failed to start Node.js process.");
        }

        string output = process.StandardOutput.ReadToEnd();
        string error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new Exception($"TypeScript scanner failed with exit code {process.ExitCode}:\n{error}\n{output}");
        }

        try
        {
            var graph = JsonSerializer.Deserialize<TypeGraphDto>(output, JsonOptions);
            return graph?.ToTypeGraph() ?? new TypeGraph(
                title: "Empty Graph",
                nodes: Array.Empty<TypeNodeData>(),
                edges: Array.Empty<TypeEdgeData>(),
                groups: Array.Empty<TypeGroupData>(),
                metadata: new TypeGraphMetadata { SourceDescription = "Empty TS output" });
        }
        catch (JsonException ex)
        {
            throw new Exception($"Failed to parse TypeScript scanner output. Output was:\n{output}\nError: {ex.Message}", ex);
        }
    }

    private bool IsNodeInstalled()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(startInfo);
            process?.WaitForExit();
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // TypeGraph constructor does not have a parameterless constructor and requires specific parameters
    // We use a DTO to deserialize the JSON and then map it.
    private class TypeGraphDto
    {
        public string Title { get; set; } = string.Empty;
        public TypeNodeData[] Nodes { get; set; } = Array.Empty<TypeNodeData>();
        public TypeEdgeData[] Edges { get; set; } = Array.Empty<TypeEdgeData>();
        public TypeGroupData[] Groups { get; set; } = Array.Empty<TypeGroupData>();
        public TypeGraphMetadata Metadata { get; set; } = new();

        public TypeGraph ToTypeGraph()
        {
            return new TypeGraph(Title, Nodes, Edges, Groups, Metadata);
        }
    }
}
