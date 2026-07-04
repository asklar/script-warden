using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace ScriptWarden.Core.Analysis;

/// <summary>
/// Evaluates a data-driven <see cref="Taxonomy"/> against an <see cref="EventFacts"/> projection.
/// Pure and deterministic: no IO, no reflection. User-supplied regexes run interpreted (AOT-safe)
/// and are cached; malformed rules/patterns simply never match rather than throwing.
/// </summary>
public static class TaxonomyEngine
{
    private static readonly StringComparison Ic = StringComparison.OrdinalIgnoreCase;
    private static readonly ConcurrentDictionary<string, Regex?> RegexCache = new();

    /// <summary>Returns the labels the taxonomy assigns to the event (multi-label or first-match).</summary>
    public static List<string> Classify(Taxonomy taxonomy, EventFacts facts)
    {
        var labels = new List<string>();
        foreach (Rule rule in taxonomy.Rules)
        {
            if (rule.Default)
            {
                continue; // applied only if nothing else matched
            }
            if (Matches(rule, facts))
            {
                labels.Add(rule.Label);
                if (!taxonomy.MultiLabel)
                {
                    return labels;
                }
            }
        }

        if (labels.Count == 0)
        {
            foreach (Rule rule in taxonomy.Rules)
            {
                if (rule.Default)
                {
                    labels.Add(rule.Label);
                    break;
                }
            }
        }
        return labels;
    }

    private static bool Matches(Rule rule, EventFacts facts)
    {
        if (rule.All is { Count: > 0 })
        {
            foreach (Predicate p in rule.All)
            {
                if (!Eval(p, facts))
                {
                    return false;
                }
            }
            return true;
        }
        if (rule.Any is { Count: > 0 })
        {
            foreach (Predicate p in rule.Any)
            {
                if (Eval(p, facts))
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool Eval(Predicate p, EventFacts facts)
    {
        if (string.IsNullOrEmpty(p.Field) || string.IsNullOrEmpty(p.Op))
        {
            return false;
        }

        string op = p.Op.ToLowerInvariant();
        if (op == "istrue")
        {
            return BoolField(p.Field, facts);
        }

        IReadOnlyList<string> values = Values(p.Field, facts);
        if (values.Count == 0)
        {
            return false;
        }

        switch (op)
        {
            case "equals":
                return p.Value is not null && values.Any(v => string.Equals(v, p.Value, Ic));
            case "in":
                return p.Values is { Count: > 0 } && values.Any(v => p.Values.Any(x => string.Equals(v, x, Ic)));
            case "contains":
                return !string.IsNullOrEmpty(p.Value) && values.Any(v => v.Contains(p.Value, Ic));
            case "startswith":
                return !string.IsNullOrEmpty(p.Value) && values.Any(v => v.StartsWith(p.Value, Ic));
            case "regex":
                Regex? rx = string.IsNullOrEmpty(p.Value) ? null : GetRegex(p.Value);
                return rx is not null && values.Any(rx.IsMatch);
            default:
                return false;
        }
    }

    private static bool BoolField(string field, EventFacts f) => field.ToLowerInvariant() switch
    {
        "hasurl" => f.HasUrl,
        _ => false,
    };

    private static IReadOnlyList<string> Values(string field, EventFacts f)
    {
        switch (field.ToLowerInvariant())
        {
            case "hookedimage": return One(f.HookedImage);
            case "commandline": return One(f.CommandLine);
            case "targetpath": return One(f.TargetPath);
            case "user": return One(f.User);
            case "usersid": return One(f.UserSid);
            case "window": return One(f.Window);
            case "origin": return One(f.Origin);
            case "exitcode": return One(f.ExitCode);
            case "parentname": return One(f.ParentName);
            case "ancestorname": return f.AncestorNames;
            case "ancestorpath": return f.AncestorPaths;
            case "url": return f.Urls;
            case "scriptcontent": return f.ScriptContents;
            default: return [];
        }
    }

    private static IReadOnlyList<string> One(string? value) =>
        string.IsNullOrEmpty(value) ? [] : [value];

    private static Regex? GetRegex(string pattern) => RegexCache.GetOrAdd(pattern, static p =>
    {
        try
        {
            return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }
        catch
        {
            return null; // malformed user pattern: never matches
        }
    });
}
