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
    public void ApplyKeepRule(DuplicateGroup group, KeepRule rule)
    {
        foreach (var f in group.Files) { f.IsKept = false; f.IsSelectedForDeletion = true; }
        var keeper = rule switch
        {
            KeepRule.Oldest => group.Files.OrderBy(f => f.Modified).First(),
            KeepRule.Newest => group.Files.OrderByDescending(f => f.Modified).First(),
            KeepRule.Largest => group.Files.OrderByDescending(f => f.Size).First(),
            KeepRule.Smallest => group.Files.OrderBy(f => f.Size).First(),
            KeepRule.ShortestPath => group.Files.OrderBy(f => f.Path.Length).First(),
            KeepRule.First => group.Files.OrderBy(f => f.Path).First(),
            _ => group.Files.OrderBy(f => f.Path).First()
        };
        keeper.IsKept = true;
        keeper.IsSelectedForDeletion = false;
    }

    public async Task<DeleteResult> DeleteAsync(IEnumerable<FileEntry> toDelete, string? quarantineDir, IProgress<string>? progress, CancellationToken ct)
    {
        var res = new DeleteResult();
        await Task.Run(() =>
        {
            foreach (var f in toDelete)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!string.IsNullOrEmpty(quarantineDir))
                    {
                        Directory.CreateDirectory(quarantineDir);
                        var dest = Path.Combine(quarantineDir, Path.GetFileName(f.Path));
                        // avoid collision
                        if (File.Exists(dest))
                        {
                            var name = Path.GetFileNameWithoutExtension(dest);
                            var ext = Path.GetExtension(dest);
                            dest = Path.Combine(quarantineDir, $"{name}_{Guid.NewGuid():N}{ext}");
                        }
                        File.Move(f.Path, dest);
                    }
                    else
                    {
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
}

public class DeleteResult
{
    public int Deleted { get; set; }
    public int Failed { get; set; }
    public long DeletedBytes { get; set; }
    public List<string> Errors { get; set; } = new();
}
