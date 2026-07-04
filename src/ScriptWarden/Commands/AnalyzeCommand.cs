using System.Globalization;
using System.Text.Json;
using ScriptWarden.Analysis;
using ScriptWarden.Core;
using ScriptWarden.Core.Analysis;

namespace ScriptWarden.Commands;

/// <summary>
/// The explicit analysis gesture: ingests events into the persistent <c>analysis.db</c>, enriches them
/// with the (data-driven) taxonomies, then groups/searches. Nothing on the shim/viewer path changes.
/// </summary>
internal static class AnalyzeCommand
{
    private static readonly HashSet<string> ValueKeys =
        new(StringComparer.OrdinalIgnoreCase) { "group-by", "filter-taxonomy", "filter-label", "search", "limit" };

    public static int Run(string[] args)
    {
        CliOptions opts = CliOptions.Parse(args, 1, ValueKeys);
        string root = DataRoots.CurrentUserRoot();
        List<Taxonomy> taxonomies = TaxonomyStore.Load(root);

        if (opts.Has("taxonomies"))
        {
            Console.WriteLine("Available taxonomies (edit/add JSON under " + TaxonomyStore.Dir(root) + "):");
            foreach (Taxonomy t in taxonomies)
            {
                Console.WriteLine($"  {t.Id,-12} {t.Name}{(t.MultiLabel ? "  (multi-label)" : "")}");
            }
            return 0;
        }

        int limit = opts.Get("limit") is { } lr && int.TryParse(lr, out int l) && l > 0 ? l : 50;

        using var store = new AnalysisStore(AnalysisStore.DefaultDbPath());
        try
        {
            store.Open();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"script-warden: could not open the analysis database: {ex.Message}");
            return 1;
        }

        var content = new ScriptContentProvider(DataRoots.ForViewer());
        AnalysisStore.RefreshResult refresh = store.Refresh(taxonomies, content);
        Console.WriteLine($"Analyzed {refresh.Total} event(s) (+{refresh.Ingested} new{(refresh.Reclassified ? ", re-labeled after taxonomy change" : "")}).");
        Console.WriteLine();

        var filters = new List<AnalysisFilter>();
        if (opts.Get("filter-taxonomy") is { Length: > 0 } filterTax && opts.Get("filter-label") is { Length: > 0 } filterLabel)
        {
            filters.Add(new AnalysisFilter { Type = "taxonomy", Taxonomy = filterTax, Op = "include", Labels = [filterLabel] });
        }

        if (opts.Get("search") is { Length: > 0 } query)
        {
            filters.Add(new AnalysisFilter { Type = "content", Query = query });
            return PrintEvents(store, filters, limit, opts.Has("json"), $"Scripts mentioning \"{query}\"");
        }

        string groupBy = opts.Get("group-by") ?? "source";
        if (!taxonomies.Any(t => string.Equals(t.Id, groupBy, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"script-warden: no taxonomy '{groupBy}'. Try: {string.Join(", ", taxonomies.Select(t => t.Id))}");
            return 2;
        }

        List<AnalysisStore.RollupRow> rows = store.Rollup(groupBy, filters);
        int totalEvents = store.Count();

        if (opts.Has("json"))
        {
            var dto = rows.Select(r => new RollupRowDto { Label = r.Label, Count = r.Count, TotalMs = r.TotalMs }).ToList();
            Console.WriteLine(JsonSerializer.Serialize(dto, AnalysisApiJsonContext.Default.ListRollupRowDto));
            return 0;
        }

        Taxonomy tax = taxonomies.First(t => string.Equals(t.Id, groupBy, StringComparison.OrdinalIgnoreCase));
        string filterNote = filters.Count > 0 ? $"  (filtered)" : "";
        Console.WriteLine($"Group by: {tax.Name}{filterNote}");
        Console.WriteLine($"{"label",-26} {"launches",8}   {"total time",10}   {"%",6}");
        Console.WriteLine(new string('-', 60));
        foreach (AnalysisStore.RollupRow r in rows)
        {
            double pct = totalEvents == 0 ? 0 : 100.0 * r.Count / totalEvents;
            Console.WriteLine($"{Truncate(r.Label, 26),-26} {r.Count,8}   {FmtDuration(r.TotalMs),10}   {pct,5:0.0}%");
        }
        if (rows.Count == 0)
        {
            Console.WriteLine("(no matching events)");
        }
        return 0;
    }

    private static int PrintEvents(AnalysisStore store, List<AnalysisFilter> filters, int limit, bool json, string header)
    {
        (int total, List<AuditEvent> events) = store.DrillEvents("", null, filters, 0, limit);
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(events, AuditJsonContext.Default.ListAuditEvent));
            return 0;
        }
        Console.WriteLine($"{header}: {total} event(s); showing up to {limit}.");
        Console.WriteLine($"{"time (UTC)",-20} {"image",-16} detail");
        Console.WriteLine(new string('-', 80));
        foreach (AuditEvent e in events)
        {
            string time = e.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            string detail = e.Scripts.Count > 0 ? (e.Scripts[0].OriginalPath ?? $"{e.Scripts[0].Kind}") : Truncate(e.CommandLine, 50);
            Console.WriteLine($"{time,-20} {Truncate(e.HookedImage, 16),-16} {detail}");
        }
        return 0;
    }

    private static string FmtDuration(long ms)
    {
        if (ms <= 0) return "—";
        if (ms < 1000) return $"{ms} ms";
        double s = ms / 1000.0;
        if (s < 60) return $"{s:0.0} s";
        int m = (int)(s / 60);
        return $"{m}m {(int)(s % 60)}s";
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
