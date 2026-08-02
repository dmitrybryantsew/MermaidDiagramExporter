using System;

namespace MermaidDiagramExporter.Core;

public interface ITypeScanner
{
    TypeGraph ScanFolder(string folderPath, GraphBuildOptions options);
}
