using MermaidDiagramExporter.Core;

namespace MermaidDiagramExporter.Gui.Design;

/// <summary>
/// Lightweight wrapper for member rows in the inspector list.
/// Supports inline editing: Name, TypeName, Visibility cycling.
/// Holds a reference to the underlying DesignMember and the class ID
/// so changes can be committed back through the undo system.
/// </summary>
public sealed class MemberRow
{
    private readonly DesignMember _member;

    public MemberRow(string classId, DesignMember member, int index)
    {
        ClassId = classId;
        _member = member;
        Index = index;
    }

    public string ClassId { get; }
    public int Index { get; }

    /// <summary>The member ID (stable across renames).</summary>
    public string MemberId => _member.Id;

    /// <summary>Editable member name.</summary>
    public string Name
    {
        get => _member.Name;
        set => _member.Name = value;
    }

    /// <summary>Editable type name.</summary>
    public string TypeName
    {
        get => _member.TypeName ?? "";
        set => _member.TypeName = value;
    }

    /// <summary>Visibility badge: + - # ~</summary>
    public string VisibilityBadge => _member.Visibility switch
    {
        Visibility.Public => "+",
        Visibility.Private => "-",
        Visibility.Protected => "#",
        Visibility.Internal => "~",
        _ => "+"
    };

    /// <summary>Color for the visibility badge.</summary>
    public string VisibilityColor => _member.Visibility switch
    {
        Visibility.Public => "#4CAF50",
        Visibility.Private => "#F44336",
        Visibility.Protected => "#FF9800",
        Visibility.Internal => "#2196F3",
        _ => "#4CAF50"
    };

    /// <summary>Kind badge: F P M C E</summary>
    public string KindBadge => _member.Kind switch
    {
        MemberKind.Field => "F",
        MemberKind.Property => "P",
        MemberKind.Method => "M",
        MemberKind.Constructor => "C",
        MemberKind.Event => "E",
        _ => "?"
    };

    /// <summary>Legacy display text for backwards compat.</summary>
    public string DisplayText
    {
        get
        {
            string vis = _member.Visibility switch
            {
                Visibility.Public => "+",
                Visibility.Private => "-",
                Visibility.Protected => "#",
                Visibility.Internal => "~",
                _ => "+"
            };
            string typeStr = string.IsNullOrEmpty(_member.TypeName) ? "" : $" : {_member.TypeName}";
            return $"{vis} {_member.Name}{typeStr}";
        }
    }
}

/// <summary>
/// Lightweight wrapper for edge rows in the inspector list.
/// Top-level class (not a record) so Avalonia XAML can resolve the
/// DisplayText/Kind properties for binding.
/// </summary>
public sealed class EdgeRow
{
    public EdgeRow(string id, string kind, string displayText)
    {
        Id = id;
        Kind = kind;
        DisplayText = displayText;
    }
    public string Id { get; }
    public string Kind { get; set; }
    public string DisplayText { get; }
}
