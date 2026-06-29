using System.Collections.Generic;
using System.Linq;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Concrete undoable commands for Design Mode mutations. Each captures the
/// state needed to undo itself.
/// </summary>
public static class DesignCommands
{
    /// <summary>
    /// Adds a class. Undo removes it (and any edges referencing it).
    /// </summary>
    public sealed class AddClass : DesignCommand
    {
        private readonly DesignClass _class;
        public AddClass(DesignClass cls) => _class = cls;
        public override string Description => $"Add class {_class.Name}";
        public override void Apply(DesignGraph graph) => graph.Classes.Add(_class);
        public override void Undo(DesignGraph graph)
        {
            graph.Classes.RemoveAll(c => c.Id == _class.Id);
            graph.Edges.RemoveAll(e => e.FromClassId == _class.Id || e.ToClassId == _class.Id);
        }
    }

    /// <summary>
    /// Removes a class (and its edges). Undo restores the class and edges.
    /// Captures a snapshot of the class state at construction time so undo
    /// works even if the class is later mutated.
    /// </summary>
    public sealed class RemoveClass : DesignCommand
    {
        private readonly DesignClass _classSnapshot;
        private readonly List<DesignEdge> _removedEdges = new();
        private readonly int _originalIndex;
        public RemoveClass(DesignGraph graph, string classId)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
            if (cls != null)
            {
                // Deep-copy the class so undo restores the original state
                _classSnapshot = new DesignClass
                {
                    Id = cls.Id,
                    Name = cls.Name,
                    Namespace = cls.Namespace,
                    Kind = cls.Kind,
                    X = cls.X,
                    Y = cls.Y,
                    Width = cls.Width,
                    Height = cls.Height,
                    Stereotype = cls.Stereotype,
                    Members = cls.Members.Select(m => new DesignMember
                    {
                        Id = m.Id,
                        Kind = m.Kind,
                        Name = m.Name,
                        TypeName = m.TypeName,
                        Visibility = m.Visibility,
                        Parameters = m.Parameters.Select(p => new DesignParameter
                        {
                            Name = p.Name,
                            TypeName = p.TypeName
                        }).ToList()
                    }).ToList()
                };
                _originalIndex = graph.Classes.IndexOf(cls);
                _removedEdges.AddRange(graph.Edges.Where(e => e.FromClassId == classId || e.ToClassId == classId)
                    .Select(e => new DesignEdge
                    {
                        Id = e.Id,
                        FromClassId = e.FromClassId,
                        ToClassId = e.ToClassId,
                        Kind = e.Kind,
                        Label = e.Label
                    }));
            }
            else
            {
                _classSnapshot = new DesignClass();
                _originalIndex = -1;
            }
        }
        public override string Description => $"Remove class {_classSnapshot.Name}";
        public override void Apply(DesignGraph graph)
        {
            graph.Classes.RemoveAll(c => c.Id == _classSnapshot.Id);
            graph.Edges.RemoveAll(e => _removedEdges.Any(r => r.Id == e.Id));
        }
        public override void Undo(DesignGraph graph)
        {
            if (_originalIndex >= 0 && _originalIndex <= graph.Classes.Count)
                graph.Classes.Insert(_originalIndex, _classSnapshot);
            else
                graph.Classes.Add(_classSnapshot);
            foreach (var edge in _removedEdges)
                graph.Edges.Add(edge);
        }
    }

    /// <summary>
    /// Moves a class. Undo restores the original position.
    /// </summary>
    public sealed class MoveClass : DesignCommand
    {
        private readonly string _classId;
        private readonly float _oldX, _oldY, _newX, _newY;
        public MoveClass(string classId, float oldX, float oldY, float newX, float newY)
        {
            _classId = classId; _oldX = oldX; _oldY = oldY; _newX = newX; _newY = newY;
        }
        public override string Description => "Move class";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) { cls.X = _newX; cls.Y = _newY; }
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) { cls.X = _oldX; cls.Y = _oldY; }
        }
    }

    /// <summary>
    /// Resizes a class. Undo restores the original size.
    /// </summary>
    public sealed class ResizeClass : DesignCommand
    {
        private readonly string _classId;
        private readonly float _oldW, _oldH, _newW, _newH;
        public ResizeClass(string classId, float oldW, float oldH, float newW, float newH)
        {
            _classId = classId; _oldW = oldW; _oldH = oldH; _newW = newW; _newH = newH;
        }
        public override string Description => "Resize class";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) { cls.Width = _newW; cls.Height = _newH; }
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) { cls.Width = _oldW; cls.Height = _oldH; }
        }
    }

    /// <summary>
    /// Renames a class. Undo restores the original name.
    /// </summary>
    public sealed class RenameClass : DesignCommand
    {
        private readonly string _classId;
        private readonly string _oldName, _newName;
        public RenameClass(string classId, string oldName, string newName)
        {
            _classId = classId; _oldName = oldName; _newName = newName;
        }
        public override string Description => "Rename class";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) cls.Name = _newName;
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) cls.Name = _oldName;
        }
    }

    /// <summary>
    /// Changes a class's kind (Class/Interface/Enum/Struct/Static/Abstract).
    /// Undo restores the original kind. Per docs/design/10.
    /// </summary>
    public sealed class ChangeClassKind : DesignCommand
    {
        private readonly string _classId;
        private readonly ClassKind _oldKind, _newKind;
        public ChangeClassKind(string classId, ClassKind oldKind, ClassKind newKind)
        {
            _classId = classId; _oldKind = oldKind; _newKind = newKind;
        }
        public override string Description => "Change class kind";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) cls.Kind = _newKind;
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) cls.Kind = _oldKind;
        }
    }

    /// <summary>
    /// Changes a class's namespace. Undo restores the original namespace.
    /// Per docs/design/10.
    /// </summary>
    public sealed class ChangeNamespace : DesignCommand
    {
        private readonly string _classId;
        private readonly string _oldNamespace, _newNamespace;
        public ChangeNamespace(string classId, string oldNamespace, string newNamespace)
        {
            _classId = classId; _oldNamespace = oldNamespace; _newNamespace = newNamespace;
        }
        public override string Description => "Change namespace";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) cls.Namespace = _newNamespace;
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null) cls.Namespace = _oldNamespace;
        }
    }

    /// <summary>
    /// Adds an edge. Undo removes it.
    /// </summary>
    public sealed class AddEdge : DesignCommand
    {
        private readonly DesignEdge _edge;
        public AddEdge(DesignEdge edge) => _edge = edge;
        public override string Description => "Add edge";
        public override void Apply(DesignGraph graph) => graph.Edges.Add(_edge);
        public override void Undo(DesignGraph graph) => graph.Edges.RemoveAll(e => e.Id == _edge.Id);
    }

    /// <summary>
    /// Removes an edge. Undo restores it.
    /// </summary>
    public sealed class RemoveEdge : DesignCommand
    {
        private readonly DesignEdge _edge;
        public RemoveEdge(DesignGraph graph, string edgeId)
            => _edge = graph.Edges.FirstOrDefault(e => e.Id == edgeId);
        public override string Description => "Remove edge";
        public override void Apply(DesignGraph graph) => graph.Edges.RemoveAll(e => e.Id == _edge.Id);
        public override void Undo(DesignGraph graph) => graph.Edges.Add(_edge);
    }

    /// <summary>
    /// Changes an edge's type. Undo restores the original type.
    /// </summary>
    public sealed class ChangeEdgeType : DesignCommand
    {
        private readonly string _edgeId;
        private readonly EdgeKind _oldKind, _newKind;
        public ChangeEdgeType(string edgeId, EdgeKind oldKind, EdgeKind newKind)
        {
            _edgeId = edgeId; _oldKind = oldKind; _newKind = newKind;
        }
        public override string Description => "Change edge type";
        public override void Apply(DesignGraph graph)
        {
            var edge = graph.Edges.FirstOrDefault(e => e.Id == _edgeId);
            if (edge != null) edge.Kind = _newKind;
        }
        public override void Undo(DesignGraph graph)
        {
            var edge = graph.Edges.FirstOrDefault(e => e.Id == _edgeId);
            if (edge != null) edge.Kind = _oldKind;
        }
    }

    /// <summary>
    /// Changes an edge's source or target endpoint. Undo restores the
    /// original endpoint. Used when the user drags an edge from one class
    /// to another.
    /// </summary>
    public sealed class ChangeEdgeEndpoint : DesignCommand
    {
        private readonly string _edgeId;
        private readonly bool _isSource;
        private readonly string _oldClassId, _newClassId;
        public ChangeEdgeEndpoint(string edgeId, bool isSource, string oldClassId, string newClassId)
        {
            _edgeId = edgeId; _isSource = isSource; _oldClassId = oldClassId; _newClassId = newClassId;
        }
        public override string Description => "Change edge endpoint";
        public override void Apply(DesignGraph graph)
        {
            var edge = graph.Edges.FirstOrDefault(e => e.Id == _edgeId);
            if (edge == null) return;
            if (_isSource) edge.FromClassId = _newClassId;
            else edge.ToClassId = _newClassId;
        }
        public override void Undo(DesignGraph graph)
        {
            var edge = graph.Edges.FirstOrDefault(e => e.Id == _edgeId);
            if (edge == null) return;
            if (_isSource) edge.FromClassId = _oldClassId;
            else edge.ToClassId = _oldClassId;
        }
    }

    /// <summary>
    /// Adds a member to a class. Undo removes it.
    /// </summary>
    public sealed class AddMember : DesignCommand
    {
        private readonly string _classId;
        private readonly DesignMember _member;
        public AddMember(string classId, DesignMember member)
        {
            _classId = classId; _member = member;
        }
        public override string Description => $"Add member {_member.Name}";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            cls?.Members.Add(_member);
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            cls?.Members.RemoveAll(m => m.Id == _member.Id);
        }
    }

    /// <summary>
    /// Removes a member from a class. Undo restores it.
    /// </summary>
    public sealed class RemoveMember : DesignCommand
    {
        private readonly string _classId;
        private readonly DesignMember _member;
        private readonly int _originalIndex;
        public RemoveMember(DesignGraph graph, string classId, int memberIndex)
        {
            _classId = classId;
            var cls = graph.Classes.FirstOrDefault(c => c.Id == classId);
            if (cls != null && memberIndex >= 0 && memberIndex < cls.Members.Count)
            {
                _member = cls.Members[memberIndex];
                _originalIndex = memberIndex;
            }
        }
        public override string Description => $"Remove member {_member?.Name}";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            cls?.Members.RemoveAll(m => m.Id == _member.Id);
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls != null && _member != null)
            {
                if (_originalIndex >= 0 && _originalIndex <= cls.Members.Count)
                    cls.Members.Insert(_originalIndex, _member);
                else
                    cls.Members.Add(_member);
            }
        }
    }

    /// <summary>
    /// Renames a member. Undo restores the original name.
    /// </summary>
    public sealed class RenameMember : DesignCommand
    {
        private readonly string _classId;
        private readonly string _memberId;
        private readonly string _oldName, _newName;
        public RenameMember(string classId, string memberId, string oldName, string newName)
        {
            _classId = classId; _memberId = memberId; _oldName = oldName; _newName = newName;
        }
        public override string Description => "Rename member";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            var member = cls?.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member != null) member.Name = _newName;
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            var member = cls?.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member != null) member.Name = _oldName;
        }
    }

    /// <summary>
    /// Changes a member's type. Undo restores the original type.
    /// </summary>
    public sealed class ChangeMemberType : DesignCommand
    {
        private readonly string _classId;
        private readonly string _memberId;
        private readonly string _oldType, _newType;
        public ChangeMemberType(string classId, string memberId, string oldType, string newType)
        {
            _classId = classId; _memberId = memberId; _oldType = oldType; _newType = newType;
        }
        public override string Description => "Change member type";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            var member = cls?.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member != null) member.TypeName = _newType;
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            var member = cls?.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member != null) member.TypeName = _oldType;
        }
    }

    /// <summary>
    /// Cycles a member's visibility. Undo restores the original visibility.
    /// </summary>
    public sealed class CycleMemberVisibility : DesignCommand
    {
        private readonly string _classId;
        private readonly string _memberId;
        private readonly Visibility _oldVisibility, _newVisibility;
        public CycleMemberVisibility(string classId, string memberId, Visibility oldVisibility, Visibility newVisibility)
        {
            _classId = classId; _memberId = memberId; _oldVisibility = oldVisibility; _newVisibility = newVisibility;
        }
        public override string Description => "Change member visibility";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            var member = cls?.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member != null) member.Visibility = _newVisibility;
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            var member = cls?.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member != null) member.Visibility = _oldVisibility;
        }
    }

    /// <summary>
    /// Moves a member within a class. Undo restores the original position.
    /// </summary>
    public sealed class MoveMember : DesignCommand
    {
        private readonly string _classId;
        private readonly string _memberId;
        private readonly int _oldIndex, _newIndex;
        public MoveMember(string classId, string memberId, int oldIndex, int newIndex)
        {
            _classId = classId; _memberId = memberId; _oldIndex = oldIndex; _newIndex = newIndex;
        }
        public override string Description => "Move member";
        public override void Apply(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls == null) return;
            var member = cls.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member == null) return;
            cls.Members.Remove(member);
            if (_newIndex >= 0 && _newIndex <= cls.Members.Count)
                cls.Members.Insert(_newIndex, member);
            else
                cls.Members.Add(member);
        }
        public override void Undo(DesignGraph graph)
        {
            var cls = graph.Classes.FirstOrDefault(c => c.Id == _classId);
            if (cls == null) return;
            var member = cls.Members.FirstOrDefault(m => m.Id == _memberId);
            if (member == null) return;
            cls.Members.Remove(member);
            if (_oldIndex >= 0 && _oldIndex <= cls.Members.Count)
                cls.Members.Insert(_oldIndex, member);
            else
                cls.Members.Add(member);
        }
    }
}
