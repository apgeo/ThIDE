using System.Collections.Generic;

namespace TherionProc.Views;

/// <summary>Pre-read file with matched token offsets, used by both the rename preview and apply logic.</summary>
internal sealed record RenameFileChanges(
    string FilePath,
    string FileText,
    List<(int Start, int Length)> Hits);
