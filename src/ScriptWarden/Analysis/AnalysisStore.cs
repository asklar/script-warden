using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ScriptWarden.Core;
using ScriptWarden.Core.Analysis;

namespace ScriptWarden.Analysis;

/// <summary>
/// The persistent analysis database (<c>analysis.db</c>), built by the explicit <c>analyze</c> gesture.
/// It does NOT move or delete the JSON event files (the viewer is unaffected); it just tracks what it
/// has ingested and stores, per event: the raw JSON, and the labels assigned by each taxonomy. It also
/// keeps an FTS5 index over the unique captured-script contents so rules and the UI can ask
/// "which scripts mention X?". Re-running <c>analyze</c> ingests only new events; if the taxonomies
/// changed, it re-labels the existing events from stored JSON (no file re-read).
/// </summary>
internal sealed class AnalysisStore : IDisposable
{
    private const int SchemaVersion = 1;
    private const string EngineVersion = "3"; // bump to force re-labeling of existing events
    private const int BatchSize = 400;
    private readonly object _lock = new();
    private readonly string _dbPath;
    private SqliteConnection? _conn;
    private readonly Dictionary<string, (long Mtime, long Size)> _ingested = new(StringComparer.OrdinalIgnoreCase);

    public AnalysisStore(string dbPath) => _dbPath = dbPath;

    public static string DefaultDbPath() => Path.Combine(DataRoots.CurrentUserRoot(), "analysis.db");

    public void Open()
    {
        string? dir = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ToString());
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        Exec("PRAGMA synchronous=NORMAL;");
        Exec("PRAGMA busy_timeout=5000;");
        EnsureSchema();
        LoadIngested();
    }

    private void EnsureSchema()
    {
        Exec("""
            CREATE TABLE IF NOT EXISTS meta(key TEXT PRIMARY KEY, value TEXT);
            CREATE TABLE IF NOT EXISTS ingested(origin TEXT, name TEXT, mtime INTEGER, size INTEGER, PRIMARY KEY(origin,name));
            CREATE TABLE IF NOT EXISTS events(event_id TEXT PRIMARY KEY, ts_unix INTEGER, duration_ms INTEGER, hooked_image TEXT, origin TEXT, json TEXT);
            CREATE TABLE IF NOT EXISTS labels(event_id TEXT, taxonomy_id TEXT, label TEXT);
            CREATE INDEX IF NOT EXISTS ix_labels_tl ON labels(taxonomy_id, label);
            CREATE INDEX IF NOT EXISTS ix_labels_e ON labels(event_id);
            CREATE TABLE IF NOT EXISTS scripts(sha TEXT PRIMARY KEY);
            CREATE TABLE IF NOT EXISTS event_scripts(event_id TEXT, sha TEXT);
            CREATE INDEX IF NOT EXISTS ix_es_sha ON event_scripts(sha);
            CREATE INDEX IF NOT EXISTS ix_es_e ON event_scripts(event_id);
            CREATE VIRTUAL TABLE IF NOT EXISTS scripts_fts USING fts5(sha UNINDEXED, content);
            """);
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO meta(key,value) VALUES('schema',$v);";
        cmd.Parameters.AddWithValue("$v", SchemaVersion.ToString(CultureInfo.InvariantCulture));
        cmd.ExecuteNonQuery();
    }

    private void LoadIngested()
    {
        lock (_lock)
        {
            _ingested.Clear();
            using SqliteCommand cmd = _conn!.CreateCommand();
            cmd.CommandText = "SELECT origin, name, mtime, size FROM ingested;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                _ingested[r.GetString(0) + "|" + r.GetString(1)] = (r.GetInt64(2), r.GetInt64(3));
            }
        }
    }

    public readonly record struct RefreshResult(int Ingested, int Total, bool Reclassified);

    /// <summary>Ingests new events and (re)labels them via the given taxonomies. Re-labels existing
    /// events too if the taxonomy rules changed since the last run.</summary>
    public RefreshResult Refresh(List<Taxonomy> taxonomies, ScriptContentProvider content)
    {
        bool reclassified = ReclassifyIfTaxonomiesChanged(taxonomies, content);

        int ingested = 0;
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

            var batch = new List<(FileInfo File, AuditEvent Ev, long Mtime, long Size)>(BatchSize);
            foreach (FileInfo fi in files)
            {
                long mtime = fi.LastWriteTimeUtc.Ticks;
                long size = fi.Length;
                if (_ingested.TryGetValue(origin + "|" + fi.Name, out (long Mtime, long Size) known)
                    && known.Mtime == mtime && known.Size == size)
                {
                    continue;
                }
                AuditEvent? ev = AuditStore.ReadEventFile(fi.FullName, root.Origin);
                if (ev is not null)
                {
                    batch.Add((fi, ev, mtime, size));
                }
                else
                {
                    MarkIngested(origin, fi.Name, mtime, size);
                }
                if (batch.Count >= BatchSize)
                {
                    ingested += FlushBatch(origin, batch, taxonomies, content);
                    batch.Clear();
                }
            }
            if (batch.Count > 0)
            {
                ingested += FlushBatch(origin, batch, taxonomies, content);
            }
        }

        return new RefreshResult(ingested, Count(), reclassified);
    }

    private int FlushBatch(string origin, List<(FileInfo File, AuditEvent Ev, long Mtime, long Size)> batch,
        List<Taxonomy> taxonomies, ScriptContentProvider content)
    {
        lock (_lock)
        {
            using SqliteTransaction tx = _conn!.BeginTransaction();
            foreach ((FileInfo file, AuditEvent ev, long mtime, long size) in batch)
            {
                List<string> contents = content.ForEvent(ev);
                UpsertEvent(tx, ev);
                ReplaceLabels(tx, ev, EventFacts.From(ev, contents), taxonomies);
                IndexScripts(tx, ev, contents);
                WriteIngestedRow(tx, origin, file.Name, mtime, size);
                _ingested[origin + "|" + file.Name] = (mtime, size);
            }
            tx.Commit();
        }
        return batch.Count;
    }

    private bool ReclassifyIfTaxonomiesChanged(List<Taxonomy> taxonomies, ScriptContentProvider content)
    {
        // Prefix with the enrichment-engine version so a logic change (not just a taxonomy edit) also
        // forces existing events to be re-labeled on the next analyze.
        string hash = EngineVersion + ":" + TaxonomyStore.ContentHash(taxonomies);
        if (GetMeta("taxHash") == hash)
        {
            return false;
        }

        // Rules changed (or first run): rebuild labels for all already-ingested events from stored JSON.
        var events = new List<AuditEvent>();
        lock (_lock)
        {
            using SqliteCommand cmd = _conn!.CreateCommand();
            cmd.CommandText = "SELECT json FROM events;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                AuditEvent? ev = JsonSerializer.Deserialize(r.GetString(0), AuditJsonContext.Default.AuditEvent);
                if (ev is not null)
                {
                    events.Add(ev);
                }
            }
        }

        lock (_lock)
        {
            using SqliteTransaction tx = _conn!.BeginTransaction();
            using (SqliteCommand del = _conn.CreateCommand())
            {
                del.Transaction = tx;
                del.CommandText = "DELETE FROM labels;";
                del.ExecuteNonQuery();
            }
            foreach (AuditEvent ev in events)
            {
                ReplaceLabels(tx, ev, EventFacts.From(ev, content.ForEvent(ev)), taxonomies, deleteFirst: false);
            }
            tx.Commit();
        }

        SetMeta("taxHash", hash);
        return events.Count > 0;
    }

    // ---- writes (within a transaction) ----

    private void UpsertEvent(SqliteTransaction tx, AuditEvent ev)
    {
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR REPLACE INTO events(event_id, ts_unix, duration_ms, hooked_image, origin, json)
            VALUES($id,$ts,$dur,$img,$origin,$json);
            """;
        cmd.Parameters.AddWithValue("$id", ev.EventId);
        cmd.Parameters.AddWithValue("$ts", ev.TimestampUtc.ToUnixTimeMilliseconds());
        cmd.Parameters.AddWithValue("$dur", (object?)ev.DurationMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$img", (object?)ev.HookedImage ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$origin", ev.Origin.ToString());
        cmd.Parameters.AddWithValue("$json", JsonSerializer.Serialize(ev, AuditJsonContext.Default.AuditEvent));
        cmd.ExecuteNonQuery();
    }

    private void ReplaceLabels(SqliteTransaction tx, AuditEvent ev, EventFacts facts, List<Taxonomy> taxonomies, bool deleteFirst = true)
    {
        if (deleteFirst)
        {
            using SqliteCommand del = _conn!.CreateCommand();
            del.Transaction = tx;
            del.CommandText = "DELETE FROM labels WHERE event_id=$e;";
            del.Parameters.AddWithValue("$e", ev.EventId);
            del.ExecuteNonQuery();
        }
        foreach (Taxonomy tax in taxonomies)
        {
            foreach (string label in TaxonomyEngine.Classify(tax, facts))
            {
                using SqliteCommand ins = _conn!.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = "INSERT INTO labels(event_id, taxonomy_id, label) VALUES($e,$t,$l);";
                ins.Parameters.AddWithValue("$e", ev.EventId);
                ins.Parameters.AddWithValue("$t", tax.Id);
                ins.Parameters.AddWithValue("$l", label);
                ins.ExecuteNonQuery();
            }
        }
    }

    private void IndexScripts(SqliteTransaction tx, AuditEvent ev, List<string> contents)
    {
        int i = 0;
        foreach (CapturedScript s in ev.Scripts)
        {
            if (string.IsNullOrEmpty(s.Sha256))
            {
                continue;
            }
            using (SqliteCommand link = _conn!.CreateCommand())
            {
                link.Transaction = tx;
                link.CommandText = "INSERT INTO event_scripts(event_id, sha) VALUES($e,$s);";
                link.Parameters.AddWithValue("$e", ev.EventId);
                link.Parameters.AddWithValue("$s", s.Sha256);
                link.ExecuteNonQuery();
            }

            bool known;
            using (SqliteCommand chk = _conn!.CreateCommand())
            {
                chk.Transaction = tx;
                chk.CommandText = "SELECT 1 FROM scripts WHERE sha=$s;";
                chk.Parameters.AddWithValue("$s", s.Sha256);
                known = chk.ExecuteScalar() is not null;
            }
            if (!known)
            {
                string text = i < contents.Count ? contents[i] : "";
                using (SqliteCommand mark = _conn!.CreateCommand())
                {
                    mark.Transaction = tx;
                    mark.CommandText = "INSERT INTO scripts(sha) VALUES($s);";
                    mark.Parameters.AddWithValue("$s", s.Sha256);
                    mark.ExecuteNonQuery();
                }
                using SqliteCommand fts = _conn!.CreateCommand();
                fts.Transaction = tx;
                fts.CommandText = "INSERT INTO scripts_fts(sha, content) VALUES($s,$c);";
                fts.Parameters.AddWithValue("$s", s.Sha256);
                fts.Parameters.AddWithValue("$c", text);
                fts.ExecuteNonQuery();
            }
            i++;
        }
    }

    private void WriteIngestedRow(SqliteTransaction tx, string origin, string name, long mtime, long size)
    {
        using SqliteCommand cmd = _conn!.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT OR REPLACE INTO ingested(origin,name,mtime,size) VALUES($o,$n,$m,$s);";
        cmd.Parameters.AddWithValue("$o", origin);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$m", mtime);
        cmd.Parameters.AddWithValue("$s", size);
        cmd.ExecuteNonQuery();
    }

    private void MarkIngested(string origin, string name, long mtime, long size)
    {
        lock (_lock)
        {
            using SqliteTransaction tx = _conn!.BeginTransaction();
            WriteIngestedRow(tx, origin, name, mtime, size);
            tx.Commit();
        }
        _ingested[origin + "|" + name] = (mtime, size);
    }

    // ---- queries (filter-driven) ----

    public readonly record struct RollupRow(string Label, int Count, long TotalMs);

    /// <summary>
    /// Builds the AND-ed WHERE conjuncts for a filter set, binding parameters to <paramref name="cmd"/>.
    /// Each conjunct constrains the events aliased <paramref name="alias"/>; within a taxonomy filter,
    /// labels are OR-ed (IN / NOT IN). Returned string is empty or begins with " AND ".
    /// </summary>
    private static string BuildFilters(List<AnalysisFilter>? filters, SqliteCommand cmd, string alias)
    {
        if (filters is null || filters.Count == 0)
        {
            return "";
        }
        var sb = new StringBuilder();
        int n = 0;
        foreach (AnalysisFilter f in filters)
        {
            switch ((f.Type ?? "").ToLowerInvariant())
            {
                case "taxonomy":
                    if (string.IsNullOrEmpty(f.Taxonomy) || f.Labels is not { Count: > 0 })
                    {
                        break;
                    }
                    var ph = new List<string>();
                    foreach (string label in f.Labels)
                    {
                        string p = "$f" + n++;
                        ph.Add(p);
                        cmd.Parameters.AddWithValue(p, label);
                    }
                    string tp = "$f" + n++;
                    cmd.Parameters.AddWithValue(tp, f.Taxonomy);
                    string not = string.Equals(f.Op, "exclude", StringComparison.OrdinalIgnoreCase) ? "NOT " : "";
                    sb.Append($" AND {alias}.event_id {not}IN (SELECT event_id FROM labels WHERE taxonomy_id={tp} AND label IN ({string.Join(",", ph)}))");
                    break;

                case "time":
                    if (f.SinceMs is long since)
                    {
                        string p = "$f" + n++;
                        cmd.Parameters.AddWithValue(p, since);
                        sb.Append($" AND {alias}.ts_unix >= {p}");
                    }
                    if (f.UntilMs is long until)
                    {
                        string p = "$f" + n++;
                        cmd.Parameters.AddWithValue(p, until);
                        sb.Append($" AND {alias}.ts_unix < {p}");
                    }
                    break;

                case "duration":
                    if (f.MinDurationMs is long dur)
                    {
                        string p = "$f" + n++;
                        cmd.Parameters.AddWithValue(p, dur);
                        sb.Append($" AND {alias}.duration_ms >= {p}");
                    }
                    break;

                case "content":
                    if (!string.IsNullOrWhiteSpace(f.Query))
                    {
                        string p = "$f" + n++;
                        cmd.Parameters.AddWithValue(p, "\"" + f.Query.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"");
                        sb.Append($" AND {alias}.event_id IN (SELECT es.event_id FROM scripts_fts fts JOIN event_scripts es ON es.sha=fts.sha WHERE fts.content MATCH {p})");
                    }
                    break;

                case "parent":
                    if (f.Values is { Count: > 0 })
                    {
                        var pph = new List<string>();
                        foreach (string v in f.Values)
                        {
                            string p = "$f" + n++;
                            pph.Add(p);
                            cmd.Parameters.AddWithValue(p, v.ToLowerInvariant());
                        }
                        // Parent name lives in the stored event JSON; match the immediate parent
                        // (case-insensitively) against the selected set, mirroring the audit list's filter.
                        sb.Append($" AND LOWER(json_extract({alias}.json,'$.parentProcessName')) IN ({string.Join(",", pph)})");
                    }
                    break;
            }
        }
        return sb.ToString();
    }

    public List<RollupRow> Rollup(string taxonomyId, List<AnalysisFilter>? filters)
    {
        lock (_lock)
        {
            var rows = new List<RollupRow>();
            using SqliteCommand cmd = _conn!.CreateCommand();
            cmd.Parameters.AddWithValue("$tax", taxonomyId);
            cmd.CommandText =
                "SELECT l.label, COUNT(DISTINCT e.event_id), COALESCE(SUM(e.duration_ms),0) " +
                "FROM labels l JOIN events e ON e.event_id = l.event_id WHERE l.taxonomy_id = $tax" +
                BuildFilters(filters, cmd, "e") +
                " GROUP BY l.label ORDER BY COUNT(DISTINCT e.event_id) DESC;";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                rows.Add(new RollupRow(r.GetString(0), r.GetInt32(1), r.GetInt64(2)));
            }
            return rows;
        }
    }

    /// <summary>Count of distinct events matching the filter set (ignoring any grouping).</summary>
    public int MatchCount(List<AnalysisFilter>? filters)
    {
        lock (_lock)
        {
            using SqliteCommand cmd = _conn!.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM events e WHERE 1=1" + BuildFilters(filters, cmd, "e") + ";";
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Events matching the filter set and (optionally) a specific group-by label. Newest first.</summary>
    public (int Total, List<AuditEvent> Events) DrillEvents(string taxonomyId, string? label, List<AnalysisFilter>? filters, int offset, int limit)
    {
        lock (_lock)
        {
            string labelClause = "";
            void BindLabel(SqliteCommand c)
            {
                if (!string.IsNullOrEmpty(label))
                {
                    c.Parameters.AddWithValue("$lbl", label);
                    c.Parameters.AddWithValue("$tax", taxonomyId);
                }
            }
            if (!string.IsNullOrEmpty(label))
            {
                labelClause = " AND e.event_id IN (SELECT event_id FROM labels WHERE taxonomy_id=$tax AND label=$lbl)";
            }

            int total;
            using (SqliteCommand c = _conn!.CreateCommand())
            {
                BindLabel(c);
                c.CommandText = "SELECT COUNT(*) FROM events e WHERE 1=1" + labelClause + BuildFilters(filters, c, "e") + ";";
                total = Convert.ToInt32(c.ExecuteScalar(), CultureInfo.InvariantCulture);
            }
            var events = new List<AuditEvent>();
            using (SqliteCommand c = _conn!.CreateCommand())
            {
                BindLabel(c);
                c.Parameters.AddWithValue("$lim", Math.Clamp(limit, 1, 500));
                c.Parameters.AddWithValue("$off", Math.Max(0, offset));
                c.CommandText = "SELECT e.json FROM events e WHERE 1=1" + labelClause + BuildFilters(filters, c, "e") +
                    " ORDER BY e.ts_unix DESC LIMIT $lim OFFSET $off;";
                Read(c, events);
            }
            return (total, events);
        }
    }

    private static void Read(SqliteCommand c, List<AuditEvent> into)
    {
        using SqliteDataReader r = c.ExecuteReader();
        while (r.Read())
        {
            AuditEvent? ev = JsonSerializer.Deserialize(r.GetString(0), AuditJsonContext.Default.AuditEvent);
            if (ev is not null)
            {
                into.Add(ev);
            }
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

    private string? GetMeta(string key)
    {
        lock (_lock)
        {
            using SqliteCommand c = _conn!.CreateCommand();
            c.CommandText = "SELECT value FROM meta WHERE key=$k;";
            c.Parameters.AddWithValue("$k", key);
            return c.ExecuteScalar() as string;
        }
    }

    private void SetMeta(string key, string value)
    {
        lock (_lock)
        {
            using SqliteCommand c = _conn!.CreateCommand();
            c.CommandText = "INSERT OR REPLACE INTO meta(key,value) VALUES($k,$v);";
            c.Parameters.AddWithValue("$k", key);
            c.Parameters.AddWithValue("$v", value);
            c.ExecuteNonQuery();
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
