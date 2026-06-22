using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Matrix;

public partial class NamespaceMatrixView : UserControl
{
    private NamespaceMatrix? _matrix;
    private const int CellSize = 28;
    private const int HeaderSize = 120;
    private readonly IBrush _headerBgBrush = new SolidColorBrush(Color.Parse("#252B33"));
    private readonly IBrush _headerTextBrush = new SolidColorBrush(Color.Parse("#B0B8C4"));
    private readonly IBrush _cellBorderBrush = new SolidColorBrush(Color.Parse("#3A4250"));
    private readonly IBrush _textBrush = new SolidColorBrush(Color.Parse("#E0E4EA"));

    /// <summary>
    /// Raised when the user clicks a cell. Arguments: (fromNamespace, toNamespace).
    /// </summary>
    public event Action<string, string>? CellClicked;

    public NamespaceMatrixView()
    {
        InitializeComponent();
        CloseButton.Click += (s, e) => IsVisible = false;
        MatrixCanvas.PointerPressed += OnCanvasPointerPressed;
    }

    /// <summary>
    /// Builds and renders the matrix from a TypeGraph.
    /// </summary>
    public void SetGraph(TypeGraph graph)
    {
        _matrix = NamespaceMatrixBuilder.Build(graph);
        RenderMatrix();
    }

    private void RenderMatrix()
    {
        MatrixCanvas.Children.Clear();
        if (_matrix == null || _matrix.Namespaces.Count == 0) return;

        int n = _matrix.Namespaces.Count;
        int canvasW = HeaderSize + n * CellSize;
        int canvasH = HeaderSize + n * CellSize;
        MatrixCanvas.Width = canvasW;
        MatrixCanvas.Height = canvasH;

        int maxCount = _matrix.Cells.Count > 0 ? _matrix.Cells.Values.Max() : 1;
        var circularPairs = new HashSet<(int, int)>();
        foreach (var (a, b) in _matrix.FindTwoWayDependencies())
        {
            circularPairs.Add((a, b));
            circularPairs.Add((b, a));
        }

        for (int row = 0; row < n; row++)
        {
            for (int col = 0; col < n; col++)
            {
                float x = HeaderSize + col * CellSize;
                float y = HeaderSize + row * CellSize;

                // Cell background
                IBrush cellBrush;
                if (row == col)
                {
                    cellBrush = new SolidColorBrush(Color.Parse("#1E242C"));
                }
                else if (circularPairs.Contains((row, col)))
                {
                    cellBrush = new SolidColorBrush(Color.Parse("#FF6040"));
                }
                else
                {
                    int count = _matrix.GetCount(row, col);
                    if (count == 0)
                        cellBrush = new SolidColorBrush(Color.Parse("#1A1F26"));
                    else
                    {
                        float intensity = (float)count / maxCount;
                        byte r = (byte)(0x1A + intensity * 0x40);
                        byte g = (byte)(0x30 + intensity * 0x80);
                        byte b = (byte)(0x20 + intensity * 0x20);
                        cellBrush = new SolidColorBrush(Color.FromArgb(255, r, g, b));
                    }
                }

                var cell = new Border
                {
                    Width = CellSize,
                    Height = CellSize,
                    Background = cellBrush,
                    BorderBrush = _cellBorderBrush,
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(2)
                };

                // Count label
                int cellCount = _matrix.GetCount(row, col);
                if (cellCount > 0)
                {
                    var label = new TextBlock
                    {
                        Text = cellCount.ToString(),
                        FontSize = 9,
                        Foreground = _textBrush,
                        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
                    };
                    cell.Child = label;
                }

                Canvas.SetLeft(cell, x);
                Canvas.SetTop(cell, y);
                MatrixCanvas.Children.Add(cell);
            }

            // Row header
            var rowHeader = new Border
            {
                Width = HeaderSize - 4,
                Height = CellSize,
                Background = _headerBgBrush,
                Padding = new Thickness(4, 0, 0, 0),
                Child = new TextBlock
                {
                    Text = TruncateNamespace(_matrix.Namespaces[row]),
                    FontSize = 10,
                    Foreground = _headerTextBrush,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis
                }
            };
            Canvas.SetLeft(rowHeader, 0);
            Canvas.SetTop(rowHeader, HeaderSize + row * CellSize);
            MatrixCanvas.Children.Add(rowHeader);

            // Column header
            var colHeader = new Border
            {
                Width = CellSize,
                Height = HeaderSize - 4,
                Background = _headerBgBrush,
                Child = new TextBlock
                {
                    Text = TruncateNamespace(_matrix.Namespaces[row]),
                    FontSize = 9,
                    Foreground = _headerTextBrush,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                    TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
                    RenderTransform = new RotateTransform(-45),
                    Margin = new Thickness(0, 0, 0, 4)
                }
            };
            Canvas.SetLeft(colHeader, HeaderSize + row * CellSize);
            Canvas.SetTop(colHeader, 0);
            MatrixCanvas.Children.Add(colHeader);
        }
    }

    private void OnCanvasPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_matrix == null) return;
        var pos = e.GetPosition(MatrixCanvas);
        int col = (int)((pos.X - HeaderSize) / CellSize);
        int row = (int)((pos.Y - HeaderSize) / CellSize);
        int n = _matrix.Namespaces.Count;
        if (row >= 0 && row < n && col >= 0 && col < n && row != col)
        {
            CellClicked?.Invoke(_matrix.Namespaces[row], _matrix.Namespaces[col]);
        }
    }

    private static string TruncateNamespace(string ns)
    {
        if (ns.Length <= 20) return ns;
        // Show last two segments
        var parts = ns.Split('.');
        if (parts.Length >= 2)
            return ".." + parts[^2] + "." + parts[^1];
        return ns[..20] + "…";
    }
}
