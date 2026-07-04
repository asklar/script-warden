using System.Text.RegularExpressions;

namespace ScriptWarden.Core;

/// <summary>
/// Extracts web URLs (<c>http</c>/<c>https</c>/<c>ftp</c>) referenced on a command line, as a
/// strongly-typed list on the audit event. These are the "pull code/content from elsewhere"
/// signals — e.g. an inline command that does <c>iex (irm https://…)</c>. URLs are <b>captured,
/// never fetched</b>: script-warden records what a launch referenced without contacting it.
/// </summary>
public static partial class UrlExtractor
{
    private const int MaxUrls = 64;

    public static List<string> Extract(string? text)
    {
        var urls = new List<string>();
        if (string.IsNullOrEmpty(text))
        {
            return urls;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in UrlRegex().Matches(text))
        {
            // Trim trailing punctuation/closers that commonly abut a URL in shell text
            // (e.g. quotes, parens, commas from `...DownloadString('http://x/y.ps1')`).
            string url = m.Value.TrimEnd('.', ',', ';', ':', ')', ']', '}', '"', '\'', '`', '>');
            if (url.Length == 0)
            {
                continue;
            }
            if (seen.Add(url))
            {
                urls.Add(url);
                if (urls.Count >= MaxUrls)
                {
                    break;
                }
            }
        }
        return urls;
    }

    [GeneratedRegex(
        @"(?:https?|ftp)://[^\s'""`|&<>()\[\]{}]+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlRegex();
}
