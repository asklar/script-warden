namespace ScriptWarden;

/// <summary>Minimal, AOT-friendly options parser: <c>--key value</c> and boolean <c>--flag</c>.</summary>
internal sealed class CliOptions
{
    private readonly Dictionary<string, string> _values = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

    public static CliOptions Parse(string[] args, int start, ISet<string> valueKeys)
    {
        var o = new CliOptions();
        for (int i = start; i < args.Length; i++)
        {
            string a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            string key = a[2..];
            if (valueKeys.Contains(key) && i + 1 < args.Length)
            {
                o._values[key] = args[++i];
            }
            else
            {
                o._flags.Add(key);
            }
        }
        return o;
    }

    public string? Get(string key) => _values.TryGetValue(key, out string? v) ? v : null;

    public bool Has(string key) => _flags.Contains(key) || _values.ContainsKey(key);
}
