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

    public void Hydrate(List<FileEntry> files)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        foreach (var f in files)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Size,Modified,ContentHash,PerceptualHash,DecodeFailed FROM Files WHERE Path=@p";
            cmd.Parameters.AddWithValue("@p", f.Path);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                var size = r.GetInt64(0);
                var mod = r.GetInt64(1);
                if (size == f.Size && mod == f.Modified.Ticks)
                {
                    f.ContentHash = r.IsDBNull(2) ? null : r.GetString(2);
                    f.PerceptualHash = r.IsDBNull(3) ? null : r.GetString(3);
                    f.DecodeFailed = r.GetInt32(4) == 1;
                }
            }
        }
    }

    public void Save(List<FileEntry> files)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        using var tx = conn.BeginTransaction();
        foreach (var f in files)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO Files(Path,Name,Size,Modified,ContentHash,PerceptualHash,DecodeFailed)
                VALUES(@p,@n,@s,@m,@c,@ph,@d)
                ON CONFLICT(Path) DO UPDATE SET Size=@s,Modified=@m,ContentHash=@c,PerceptualHash=@ph,DecodeFailed=@d;";
            cmd.Parameters.AddWithValue("@p", f.Path);
            cmd.Parameters.AddWithValue("@n", f.Name);
            cmd.Parameters.AddWithValue("@s", f.Size);
            cmd.Parameters.AddWithValue("@m", f.Modified.Ticks);
            cmd.Parameters.AddWithValue("@c", (object?)f.ContentHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@ph", (object?)f.PerceptualHash ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@d", f.DecodeFailed ? 1 : 0);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }
}
