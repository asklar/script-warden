using System.Globalization;
using System.Text.Json;

namespace ScriptWarden.Core;

/// <summary>
/// Reads and writes audit events. Each event is a standalone JSON file under <c>events\</c>, named
/// with a sortable UTC timestamp + pid + short id. One-file-per-event keeps concurrent writers from
/// many interpreter launches lock-free.
/// </summary>
public static class AuditStore
{
    public static string WriteEvent(string root, AuditEvent ev)
    {
        DataRoots.EnsureLayout(root);
        string dir = DataRoots.EventsDir(root);
        string finalPath = Path.Combine(dir, FormatFileName(ev));
        string tmp = finalPath + ".tmp";

        byte[] json = JsonSerializer.SerializeToUtf8Bytes(ev, AuditJsonContext.Default.AuditEvent);
        File.WriteAllBytes(tmp, json);
        File.Move(tmp, finalPath, overwrite: true);
        return finalPath;
    }

    public static string FormatFileName(AuditEvent ev)
    {
        string ts = ev.TimestampUtc.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        string id = ev.EventId.Replace("-", "", StringComparison.Ordinal);
        if (id.Length > 12)
        {
            id = id[..12];
        }
        if (id.Length == 0)
        {
            id = "0";
        }
        return $"{ts}-{ev.ShimProcessId}-{id}.json";
    }

    /// <summary>Reads all events from a single root, tagging each with <paramref name="origin"/>.</summary>
    public static IEnumerable<AuditEvent> ReadEvents(string root, AuditOrigin origin)
    {
        string dir = DataRoots.EventsDir(root);
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.json");
        }
        catch
        {
            yield break;
        }

        foreach (string file in files)
        {
            AuditEvent? ev = TryRead(file, origin);
            if (ev is not null)
            {
                yield return ev;
            }
        }
    }

    /// <summary>Reads all archived events from a single root (recursive), tagging with <paramref name="origin"/>.</summary>
    public static IEnumerable<AuditEvent> ReadArchivedEvents(string root, AuditOrigin origin)
    {
        string dir = DataRoots.ArchiveDir(root);
        if (!Directory.Exists(dir))
        {
            yield break;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories);
        }
        catch
        {
            yield break;
        }

        foreach (string file in files)
        {
            AuditEvent? ev = TryRead(file, origin);
            if (ev is not null)
            {
                yield return ev;
            }
        }
    }

    /// <summary>Reads and merges events from all viewer roots, newest first.</summary>
    public static List<AuditEvent> ReadAllForViewer(out IReadOnlyList<ResolvedRoot> roots, bool includeArchive = false)
    {
        roots = DataRoots.ForViewer();
        var all = new List<AuditEvent>();
        foreach (ResolvedRoot root in roots)
        {
            if (!root.Readable)
            {
                continue;
            }
            all.AddRange(ReadEvents(root.Path, root.Origin));
            if (includeArchive)
            {
                all.AddRange(ReadArchivedEvents(root.Path, root.Origin));
            }
        }

        // De-duplicate by event id (events\ and archive\ can briefly overlap during a race), newest first.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deduped = new List<AuditEvent>(all.Count);
        foreach (AuditEvent e in all)
        {
            if (string.IsNullOrEmpty(e.EventId) || seen.Add(e.EventId))
            {
                deduped.Add(e);
            }
        }
        deduped.Sort(static (a, b) => b.TimestampUtc.CompareTo(a.TimestampUtc));
        return deduped;
    }

    /// <summary>Deletes all event, archived-event, and script files under a root. Robust to locks.</summary>
    public static (int Events, int Scripts) ClearRoot(string root)
    {
        int events = DeleteFilesIn(DataRoots.EventsDir(root), "*.json");
        events += DeleteTree(DataRoots.ArchiveDir(root));
        int scripts = DeleteFilesIn(DataRoots.ScriptsDir(root), "*");
        return (events, scripts);
    }

    /// <summary>Recursively counts then deletes a directory tree (best-effort). Returns files removed.</summary>
    private static int DeleteTree(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return 0;
        }
        int count = 0;
        try
        {
            count = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count();
        }
        catch
        {
            // ignore
        }
        try
        {
            Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // best-effort: a file may be locked
        }
        return count;
    }

    private static int DeleteFilesIn(string dir, string pattern)
    {
        if (!Directory.Exists(dir))
        {
            return 0;
        }

        int deleted = 0;
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, pattern);
        }
        catch
        {
            return 0;
        }

        foreach (string file in files)
        {
            try
            {
                File.Delete(file);
                deleted++;
            }
            catch
            {
                // best-effort: a file may be locked by a concurrent write
            }
        }
        return deleted;
    }

    /// <summary>Reads a single event file, tagging it with <paramref name="origin"/>. Null on failure.</summary>
    public static AuditEvent? ReadEventFile(string file, AuditOrigin origin) => TryRead(file, origin);

    private static AuditEvent? TryRead(string file, AuditOrigin origin)
    {
        try
        {
            using FileStream fs = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            AuditEvent? ev = JsonSerializer.Deserialize(fs, AuditJsonContext.Default.AuditEvent);
            if (ev is not null)
            {
                ev.Origin = origin;
            }
            return ev;
        }
        catch
        {
            return null;
        }
    }
}
