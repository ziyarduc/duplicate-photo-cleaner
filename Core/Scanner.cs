using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuplicatePhotoCleaner.Models;

namespace DuplicatePhotoCleaner.Core;

public class Scanner
{
    // tam eşleşme yapılacak sistem kök dizinleri (prefix değil boundary kontrolü Enumerate içinde)
    private static readonly string[] SystemRoots =
    {
        @"C:\Windows", @"C:\Program Files", @"C:\Program Files (x86)", @"C:\ProgramData"
    };

    // taramaya gerek olmayan çöp klasörler
    private static readonly HashSet<string> JunkDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "node_modules", ".git", ".svn", ".hg", "__pycache__"
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
            ct.ThrowIfCancellationRequested();
        }, ct).ConfigureAwait(false);

        return result;
    }

    private void Enumerate(string dir, ScanOptions opts, List<FileEntry> result, HashSet<string> seen, IProgress<string>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        try
        {
            if (!IsSystemOrJunk(dir)) return;

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                ct.ThrowIfCancellationRequested();
                if (!Matches(Path.GetFileName(file), opts)) continue;
                var fi = new FileInfo(file);
                if (fi.Length == 0) continue;
                if (!opts.IncludeHidden && (fi.Attributes & FileAttributes.Hidden) != 0) continue;

                if (!seen.Add(fi.FullName.ToLowerInvariant())) continue;

                result.Add(new FileEntry
                {
                    Path = fi.FullName,
                    Name = fi.Name,
                    NameNoExt = Path.GetFileNameWithoutExtension(fi.Name),
                    Size = fi.Length,
                    Modified = fi.LastWriteTime,
                    Created = fi.CreationTime
                });
            }

            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                ct.ThrowIfCancellationRequested();
                var attr = new FileInfo(sub).Attributes;
                if (!opts.IncludeHidden && (attr & FileAttributes.Hidden) != 0) continue;
                if ((attr & FileAttributes.ReparsePoint) != 0) continue; // junction/symlink döngüsü riski

                ThrottledReport(progress, sub);
                Enumerate(sub, opts, result, seen, progress, ct);
            }
        }
        // yalnızca erişim/yol hatalarını yut; OperationCanceledException yukarı çıkmalı
        catch (UnauthorizedAccessException) { }
        catch (DirectoryNotFoundException) { }
        catch (IOException) { }

        ThrottledReport(progress, dir);
    }

    // prefix değil segment-bazlı kontrol: "C:\WindowsFoo" sistem dizini sayılmaz
    private static bool IsSystemOrJunk(string dir)
    {
        var name = Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar));
        if (JunkDirs.Contains(name)) return false;

        foreach (var s in SystemRoots)
        {
            if (string.Equals(dir, s, StringComparison.OrdinalIgnoreCase)) return false;
            if (dir.StartsWith(s + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static readonly string[] DefaultExcludes = { "thumbs.db", "desktop.ini" };

    private static bool Matches(string name, ScanOptions opts)
    {
        if (DefaultExcludes.Contains(name.ToLowerInvariant())) return false;
        foreach (var pat in opts.ExcludePatterns)
            if (WildcardMatch(name, pat)) return false;
        foreach (var pat in opts.IncludePatterns)
            if (WildcardMatch(name, pat)) return true;
        return opts.IncludePatterns.Count == 0;
    }

    private static bool WildcardMatch(string text, string pattern)
    {
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(text, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    // UI flood engelleme: en fazla ~6 rapor/sn
    private static long _lastReportTicks;
    private static void ThrottledReport(IProgress<string>? progress, string dir)
    {
        if (progress == null) return;
        var now = Stopwatch.GetTimestamp();
        var last = Interlocked.Read(ref _lastReportTicks);
        var elapsedMs = (now - last) * 1000d / Stopwatch.Frequency;
        if (elapsedMs < 150) return;
        Interlocked.Exchange(ref _lastReportTicks, now);
        progress.Report(dir);
    }
}
