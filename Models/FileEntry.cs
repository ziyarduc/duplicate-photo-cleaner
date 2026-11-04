using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;

namespace DuplicatePhotoCleaner.Models;

public class FileEntry : INotifyPropertyChanged
{
    public string Path { get; set; } = "";
    public string Name { get; set; } = "";
    public string NameNoExt { get; set; } = "";
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public DateTime Created { get; set; }
    public string? ContentHash { get; set; }
    public string? PerceptualHash { get; set; }
    public bool DecodeFailed { get; set; }

    private bool _isKept = true;
    public bool IsKept
    {
        get => _isKept;
        set => SetField(ref _isKept, value);
    }

    private bool _isSelectedForDeletion;
    public bool IsSelectedForDeletion
    {
        get => _isSelectedForDeletion;
        set => SetField(ref _isSelectedForDeletion, value);
    }

    public string Extension => System.IO.Path.GetExtension(Name).ToLowerInvariant();
    public string Directory => System.IO.Path.GetDirectoryName(Path) ?? "";
    public string SizeFormatted
    {
        get
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            double v = Size; int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return $"{v:0.##} {u[i]}";
        }
    }
    public string ModifiedFormatted => Modified.ToString("yyyy-MM-dd HH:mm");

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
