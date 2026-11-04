using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using DuplicatePhotoCleaner.Models;

namespace DuplicatePhotoCleaner.Core;

public class Grouper
{
    public List<DuplicateGroup> GroupByName(IEnumerable<FileEntry> files)
    {
        return Group(files.Where(f => f.NameNoExt.Length > 0), f => f.NameNoExt.ToLowerInvariant(), DuplicateKind.Name);
    }

    public List<DuplicateGroup> GroupByContent(IEnumerable<FileEntry> files)
    {
        return Group(files.Where(f => f.ContentHash != null), f => f.ContentHash!, DuplicateKind.Content);
    }

    private static List<DuplicateGroup> Group(IEnumerable<FileEntry> files, Func<FileEntry, string> key, DuplicateKind kind)
    {
        return files
            .GroupBy(key, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select((g, i) => new DuplicateGroup
            {
                Id = i,
                Kind = kind,
                Key = g.Key,
                Files = new ObservableCollection<FileEntry>(g.OrderBy(f => f.Path))
            })
            .ToList();
    }
}
