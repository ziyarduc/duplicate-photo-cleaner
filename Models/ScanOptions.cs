using System;

namespace DuplicatePhotoCleaner.Models;

public enum DuplicateKind { Name, Content, Perceptual }

public enum KeepRule { Oldest, Newest, Largest, Smallest, ShortestPath, First }

public class ScanOptions
{
    public System.Collections.Generic.List<string> Roots { get; set; } = new();
    public System.Collections.Generic.List<string> IncludePatterns { get; set; } = new() { "*.jpg","*.jpeg","*.png","*.gif","*.bmp","*.webp","*.tif","*.tiff","*.heic","*.heif","*.raw","*.cr2","*.nef","*.arw","*.dng" };
    public System.Collections.Generic.List<string> ExcludePatterns { get; set; } = new();
    public long MinSizeBytes { get; set; } = 0;
    public long MaxSizeBytes { get; set; } = 0; // 0 = unlimited
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public bool IncludeHidden { get; set; } = false;
    public bool IncludeSystemDirs { get; set; } = false;
    public bool ScanNameDuplicates { get; set; } = true;
    public bool ScanContentDuplicates { get; set; } = true;
    public bool ScanPerceptualDuplicates { get; set; } = false;
    public int PerceptualThreshold { get; set; } = 8;
    public bool UseCache { get; set; } = true;
    public KeepRule KeepRule { get; set; } = KeepRule.Oldest;
    public int Parallelism { get; set; } = Environment.ProcessorCount;
}
