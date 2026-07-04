namespace ScriptWarden.Core.Analysis;

/// <summary>
/// The projection of an <see cref="AuditEvent"/> that taxonomy rules are evaluated against — a flat
/// bag of the fields the rule vocabulary exposes. Built once per event during enrichment. Multi-valued
/// fields (ancestors, urls, script contents) match a predicate when <em>any</em> of their values do.
/// Kept free of IO so the engine is pure and testable; script contents are supplied by the caller
/// (loaded + de-duplicated by sha256 at the <c>analyze</c> gesture).
/// </summary>
public sealed class EventFacts
{
    public string? HookedImage { get; init; }
    public string? CommandLine { get; init; }
    public string? TargetPath { get; init; }
    public string? User { get; init; }
    public string? UserSid { get; init; }
    public string? Window { get; init; }
    public string? ParentName { get; init; }
    public IReadOnlyList<string> AncestorNames { get; init; } = [];
    public IReadOnlyList<string> AncestorPaths { get; init; } = [];
    public IReadOnlyList<string> Urls { get; init; } = [];
    public IReadOnlyList<string> ScriptContents { get; init; } = [];

    public bool HasUrl => Urls.Count > 0;

    /// <summary>Builds facts from an event plus the decoded contents of its captured scripts.</summary>
    public static EventFacts From(AuditEvent ev, IReadOnlyList<string>? scriptContents = null)
    {
        var names = new List<string>();
        var paths = new List<string>();
        foreach (ProcessRef a in ev.Ancestors)
        {
            if (!string.IsNullOrEmpty(a.Name)) names.Add(a.Name!);
            if (!string.IsNullOrEmpty(a.Path)) paths.Add(a.Path!);
        }
        // Ensure the immediate parent is represented even when the full ancestor chain wasn't
        // captured (events recorded before ancestor-chain capture existed have only ParentProcess*).
        if (!string.IsNullOrEmpty(ev.ParentProcessName) &&
            !names.Any(n => string.Equals(n, ev.ParentProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            names.Insert(0, ev.ParentProcessName!);
        }
        if (!string.IsNullOrEmpty(ev.ParentProcessPath) &&
            !paths.Any(p => string.Equals(p, ev.ParentProcessPath, StringComparison.OrdinalIgnoreCase)))
        {
            paths.Insert(0, ev.ParentProcessPath!);
        }
        return new EventFacts
        {
            HookedImage = ev.HookedImage,
            CommandLine = ev.CommandLine,
            TargetPath = ev.TargetPath,
            User = ev.User,
            UserSid = ev.UserSid,
            Window = ev.Window.ToString(),
            ParentName = ev.ParentProcessName,
            AncestorNames = names,
            AncestorPaths = paths,
            Urls = ev.Urls,
            ScriptContents = scriptContents ?? [],
        };
    }
}
