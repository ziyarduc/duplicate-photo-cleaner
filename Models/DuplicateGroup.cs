using System.Collections.ObjectModel;
using System.Linq;

namespace DuplicatePhotoCleaner.Models;

public class DuplicateGroup
{
    public int Id { get; set; }
    public DuplicateKind Kind { get; set; }
    public string Key { get; set; } = "";
    public ObservableCollection<FileEntry> Files { get; set; } = new();

    // doğru hesap: gruptaki toplam boyut - en büyük dosya (keeper varsayımı)
    public long WastedBytes => Files.Count <= 1
        ? 0
        : Files.Sum(f => f.Size) - Files.Max(f => f.Size);

    public string DisplayKey => Kind switch
    {
        DuplicateKind.Name => $"İsim: {Key}",
        DuplicateKind.Content => $"İçerik: {Key[..System.Math.Min(12, Key.Length)]}...",
        _ => Key
    };

    public string WastedBytesFormatted => Format(WastedBytes);

    private static string Format(long b)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.##} {u[i]}";
    }
}
