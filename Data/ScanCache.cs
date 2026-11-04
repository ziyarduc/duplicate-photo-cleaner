using System;
using System.Collections.Generic;
using System.IO;
using DuplicatePhotoCleaner.Models;
using Microsoft.Data.Sqlite;

namespace DuplicatePhotoCleaner.Data;

public class ScanCache
{
    private readonly string _dbPath;
    public ScanCache(string dbPath) { _dbPath = dbPath; }

    public void Initialize()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_dbPath)!);
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"CREATE TABLE IF NOT EXISTS Files(
            Path TEXT PRIMARY KEY,
            Name TEXT,
            Size INTEGER,
            Modified INTEGER,
            ContentHash TEXT,
            PerceptualHash TEXT,
            DecodeFailed INTEGER
        );";
        cmd.ExecuteNonQuery();
    }

    // tek sorguyla tüm cache'i belleğe yükle (O(N) sorgu yerine O(1) sorgu)
    public void Hydrate(List<FileEntry> files)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        var map = new Dictionary<string, (long Size, long Mod, string? Hash, string? Ph, bool Fail)>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Path,Size,Modified,ContentHash,PerceptualHash,DecodeFailed FROM Files";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var path = r.GetString(0);
                map[path] = (
                    r.GetInt64(1),
                    r.GetInt64(2),
                    r.IsDBNull(3) ? null : r.GetString(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetInt32(5) == 1);
            }
        }

        foreach (var f in files)
        {
            if (!map.TryGetValue(f.Path, out var c)) continue;
            if (c.Size != f.Size || c.Mod != f.Modified.Ticks) continue;
            f.ContentHash = c.Hash;
            f.PerceptualHash = c.Ph;
            f.DecodeFailed = c.Fail;
        }
    }

    public void Save(List<FileEntry> files)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();

        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"INSERT INTO Files(Path,Name,Size,Modified,ContentHash,PerceptualHash,DecodeFailed)
            VALUES(@p,@n,@s,@m,@c,@ph,@d)
            ON CONFLICT(Path) DO UPDATE SET Size=@s,Modified=@m,ContentHash=@c,PerceptualHash=@ph,DecodeFailed=@d;";
        var p = cmd.Parameters;
        p.Add("@p", SqliteType.Text);
        p.Add("@n", SqliteType.Text);
        p.Add("@s", SqliteType.Integer);
        p.Add("@m", SqliteType.Integer);
        p.Add("@c", SqliteType.Text);
        p.Add("@ph", SqliteType.Text);
        p.Add("@d", SqliteType.Integer);

        foreach (var f in files)
        {
            p["@p"].Value = f.Path;
            p["@n"].Value = f.Name;
            p["@s"].Value = f.Size;
            p["@m"].Value = f.Modified.Ticks;
            p["@c"].Value = (object?)f.ContentHash ?? DBNull.Value;
            p["@ph"].Value = (object?)f.PerceptualHash ?? DBNull.Value;
            p["@d"].Value = f.DecodeFailed ? 1 : 0;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
