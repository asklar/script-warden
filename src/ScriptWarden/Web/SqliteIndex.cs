using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScriptWarden.Core;

namespace ScriptWarden.Web;

/// <summary>
/// Persistent, incrementally-maintained SQLite index over the JSON audit events, used by the viewer
/// for fast filtered + paginated queries. The shim's write path is untouched (still one JSON file
/// per event); this index is a derived, rebuildable cache. On <see cref="Reconcile"/> it ingests new
/// or changed event files (upsert keyed by event id) and — for roots it owns (current user) — moves
/// completed events (those with an exit code, i.e. the second/final write has landed) into
/// <c>archive\YYYY-MM-DD\</c>, keeping <c>events\</c> small so scanning stays cheap. Roots it cannot
/// write (e.g. SYSTEM when unelevated) are ingest-only, skipped via a small (mtime,size) file table.
/// </summary>
internal sealed class SqliteIndex : IDisposable
{
    private const int SchemaVersion = 1;
    private const int BatchSize = 500;
    private readonly object _lock = new();
    private readonly string _dbPath;
    private SqliteConnection? _conn;

    // In-memory mirror of the `files` table (origin|name -> mtime,size), so the skip check is a
    // dictionary lookup with no per-file DB read. Touched only by the single reconcile thread.
    private readonly Dictionary<string, (long Mtime, long Size)> _files = new(StringComparer.OrdinalIgnoreCase);

    public SqliteIndex(string dbPath) => _dbPath = dbPath;

    public static string DefaultDbPath() => Path.Combine(DataRoots.CurrentUserRoot(), "index.db");

    public void Open()
    {
        string? dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        string cs = new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString();
        _conn = new SqliteConnection(cs);
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        Exec("PRAGMA busy_timeout=5000;");
        EnsureSchema();
        LoadFiles();
    }

    private void LoadFiles()
    {
        lock (_lock)
        {
            _files.Clear();
            using SqliteCommand cmd = _conn!.CreateCommand();
            cmd.CommandText = "SELECT origin, name, mtime, size FROM files;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                _files[r.GetString(0) + "|" + r.GetString(1)] = (r.GetInt64(2), r.GetInt64(3));
            }
        }
    }

    private void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS files(
                origin TEXT NOT NULL, name TEXT NOT NULL, mtime INTEGER NOT NULL, size INTEGER NOT NULL,
                PRIMARY KEY(origin, name));
            CREATE TABLE IF NOT EXISTS events(
                event_id TEXT PRIMARY KEY,
                ts_unix INTEGER NOT NULL,
                hooked_image TEXT,
                target_path TEXT,
                command_line TEXT,
                working_dir TEXT,
                user TEXT,
                parent_name TEXT,
                window TEXT,
                origin TEXT,
                exit_code INTEGER,
                duration_ms INTEGER,
                script_count INTEGER,
                url_count INTEGER,
                json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_events_ts ON events(ts_unix DESC);
            CREATE INDEX IF NOT EXISTS ix_events_image ON events(hooked_image);
            CREATE INDEX IF NOT EXISTS ix_events_parent ON events(parent_name);
            CREATE INDEX IF NOT EXISTS ix_events_window ON events(window);
            CREATE INDEX IF NOT EXISTS ix_events_origin ON events(origin);
            """);
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO meta(key,value) VALUES('schema',$v);";
        cmd.Parameters.AddWithValue("$v", SchemaVersion.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    public readonly record struct ReconcileResult(int Ingested, int Archived);

    private sealed record Pending(string RootPath, string Origin, bool Writable, FileInfo File, AuditEvent? Event, long Mtime, long Size);

    /// <summary>
    /// Ingests new/changed event files across all readable roots; archives completed ones we own.
    /// Skip decisions are in-memory (the <c>_files</c> mirror); DB writes are batched into
    /// transactions so catching up a large backlog stays fast regardless of how often <c>serve</c> runs.
    /// </summary>
    public ReconcileResult Reconcile()
    {
        int ingested = 0, archived = 0;
        foreach (ResolvedRoot root in DataRoots.ForViewer())
        {
            if (!root.Readable)
            {
                continue;
            }
            string dir = DataRoots.EventsDir(root.Path);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            bool writable = root.Origin == AuditOrigin.CurrentUser; // the only root we can move files in
            string origin = root.Origin.ToString();

            IEnumerable<FileInfo> files;
            try
            {
                files = new DirectoryInfo(dir).EnumerateFiles("*.json");
            }
            catch
            {
                continue;
            }

            var batch = new List<Pending>(BatchSize);
            foreach (FileInfo fi in files)
            {
                long mtime = fi.LastWriteTimeUtc.Ticks;
                long size = fi.Length;
                if (_files.TryGetValue(origin + "|" + fi.Name, out (long Mtime, long Size) known)
                    && known.Mtime == mtime && known.Size == size)
                {
                    continue; // unchanged — in-memory skip, no DB read, no re-parse
                }

                // Read + parse outside the DB lock so queries never wait on file IO.
                AuditEvent? ev = AuditStore.ReadEventFile(fi.FullName, root.Origin);
                batch.Add(new Pending(root.Path, origin, writable, fi, ev, mtime, size));
                if (batch.Count >= BatchSize)
                {
                    (int i, int a) = Flush(batch);
                    ingested += i; archived += a;
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                (int i, int a) = Flush(batch);
                ingested += i; archived += a;
            }
        }
        return new ReconcileResult(ingested, archived);
    }

    private (int Ingested, int Archived) Flush(List<Pending> batch)
    {
        int ingested = 0, archived = 0;
        var toArchive = new List<Pending>();

        // Phase 1: one transaction per batch — amortizes the WAL fsync across ~500 rows.
        lock (_lock)
        {
            using SqliteTransaction tx = _conn!.BeginTransaction();
            foreach (Pending p in batch)
            {
                string key = p.Origin + "|" + p.File.Name;
                if (p.Event is null)
                {
                    // Unreadable/partial file: stamp it so we don't re-parse until it changes.
                    WriteFileRow(tx, p.Origin, p.File.Name, p.Mtime, p.Size);
                    _files[key] = (p.Mtime, p.Size);
                    continue;
                }

                UpsertEvent(tx, p.Event);
                ingested++;

                if (p.Writable && p.Event.ExitCode.HasValue)
                {
                    toArchive.Add(p); // move the file (and drop its row) after committing
                }
                else
                {
                    WriteFileRow(tx, p.Origin, p.File.Name, p.Mtime, p.Size);
                    _files[key] = (p.Mtime, p.Size);
                }
            }
            tx.Commit();
        }

        // Phase 2: archive completed files (filesystem moves, outside the DB lock).
        if (toArchive.Count == 0)
        {
            return (ingested, archived);
        }

        var moved = new List<Pending>();
        var failed = new List<Pending>();
        foreach (Pending p in toArchive)
        {
            if (TryArchive(p.RootPath, p.File, p.Event!))
            {
                moved.Add(p);
                archived++;
            }
            else
            {
                failed.Add(p); // couldn't move (locked?); stamp so we skip re-ingest, retry archive on change
            }
        }

        lock (_lock)
        {
            using SqliteTransaction tx = _conn!.BeginTransaction();
            foreach (Pending p in moved)
            {
                DeleteFileRow(tx, p.Origin, p.File.Name);
            }
            foreach (Pending p in failed)
            {
                WriteFileRow(tx, p.Origin, p.File.Name, p.Mtime, p.Size);
            }
            tx.Commit();
        }
        foreach (Pending p in moved)
        {
            _files.Remove(p.Origin + "|" + p.File.Name);
        }
        foreach (Pending p in failed)
        {
            _files[p.Origin + "|" + p.File.Name] = (p.Mtime, p.Size);
        }

        return (ingested, archived);
    }

    private void WriteFileRow(SqliteTransaction tx, string origin, string name, long mtime, long size)
    {
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO files(origin,name,mtime,size) VALUES($o,$n,$m,$s);";
        cmd.Parameters.AddWithValue("$o", origin);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$m", mtime);
        cmd.Parameters.AddWithValue("$s", size);
        cmd.ExecuteNonQuery();
    }

    private void DeleteFileRow(SqliteTransaction tx, string origin, string name)
    {
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "DELETE FROM files WHERE origin=$o AND name=$n;";
        cmd.Parameters.AddWithValue("$o", origin);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.ExecuteNonQuery();
    }

    private void UpsertEvent(SqliteTransaction tx, AuditEvent ev)
    {
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO events(event_id,ts_unix,hooked_image,target_path,command_line,working_dir,
                user,parent_name,window,origin,exit_code,duration_ms,script_count,url_count,json)
            VALUES($id,$ts,$img,$tgt,$cmd,$wd,$user,$parent,$win,$origin,$exit,$dur,$scripts,$urls,$json);
            """;
        cmd.Parameters.AddWithValue("$id", ev.EventId);
        cmd.Parameters.AddWithValue("$ts", ev.TimestampUtc.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$img", (object?)ev.HookedImage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$tgt", (object?)ev.TargetPath ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$cmd", (object?)ev.CommandLine ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$wd", (object?)ev.WorkingDirectory ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$user", (object?)ev.User ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$parent", (object?)ev.ParentProcessName ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$win", ev.Window.ToString());
        cmd.Parameters.AddWithValue("$origin", ev.Origin.ToString());
        cmd.Parameters.AddWithValue("$exit", (object?)ev.ExitCode ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$dur", (object?)ev.DurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$scripts", ev.Scripts.Count);
        cmd.Parameters.AddWithValue("$urls", ev.Urls.Count);
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(ev, AuditJsonContext.Default.AuditEvent));
        cmd.ExecuteNonQuery();
    }

    private static bool TryArchive(string root, FileInfo fi, AuditEvent ev)
    {
        try
        {
            string day = ev.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            string destDir = Path.Combine(DataRoots.ArchiveDir(root), day);
            Directory.CreateDirectory(destDir);
            string dest = Path.Combine(destDir, fi.Name);
            File.Move(fi.FullName, dest, overwrite: true);
            return true;
        }
        catch
        {
            return false; // leave in place; retried next reconcile
        }
    }

    // ---- queries ----

    public readonly record struct Page(int Total, List<AuditEvent> Events);

    public Page Query(string? image, string? origin, string? parent, string? window, string? search, int offset, int limit, long? sinceUnixMs = null)
    {
        lock (_lock)
        {
            var where = new List<string>();
            var ps = new List<(string, object)>();
            void Eq(string col, string? val)
            {
                if (!string.IsNullOrEmpty(val) && !string.Equals(val, "all", StringComparison.OrdinalIgnoreCase))
                {
                    string p = "$p" + ps.Count;
                    where.Add($"{col} = {p}");
                    ps.Add((p, val));
                }
            }
            Eq("hooked_image", image);
            Eq("origin", origin);
            Eq("parent_name", parent);
            Eq("window", window);
            if (!string.IsNullOrWhiteSpace(search))
            {
                string p = "$p" + ps.Count;
                where.Add($"(command_line LIKE {p} OR target_path LIKE {p} OR user LIKE {p} OR parent_name LIKE {p})");
                ps.Add((p, "%" + search + "%"));
            }
            if (sinceUnixMs is long since)
            {
                where.Add("ts_unix >= $since");
                ps.Add(("$since", since));
            }
            string clause = where.Count > 0 ? " WHERE " + string.Join(" AND ", where) : "";

            int total;
            using (SqliteCommand c = _conn!.CreateCommand())
            {
                c.CommandText = "SELECT COUNT(*) FROM events" + clause + ";";
                foreach ((string name, object val) in ps) c.Parameters.AddWithValue(name, val);
                total = Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture);
            }

            var events = new List<AuditEvent>();
            using (SqliteCommand c = _conn!.CreateCommand())
            {
                c.CommandText = "SELECT json FROM events" + clause + " ORDER BY ts_unix DESC LIMIT $lim OFFSET $off;";
                foreach ((string name, object val) in ps) c.Parameters.AddWithValue(name, val);
                c.Parameters.AddWithValue("$lim", Math.Clamp(limit, 1, 500));
                c.Parameters.AddWithValue("$off", Math.Max(0, offset));
                using SqliteDataReader r = c.ExecuteReader();
                while (r.Read())
                {
                    AuditEvent? ev = JsonSerializer.Deserialize(r.GetString(0), AuditJsonContext.Default.AuditEvent);
                    if (ev is not null)
                    {
                        events.Add(ev);
                    }
                }
            }
            return new Page(total, events);
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            using SqliteCommand c = _conn!.CreateCommand();
            c.CommandText = "SELECT COUNT(*) FROM events;";
            return Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    public List<string> Distinct(string column)
    {
        // column is a fixed, internal identifier (never user input).
        lock (_lock)
        {
            var list = new List<string>();
            using SqliteCommand c = _conn!.CreateCommand();
            c.CommandText = $"SELECT DISTINCT {column} FROM events WHERE {column} IS NOT NULL AND {column} <> '' ORDER BY {column};";
            using SqliteDataReader r = c.ExecuteReader();
            while (r.Read())
            {
                list.Add(r.GetString(0));
            }
            return list;
        }
    }

    /// <summary>Clears all indexed rows (does not touch the JSON/archive files on disk).</summary>
    public void ClearRows()
    {
        lock (_lock)
        {
            Exec("DELETE FROM events; DELETE FROM files;");
            _files.Clear();
        }
    }

    private void Exec(string sql)
    {
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        _conn?.Dispose();
        _conn = null;
    }
}
