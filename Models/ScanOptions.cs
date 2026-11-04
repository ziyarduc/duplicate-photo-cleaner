using System;
using System.Collections.Generic;

namespace DuplicatePhotoCleaner.Models;

public enum DuplicateKind { Name, Content }

public enum KeepRule { Oldest, Newest, Largest, First }

public class ScanOptions
{
    public List<string> Roots { get; set; } = new();
    public List<string> IncludePatterns { get; set; } = new()
    {
        "*.jpg","*.jpeg","*.png","*.gif","*.bmp","*.webp","*.tif","*.tiff",
        "*.heic","*.heif","*.raw","*.cr2","*.nef","*.arw","*.dng"
    };
    public List<string> ExcludePatterns { get; set; } = new();
    public bool IncludeHidden { get; set; } = false;
    public int Parallelism { get; set; } = Environment.ProcessorCount;
}
