using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuplicatePhotoCleaner.Models;

namespace DuplicatePhotoCleaner.Core;

public class Scanner
{
    public static readonly string[] SystemDirs = new[]
    {
        @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"
    };

    public async Task<List<FileEntry>> ScanAsync(ScanOptions opts, IProgress<string>? progress, CancellationToken ct)
    {
        var result = new List<FileEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            foreach (var root in opts.Roots)
            {
                if (!Directory.Exists(root)) continue;
                Enumerate(root, opts, result, seen, progress, ct);
            }
        }, ct).ConfigureAwait(false);

        return result;
    }

    private void Enumerate(string dir, ScanOptions opts, List<FileEntry> result, HashSet<string> seen, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (!opts.IncludeSystemDirs && IsSystemDir(dir)) return;

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                if (!Matches(file, opts)) continue;
                var fi = new FileInfo(file);
                if (fi.Length < opts.MinSizeBytes) continue;
                if (opts.MaxSizeBytes > 0 && fi.Length > opts.MaxSizeBytes) continue;
                if (opts.FromDate.HasValue && fi.LastWriteTime < opts.FromDate.Value) continue;
                if (opts.ToDate.HasValue && fi.LastWriteTime > opts.ToDate.Value) continue;
                if (!opts.IncludeHidden && (fi.Attributes & FileAttributes.Hidden) != 0) continue;

                var key = fi.FullName.ToLowerInvariant();
                if (!seen.Add(key)) continue;

                var entry = new FileEntry
                {
                    Path = fi.FullName,
                    Name = fi.Name,
                    NameNoExt = Path.GetFileNameWithoutExtension(fi.Name),
                    Size = fi.Length,
                    Modified = fi.LastWriteTime,
                    Created = fi.CreationTime
                };
                lock (result) result.Add(entry);
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                ct.ThrowIfCancellationRequested();
                var name = Path.GetFileName(sub);
                if (!opts.IncludeHidden && (new DirectoryInfo(sub).Attributes & FileAttributes.Hidden) != 0) continue;
                if (name.Equals("$Recycle.Bin", StringComparison.OrdinalIgnoreCase)) continue;
                Enumerate(sub, opts, result, seen, progress, ct);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }
        catch (Exception) { }

        progress?.Report(dir);
    }

    private bool IsSystemDir(string dir)
    {
        foreach (var s in SystemDirs)
            if (dir.StartsWith(s, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool Matches(string path, ScanOptions opts)
    {
        var name = Path.GetFileName(path);
        var ext = Path.GetExtension(name).ToLowerInvariant();
        bool included = opts.IncludePatterns.Count == 0;
        foreach (var pat in opts.IncludePatterns)
        {
            if (WildcardMatch(name, pat)) { included = true; break; }
        }
        if (!included) return false;
        foreach (var pat in opts.ExcludePatterns)
            if (WildcardMatch(name, pat)) return false;
        return true;
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        // simple glob: * and ?
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }
}
