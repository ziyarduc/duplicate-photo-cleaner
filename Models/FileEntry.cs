using System;

namespace DuplicatePhotoCleaner.Models;

public class FileEntry
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameNoExt { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public DateTime Created { get; set; }
    public string? ContentHash { get; set; }
    public string? PerceptualHash { get; set; }
    public bool IsKept { get; set; } = true;
    public bool IsSelectedForDeletion { get; set; } = false;
    public bool DecodeFailed { get; set; }
    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
}
