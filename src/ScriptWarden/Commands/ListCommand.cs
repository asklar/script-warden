using System.Globalization;
using System.Text.Json;
using ScriptWarden.Core;
using ScriptWarden.Web;

namespace ScriptWarden.Commands;

/// <summary>Prints recent audit events to the console (table or JSON), with optional filters.</summary>
internal static class ListCommand
{
    private static readonly HashSet<string> ValueKeys =
        new(StringComparer.OrdinalIgnoreCase) { "image", "since", "limit" };

    public static int Run(string[] args)
    {
        CliOptions opts = CliOptions.Parse(args, 1, ValueKeys);

        string? imageFilter = opts.Get("image") is { } img ? ImageCatalog.Normalize(img) : null;
        DateTimeOffset? since = null;
        if (opts.Get("since") is { } sinceRaw)
        {
            if (!DateTimeOffset.TryParse(sinceRaw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
            {
                Console.Error.WriteLine($"script-warden: could not parse --since '{sinceRaw}'.");
                return 2;
            }
            since = parsed;
        }

        int limit = 50;
        if (opts.Get("limit") is { } limitRaw && int.TryParse(limitRaw, out int l) && l > 0)
        {
            limit = l;
        }

        // Read from the persistent index (catching it up first). Fall back to scanning the JSON files
        // (events + archive) if the index can't be opened, so `list` always works.
        int total;
        List<AuditEvent> events = QueryIndex(imageFilter, since, limit, out total)
                                  ?? ScanFiles(imageFilter, since, limit, out total);

        IReadOnlyList<ResolvedRoot> roots = DataRoots.ForViewer();

        if (opts.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(events, AuditJsonContext.Default.ListAuditEvent));
            return 0;
        }

        foreach (ResolvedRoot root in roots)
        {
            if (!root.Readable)
            {
                Console.Error.WriteLine($"note: {root.Origin} root not readable: {root.Path}");
            }
        }

        if (events.Count == 0)
        {
            Console.WriteLine("No audit events found.");
            return 0;
        }

        Console.WriteLine($"{"time (UTC)",-20} {"image",-16} {"scr",3} {"exit",4}  {"parent",-16} detail");
        Console.WriteLine(new string('-', 100));

        foreach (AuditEvent e in events)
        {
            string time = e.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string exit = e.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-";
            string parent = Truncate(e.ParentProcessName ?? "", 16);
            string detail = Detail(e);
            Console.WriteLine($"{time,-20} {Truncate(e.HookedImage, 16),-16} {e.Scripts.Count,3} {exit,4}  {parent,-16} {detail}");
        }

        Console.WriteLine();
        Console.WriteLine($"{total} event(s); showing up to {limit}.");
        return 0;
    }

    private static List<AuditEvent>? QueryIndex(string? image, DateTimeOffset? since, int limit, out int total)
    {
        total = 0;
        try
        {
            using var index = new SqliteIndex(SqliteIndex.DefaultDbPath());
            index.Open();
            index.Reconcile();
            SqliteIndex.Page page = index.Query(image, origin: null, parent: null, window: null, search: null,
                offset: 0, limit: limit, sinceUnixMs: since?.ToUnixTimeMilliseconds());
            total = page.Total;
            return page.Events;
        }
        catch
        {
            return null; // fall back to file scan
        }
    }

    private static List<AuditEvent> ScanFiles(string? image, DateTimeOffset? since, int limit, out int total)
    {
        List<AuditEvent> events = AuditStore.ReadAllForViewer(out _, includeArchive: true);
        if (image is not null)
        {
            events = events.Where(e => string.Equals(e.HookedImage, image, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (since is not null)
        {
            events = events.Where(e => e.TimestampUtc >= since.Value).ToList();
        }
        total = events.Count;
        return events.Take(limit).ToList();
    }

    private static string Detail(AuditEvent e)
    {
        if (e.Scripts.Count > 0)
        {
            CapturedScript s = e.Scripts[0];
            return s.OriginalPath ?? $"{s.Kind} ({s.Language})";
        }
        return Truncate(e.CommandLine, 60);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
