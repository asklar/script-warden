using System.Text.Json.Serialization;

namespace ScriptWarden.Core;

/// <summary>A page of query results plus the total count (for pagination controls).</summary>
public sealed class EventsPage
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public List<AuditEvent> Events { get; set; } = [];
}

/// <summary>Distinct filter values across the whole set (for populating the UI dropdowns).</summary>
public sealed class AuditFacets
{
    public List<string> Images { get; set; } = [];
    public List<string> Parents { get; set; } = [];
    public List<string> Windows { get; set; } = [];
}

/// <summary>
/// Pure filtering / sorting / paging over a set of events. Kept free of IO so the viewer can cache
/// events in memory and serve fast, correctly-paginated queries without re-reading disk each time.
/// </summary>
public static class AuditQuery
{
    public const string All = "all";

    public static EventsPage Query(
        IReadOnlyList<AuditEvent> events,
        string? image = null,
        string? origin = null,
        string? parent = null,
        string? window = null,
        string? search = null,
        int offset = 0,
        int limit = 100)
    {
        if (offset < 0)
        {
            offset = 0;
        }
        if (limit <= 0)
        {
            limit = 100;
        }

        string? q = string.IsNullOrWhiteSpace(search) ? null : search.Trim().ToLowerInvariant();

        var filtered = new List<AuditEvent>();
        foreach (AuditEvent e in events)
        {
            if (!MatchesExact(image, e.HookedImage)) continue;
            if (!MatchesExact(origin, e.Origin.ToString())) continue;
            if (!MatchesExact(parent, e.ParentProcessName ?? "")) continue;
            if (!MatchesExact(window, e.Window.ToString())) continue;
            if (q is not null && !MatchesSearch(e, q)) continue;
            filtered.Add(e);
        }

        filtered.Sort(static (a, b) => b.TimestampUtc.CompareTo(a.TimestampUtc));

        int total = filtered.Count;
        List<AuditEvent> page = offset >= total
            ? []
            : filtered.GetRange(offset, Math.Min(limit, total - offset));

        return new EventsPage { Total = total, Offset = offset, Limit = limit, Events = page };
    }

    public static AuditFacets Facets(IReadOnlyList<AuditEvent> events)
    {
        var images = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var parents = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var windows = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (AuditEvent e in events)
        {
            if (!string.IsNullOrEmpty(e.HookedImage)) images.Add(e.HookedImage);
            if (!string.IsNullOrEmpty(e.ParentProcessName)) parents.Add(e.ParentProcessName!);
            windows.Add(e.Window.ToString());
        }

        return new AuditFacets
        {
            Images = [.. images],
            Parents = [.. parents],
            Windows = [.. windows],
        };
    }

    private static bool MatchesExact(string? filter, string value) =>
        filter is null || filter == All || string.Equals(filter, value, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesSearch(AuditEvent e, string q)
    {
        if (Contains(e.HookedImage, q)) return true;
        if (Contains(e.CommandLine, q)) return true;
        if (Contains(e.User, q)) return true;
        if (Contains(e.ParentProcessName, q)) return true;
        if (Contains(e.ParentProcessPath, q)) return true;
        if (Contains(e.Origin.ToString(), q)) return true;
        if (Contains(e.Window.ToString(), q)) return true;
        foreach (CapturedScript s in e.Scripts)
        {
            if (Contains(s.OriginalPath, q) || Contains(s.Note, q) || Contains(s.Kind.ToString(), q))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Contains(string? value, string lowerNeedle) =>
        value is not null && value.Contains(lowerNeedle, StringComparison.OrdinalIgnoreCase);
}
