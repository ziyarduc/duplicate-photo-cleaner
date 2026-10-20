using System;
using System.Collections.Generic;
using System.Linq;
using DuplicatePhotoCleaner.Imaging;
using DuplicatePhotoCleaner.Models;

namespace DuplicatePhotoCleaner.Core;

public class Grouper
{
    public List<DuplicateGroup> GroupByName(IEnumerable<FileEntry> files)
    {
        return files
            .GroupBy(f => f.NameNoExt.ToLowerInvariant())
            .Where(g => g.Count() > 1)
            .Select((g, i) => new DuplicateGroup { Id = i, Kind = DuplicateKind.Name, Key = g.Key, Files = g.OrderBy(f => f.Path).ToList() })
            .ToList();
    }

    public List<DuplicateGroup> GroupByContent(IEnumerable<FileEntry> files)
    {
        return files.Where(f => f.ContentHash != null)
            .GroupBy(f => f.ContentHash!)
            .Where(g => g.Count() > 1)
            .Select((g, i) => new DuplicateGroup { Id = i, Kind = DuplicateKind.Content, Key = g.Key, Files = g.OrderBy(f => f.Path).ToList() })
            .ToList();
    }

    public List<DuplicateGroup> GroupByPerceptual(IEnumerable<FileEntry> files, int threshold)
    {
        var list = files.Where(f => f.PerceptualHash != null).ToList();
        int n = list.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        Func<int, int> find = null!;
        find = x => parent[x] == x ? x : (parent[x] = find(parent[x]));
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                var d = PerceptualHasher.HammingDistance(list[i].PerceptualHash, list[j].PerceptualHash);
                if (d <= threshold)
                {
                    var ri = find(i); var rj = find(j);
                    if (ri != rj) parent[ri] = rj;
                }
            }
        var groups = list
            .Select((f, i) => (f, root: find(i)))
            .GroupBy(t => t.root)
            .Where(g => g.Count() > 1)
            .Select((g, i) => new DuplicateGroup { Id = i, Kind = DuplicateKind.Perceptual, Key = g.First().f.PerceptualHash!, Files = g.Select(t => t.f).OrderBy(f => f.Path).ToList() })
            .ToList();
        return groups;
    }
}
