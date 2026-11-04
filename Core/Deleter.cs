using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DuplicatePhotoCleaner.Models;

namespace DuplicatePhotoCleaner.Core;

public class Deleter
{
    public static string DefaultQuarantinePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DuplicatePhotoCleaner", "Quarantine");

    public void ApplyKeepRule(DuplicateGroup group, KeepRule rule)
    {
        foreach (var f in group.Files) { f.IsKept = false; f.IsSelectedForDeletion = true; }
        var keeper = rule switch
        {
            KeepRule.Oldest => group.Files.OrderBy(f => f.Modified).First(),
            KeepRule.Newest => group.Files.OrderByDescending(f => f.Modified).First(),
            KeepRule.Largest => group.Files.OrderByDescending(f => f.Size).First(),
            _ => group.Files.OrderBy(f => f.Path).First()
        };
        keeper.IsKept = true;
        keeper.IsSelectedForDeletion = false;
    }

    public async Task<DeleteResult> DeleteAsync(IEnumerable<FileEntry> toDelete, string? quarantineDir, IProgress<string>? progress, CancellationToken ct)
    {
        var res = new DeleteResult();
        var qRoot = quarantineDir;
        string? qSession = null;

        await Task.Run(() =>
        {
            foreach (var f in toDelete)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!string.IsNullOrWhiteSpace(qRoot))
                    {
                        // her silme oturumu için GUID alt klasörü: kaynak klasörle çakışma imkansız
                        qSession ??= Path.Combine(qRoot, DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8]);
                        Directory.CreateDirectory(qSession);

                        var dest = Path.Combine(qSession, Path.GetFileName(f.Path));
                        if (File.Exists(dest))
                            dest = Path.Combine(qSession, $"{Path.GetFileNameWithoutExtension(dest)}_{Guid.NewGuid():N}{Path.GetExtension(dest)}");

                        ClearReadOnly(f.Path);
                        File.Move(f.Path, dest);
                    }
                    else
                    {
                        ClearReadOnly(f.Path);
                        File.Delete(f.Path);
                    }
                    res.Deleted++;
                    res.DeletedBytes += f.Size;
                }
                catch (Exception ex)
                {
                    res.Failed++;
                    res.Errors.Add($"{f.Path}: {ex.Message}");
                }
                progress?.Report(f.Path);
            }
        }, ct).ConfigureAwait(false);
        return res;
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            if (fi.IsReadOnly) fi.IsReadOnly = false;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}

public class DeleteResult
{
    public int Deleted { get; set; }
    public int Failed { get; set; }
    public long DeletedBytes { get; set; }
    public List<string> Errors { get; set; } = new();
}
