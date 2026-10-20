using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CsvHelper;
using DuplicatePhotoCleaner.Models;

namespace DuplicatePhotoCleaner.Core;

public class ReportExporter
{
    public void ExportCsv(string path, IEnumerable<DuplicateGroup> groups)
    {
        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture);
        csv.WriteField("Kind"); csv.WriteField("GroupKey"); csv.WriteField("Path"); csv.WriteField("Name");
        csv.WriteField("Size"); csv.WriteField("Modified"); csv.WriteField("Kept"); csv.NextRecord();
        foreach (var g in groups)
            foreach (var f in g.Files)
            {
                csv.WriteField(g.Kind.ToString()); csv.WriteField(g.Key); csv.WriteField(f.Path); csv.WriteField(f.Name);
                csv.WriteField(f.Size); csv.WriteField(f.Modified); csv.WriteField(f.IsKept); csv.NextRecord();
            }
    }

    public void ExportJson(string path, IEnumerable<DuplicateGroup> groups)
    {
        var data = groups.Select(g => new
        {
            Kind = g.Kind.ToString(),
            Key = g.Key,
            WastedBytes = g.WastedBytes,
            Files = g.Files.Select(f => new { f.Path, f.Name, f.Size, f.Modified, f.IsKept })
        });
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);
    }
}
