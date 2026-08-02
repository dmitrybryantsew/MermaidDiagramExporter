using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using MermaidDiagramExporter.Llm;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Lightweight view model for an attached context file row in the list.
/// </summary>
public sealed class AttachedFileItem
{
    public string FileName { get; init; } = "";
    public string FilePath { get; init; } = "";
    public string Content { get; init; } = "";
}

public partial class LlmGenerateDialog : Window
{
    private readonly LlmSettings _llmSettings;
    private CancellationTokenSource? _cts;
    private MermaidGenerationResult? _result;
    private readonly StringBuilder _streamBuffer = new();
    private readonly ObservableCollection<AttachedFileItem> _attachedFiles = new();

    /// <summary>
    /// The parsed result after a successful generation. Null until the user
    /// clicks "Apply to Design".
    /// </summary>
    public MermaidGenerationResult? AppliedResult { get; private set; }

    public LlmGenerateDialog(LlmSettings llmSettings)
    {
        _llmSettings = llmSettings;
        InitializeComponent();
        AttachedFilesList.ItemsSource = _attachedFiles;
    }

    /// <summary>
    /// Builds the full user message: attached file contents (as fenced blocks
    /// with filename headers) followed by the user's prompt text.
    /// </summary>
    private string BuildFullPrompt()
    {
        var sb = new StringBuilder();

        if (_attachedFiles.Count > 0)
        {
            sb.AppendLine("## Reference documents");
            sb.AppendLine();

            foreach (var file in _attachedFiles)
            {
                var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
                var fenceTag = ext switch
                {
                    ".md" => "markdown",
                    ".json" => "json",
                    ".yaml" or ".yml" => "yaml",
                    ".xml" => "xml",
                    ".cs" => "csharp",
                    _ => ""
                };

                sb.AppendLine($"### {file.FileName}");
                sb.AppendLine();
                sb.AppendLine($"```{fenceTag}");
                sb.AppendLine(file.Content);
                sb.AppendLine("```");
                sb.AppendLine();
            }

            sb.AppendLine("Use the documents above as context for the class diagram design.");
            sb.AppendLine();
        }

        sb.Append(PromptTextBox.Text?.Trim() ?? "");
        return sb.ToString();
    }

    // ── Context file management ──

    private async void OnAddContextFile(object? sender, RoutedEventArgs e)
    {
        var topLevel = GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Select context files (.md, .txt, .cs, .json, .yaml)",
            AllowMultiple = true
        });

        foreach (var file in files)
        {
            if (file.TryGetLocalPath() is not { } path) continue;
            if (_attachedFiles.Any(f => f.FilePath == path)) continue; // skip duplicates

            try
            {
                var content = await File.ReadAllTextAsync(path);
                _attachedFiles.Add(new AttachedFileItem
                {
                    FileName = Path.GetFileName(path),
                    FilePath = path,
                    Content = content
                });
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Failed to read {Path.GetFileName(path)}: {ex.Message}";
            }
        }
    }

    private void OnRemoveContextFile(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is AttachedFileItem item)
            _attachedFiles.Remove(item);
    }

    private void OnClearContextFiles(object? sender, RoutedEventArgs e)
    {
        _attachedFiles.Clear();
    }

    // ── Generation ──

    private async void OnGenerate(object? sender, RoutedEventArgs e)
    {
        var fullPrompt = BuildFullPrompt();
        if (string.IsNullOrWhiteSpace(PromptTextBox.Text?.Trim()))
        {
            StatusText.Text = "Enter a prompt first";
            return;
        }

        if (!_llmSettings.IsConfigured)
        {
            StatusText.Text = "LLM not configured — open Settings to set provider/model/key";
            return;
        }

        // Reset UI
        GenerateButton.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        MermaidPreviewBox.Text = "";
        WarningText.IsVisible = false;
        _streamBuffer.Clear();
        _cts = new CancellationTokenSource();

        StatusText.Text = _attachedFiles.Count > 0
            ? $"Generating (with {_attachedFiles.Count} context file(s))..."
            : "Generating...";

        try
        {
            var genService = new MermaidGeneratorService(_llmSettings);

            await foreach (var chunk in genService.GenerateStreamAsync(fullPrompt, _cts.Token))
            {
                if (chunk.Kind == MermaidStreamChunkKind.Token)
                {
                    _streamBuffer.Append(chunk.Text);
                    MermaidPreviewBox.Text = _streamBuffer.ToString();
                }
                else if (chunk.Kind == MermaidStreamChunkKind.Complete)
                {
                    _result = chunk.Result;
                    MermaidPreviewBox.Text = chunk.Text;

                    if (_result?.GeneratedOk == true)
                    {
                        StatusText.Text = $"Done — {_result.Classes.Count} classes, {_result.Edges.Count} edges";
                        ApplyButton.IsEnabled = true;

                        if (!string.IsNullOrWhiteSpace(_result.Warning))
                        {
                            WarningText.Text = _result.Warning;
                            WarningText.IsVisible = true;
                        }
                    }
                    else
                    {
                        StatusText.Text = _result?.Error ?? "Unknown error";
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Cancelled";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            GenerateButton.IsEnabled = true;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private void OnApply(object? sender, RoutedEventArgs e)
    {
        AppliedResult = _result;
        Close();
    }

    private void OnCancel(object? sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }
}
