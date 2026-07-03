using System.Globalization;
using System.Text.Json;
using ScriptWarden.Core;

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

        List<AuditEvent> events = AuditStore.ReadAllForViewer(out IReadOnlyList<ResolvedRoot> roots);

        if (imageFilter is not null)
        {
            events = events.Where(e => string.Equals(e.HookedImage, imageFilter, StringComparison.OrdinalIgnoreCase)).ToList();
        }
        if (since is not null)
        {
            events = events.Where(e => e.TimestampUtc >= since.Value).ToList();
        }

        if (opts.Has("json"))
        {
            List<AuditEvent> limited = events.Take(limit).ToList();
            Console.WriteLine(JsonSerializer.Serialize(limited, AuditJsonContext.Default.ListAuditEvent));
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

        foreach (AuditEvent e in events.Take(limit))
        {
            string time = e.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string exit = e.ExitCode?.ToString(CultureInfo.InvariantCulture) ?? "-";
            string parent = Truncate(e.ParentProcessName ?? "", 16);
            string detail = Detail(e);
            Console.WriteLine($"{time,-20} {Truncate(e.HookedImage, 16),-16} {e.Scripts.Count,3} {exit,4}  {parent,-16} {detail}");
        }

        Console.WriteLine();
        Console.WriteLine($"{events.Count} event(s); showing up to {limit}.");
        return 0;
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
