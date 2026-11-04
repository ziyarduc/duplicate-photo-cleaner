using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DuplicatePhotoCleaner.Models;

namespace DuplicatePhotoCleaner.Core;

public class Hasher
{
    public async Task HashContentAsync(IEnumerable<FileEntry> files, int parallelism, IProgress<int>? progress, CancellationToken ct)
    {
        var list = files as IReadOnlyList<FileEntry> ?? new List<FileEntry>(files);
        int done = 0;
        var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct };

        await Task.Run(() =>
        {
            Parallel.ForEach(list, po, f =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var fs = new FileStream(f.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.SequentialScan);
                    using var sha = SHA256.Create(); // SHA256 thread-safe değil, dosya başına instance
                    f.ContentHash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
                }
                catch (IOException) { f.ContentHash = null; }
                catch (UnauthorizedAccessException) { f.ContentHash = null; }
                finally
                {
                    var d = Interlocked.Increment(ref done);
                    if (d % 16 == 0 || d == list.Count) progress?.Report(d);
                }
            });
        }, ct).ConfigureAwait(false);
    }
}
