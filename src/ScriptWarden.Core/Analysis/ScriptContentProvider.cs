namespace ScriptWarden.Core.Analysis;

/// <summary>
/// Loads and caches the decoded text of captured scripts by content hash, for taxonomy rules that
/// match on <c>scriptContent</c> (and to feed the FTS index). Scripts are content-addressed and
/// de-duplicated, so each unique script is read + decoded at most once. Content is capped so a
/// pathologically large script can't blow up enrichment.
/// </summary>
public sealed class ScriptContentProvider
{
    /// <summary>Max decoded characters kept per script for matching/indexing.</summary>
    public const int MaxChars = 1_000_000;

    private readonly IReadOnlyList<ResolvedRoot> _roots;
    private readonly Dictionary<string, string> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ScriptContentProvider(IReadOnlyList<ResolvedRoot> roots) => _roots = roots;

    /// <summary>Returns the decoded text of the script with this hash, or null if not found/readable.</summary>
    public string? Get(string sha256, string extension)
    {
        if (string.IsNullOrEmpty(sha256))
        {
            return null;
        }
        if (_cache.TryGetValue(sha256, out string? cached))
        {
            return cached;
        }

        string? text = null;
        foreach (ResolvedRoot root in _roots)
        {
            if (!root.Readable)
            {
                continue;
            }
            string path = new ScriptStore(root.Path).ScriptPath(sha256, extension);
            try
            {
                if (File.Exists(path))
                {
                    string decoded = ScriptText.DecodeToText(File.ReadAllBytes(path));
                    text = decoded.Length > MaxChars ? decoded[..MaxChars] : decoded;
                    break;
                }
            }
            catch
            {
                // unreadable; try the next root
            }
        }

        _cache[sha256] = text ?? "";
        return text;
    }

    /// <summary>Decoded contents of all of an event's captured scripts (skips ones that can't be read).</summary>
    public List<string> ForEvent(AuditEvent ev)
    {
        var contents = new List<string>();
        foreach (CapturedScript s in ev.Scripts)
        {
            string? text = Get(s.Sha256, s.Extension);
            if (!string.IsNullOrEmpty(text))
            {
                contents.Add(text);
            }
        }
        return contents;
    }
}
