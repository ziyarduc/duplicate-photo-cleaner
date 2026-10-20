using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DuplicatePhotoCleaner.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace DuplicatePhotoCleaner.Imaging;

public class PerceptualHasher
{
    public async Task ComputeAsync(IEnumerable<FileEntry> files, int parallelism, IProgress<int>? progress, CancellationToken ct)
    {
        var list = files is List<FileEntry> l ? l : new List<FileEntry>(files);
        int done = 0;
        var po = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism), CancellationToken = ct };

        await Task.Run(() =>
        {
            Parallel.ForEach(list, po, f =>
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var img = SixLabors.ImageSharp.Image.Load<SixLabors.ImageSharp.PixelFormats.L8>(f.Path);
                    img.Mutate(x => x.Resize(9, 8).Grayscale());
                    // dHash
                    ulong hash = 0;
                    int bit = 0;
                    for (int y = 0; y < 8; y++)
                    {
                        for (int x = 0; x < 8; x++)
                        {
                            var left = img[x, y].PackedValue;
                            var right = img[x + 1, y].PackedValue;
                            if (left > right) hash |= (1UL << bit);
                            bit++;
                        }
                    }
                    f.PerceptualHash = hash.ToString("x16");
                }
                catch
                {
                    f.DecodeFailed = true;
                    f.PerceptualHash = null;
                }
                finally
                {
                    var d = Interlocked.Increment(ref done);
                    progress?.Report(d);
                }
            });
        }, ct).ConfigureAwait(false);
    }

    public static int HammingDistance(string? a, string? b)
    {
        if (a == null || b == null || a.Length != b.Length) return int.MaxValue;
        ulong x = ulong.Parse(a, System.Globalization.NumberStyles.HexNumber);
        ulong y = ulong.Parse(b, System.Globalization.NumberStyles.HexNumber);
        return System.Numerics.BitOperations.PopCount(x ^ y);
    }
}
