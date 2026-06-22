using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace MermaidDiagramExporter.Gui.Persistence;

public partial class CachePromptDialog : Window
{
    public CachePromptDialog()
    {
        InitializeComponent();
        RescanButton.Click += (s, e) => { Result = CachePromptResult.Rescan; Close(); };
        LoadCacheButton.Click += (s, e) => { Result = CachePromptResult.LoadCache; Close(); };
    }

    public CachePromptResult Result { get; private set; } = CachePromptResult.Cancelled;

    public void SetInfo(CacheInfo info, CacheValidationResult validation)
    {
        CacheInfoText.Text = $"Last scan: {info.LastScanUtc:yyyy-MM-dd HH:mm UTC} | Files: {info.TotalFiles}";
        ValidationText.Text = validation switch
        {
            CacheValidationResult.UpToDate => "Source files are unchanged.",
            CacheValidationResult.MinorChanges => "Minor source changes detected (under 10%).",
            _ => "Source has changed significantly. Rescan recommended."
        };
    }
}

public enum CachePromptResult
{
    Rescan,
    LoadCache,
    Cancelled
}
