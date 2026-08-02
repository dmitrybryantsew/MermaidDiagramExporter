using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Selection-driven view model for the Design Mode inspector panel. Exposes
/// four states (Nothing, SingleClass, SingleMember, MultiSelect) so the
/// inspector XAML can swap between them via IsVisible bindings. Per
/// docs/design/10.
/// </summary>
public sealed class DesignInspectorViewModel : INotifyPropertyChanged
{
    private readonly DesignCanvasController _controller;
    private DesignSelection _selection;
    private DesignGraph? _graph;

    public DesignInspectorViewModel(DesignCanvasController controller, DesignGraph graph)
    {
        _controller = controller;
        _graph = graph;
        _selection = controller.Selection;
        _controller.SelectionChanged += (_, sel) =>
        {
            _selection = sel;
            RaiseAll();
        };
        _controller.GraphMutated += (_, _) => RaiseAll();
    }

    public string StateKind => _selection.SelectedClassIds.Count switch
    {
        0 => "Nothing",
        1 => "SingleClass",
        _ => "MultiSelect"
    };

    public DesignClass? SelectedClass => _selection.SelectedClassIds.Count == 1
        ? _graph?.Classes.FirstOrDefault(c => c.Id == _selection.SelectedClassIds[0])
        : null;

    public IReadOnlyList<DesignClass> SelectedClasses =>
        _graph?.Classes.Where(c => _selection.SelectedClassIds.Contains(c.Id)).ToList()
        ?? new List<DesignClass>();

    public IReadOnlyList<DesignEdge> OutgoingEdges =>
        SelectedClass == null || _graph == null
            ? new List<DesignEdge>()
            : _graph.Edges.Where(e => e.FromClassId == SelectedClass.Id).ToList();

    public IReadOnlyList<DesignEdge> IncomingEdges =>
        SelectedClass == null || _graph == null
            ? new List<DesignEdge>()
            : _graph.Edges.Where(e => e.ToClassId == SelectedClass.Id).ToList();

    public IReadOnlyList<DesignClass> UnnamedClasses =>
        _graph?.Classes.Where(c => string.IsNullOrWhiteSpace(c.Name)).ToList()
        ?? new List<DesignClass>();

    public int ClassCount => _graph?.Classes.Count ?? 0;
    public int EdgeCount => _graph?.Edges.Count ?? 0;
    public int UnnamedCount => UnnamedClasses.Count;

    /// <summary>
    /// Returns the display name of the class on the other end of an edge,
    /// or "(deleted)" if the class no longer exists.
    /// </summary>
    public string GetOtherClassName(DesignEdge edge)
    {
        if (_graph == null) return "(unknown)";
        var otherId = edge.FromClassId == SelectedClass?.Id ? edge.ToClassId : edge.FromClassId;
        var other = _graph.Classes.FirstOrDefault(c => c.Id == otherId);
        return other?.Name ?? "(deleted)";
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void RaiseAll()
    {
        OnPropertyChanged(nameof(StateKind));
        OnPropertyChanged(nameof(SelectedClass));
        OnPropertyChanged(nameof(SelectedClasses));
        OnPropertyChanged(nameof(OutgoingEdges));
        OnPropertyChanged(nameof(IncomingEdges));
        OnPropertyChanged(nameof(UnnamedClasses));
        OnPropertyChanged(nameof(ClassCount));
        OnPropertyChanged(nameof(EdgeCount));
        OnPropertyChanged(nameof(UnnamedCount));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
