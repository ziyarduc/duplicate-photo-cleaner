using System.Collections.Generic;

namespace DuplicatePhotoCleaner.Models;

public class DuplicateGroup
{
    public int Id { get; set; }
    public DuplicateKind Kind { get; set; }
    public string Key { get; set; } = "";
    public List<FileEntry> Files { get; set; } = new();
    public long WastedBytes => Files.Count <= 1 ? 0 : Files[0].Size * (Files.Count - 1);
    public string DisplayKey => Kind switch
    {
        DuplicateKind.Name => $"İsim: {Key}",
        DuplicateKind.Content => $"İçerik: {Key[..Math.Min(12, Key.Length)]}...",
        DuplicateKind.Perceptual => $"Benzer: {Key}",
        _ => Key
    };
}
