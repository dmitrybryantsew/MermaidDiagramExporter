using System.Collections.Generic;

namespace MermaidDiagramExporter.Gui.Search;

public class SearchResultViewModel
{
    public string NodeId { get; set; } = "";
    public string DisplayLabel { get; set; } = "";
    public string NodeKind { get; set; } = "";
    public string NodeNamespace { get; set; } = "";
    public string FileName { get; set; } = "";
    public List<MatchedMemberViewModel> MatchedMembers { get; set; } = new();
}

public class MatchedMemberViewModel
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string Kind { get; set; } = "";
}
