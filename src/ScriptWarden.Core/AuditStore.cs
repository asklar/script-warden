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

    /// <summary>Reads and merges events from all viewer roots, newest first.</summary>
    public static List<AuditEvent> ReadAllForViewer(out IReadOnlyList<ResolvedRoot> roots)
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
        }
        all.Sort(static (a, b) => b.TimestampUtc.CompareTo(a.TimestampUtc));
        return all;
    }

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
