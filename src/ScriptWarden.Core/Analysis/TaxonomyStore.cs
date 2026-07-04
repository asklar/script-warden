using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ScriptWarden.Core.Analysis;

/// <summary>
/// Loads taxonomies from <c>&lt;root&gt;\taxonomies\*.json</c>, writing the shipped defaults there on
/// first run so they are user-editable. Users can add their own <c>*.json</c> files too. A content
/// hash lets the analyzer detect edits and re-enrich when rules change.
/// </summary>
public static class TaxonomyStore
{
    public static string Dir(string root) => Path.Combine(root, "taxonomies");

    /// <summary>Writes any missing default taxonomy files (never overwrites user edits).</summary>
    public static void EnsureDefaults(string root)
    {
        string dir = Dir(root);
        Directory.CreateDirectory(dir);
        foreach (Taxonomy tax in DefaultTaxonomies.All())
        {
            string path = Path.Combine(dir, tax.Id + ".json");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonSerializer.Serialize(tax, AnalysisJsonContext.Default.Taxonomy));
            }
        }
    }

    /// <summary>Loads all taxonomies (defaults + user), keyed by id (later files win on id collisions).</summary>
    public static List<Taxonomy> Load(string root)
    {
        EnsureDefaults(root);
        var byId = new Dictionary<string, Taxonomy>(StringComparer.OrdinalIgnoreCase);
        string dir = Dir(root);
        if (!Directory.Exists(dir))
        {
            return [];
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(dir, "*.json").OrderBy(static f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }

        foreach (string file in files)
        {
            try
            {
                Taxonomy? tax = JsonSerializer.Deserialize(File.ReadAllText(file), AnalysisJsonContext.Default.Taxonomy);
                if (tax is not null && !string.IsNullOrEmpty(tax.Id))
                {
                    byId[tax.Id] = tax;
                }
            }
            catch
            {
                // skip malformed taxonomy files
            }
        }
        return byId.Values.ToList();
    }

    /// <summary>A stable hash of the taxonomy set, used to detect edits (triggering a re-enrich).</summary>
    public static string ContentHash(IEnumerable<Taxonomy> taxonomies)
    {
        var sb = new StringBuilder();
        foreach (Taxonomy t in taxonomies.OrderBy(static t => t.Id, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(JsonSerializer.Serialize(t, AnalysisJsonContext.Default.Taxonomy));
            sb.Append('\u0001');
        }
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexStringLower(hash);
    }
}
