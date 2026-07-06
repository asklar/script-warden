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

    // Records the hash of exactly what we last wrote for each default id, so a later build can tell an
    // untouched default file (safe to auto-upgrade) from one the user edited (must be preserved).
    private static string ManifestPath(string root) => Path.Combine(Dir(root), ".shipped.json");

    /// <summary>
    /// Hashes of previously-shipped default files we still recognize as "untouched", keyed by id. Lets
    /// installs that predate the manifest pick up improved built-ins without clobbering real edits.
    /// Add an entry here whenever a default's content changes so old-but-unedited files upgrade cleanly.
    /// </summary>
    private static readonly Dictionary<string, HashSet<string>> KnownPriorHashes = new(StringComparer.OrdinalIgnoreCase)
    {
        // "source" before "Dev tools (mine)" gained dotnet.exe / MSBuild.exe.
        ["source"] = ["6ef4d60cc4402fd76d6039a7748ba4f9655734ce78cb83db1e9a5340c4b0c323"],
    };

    /// <summary>
    /// Writes any missing default taxonomy files, and upgrades default files that are still identical
    /// to a version we shipped (i.e. the user never edited them) to the current built-ins. Files the
    /// user has edited, and any custom taxonomies, are never touched.
    /// </summary>
    public static void EnsureDefaults(string root) => SyncDefaults(root, force: false);

    /// <summary>
    /// Overwrites the shipped default taxonomy files with the current built-ins even if edited. User
    /// authored (non-default id) taxonomies are left alone. Returns the ids that were (re)written.
    /// </summary>
    public static List<string> ResetDefaults(string root) => SyncDefaults(root, force: true);

    private static List<string> SyncDefaults(string root, bool force)
    {
        string dir = Dir(root);
        Directory.CreateDirectory(dir);
        Dictionary<string, string> manifest = LoadManifest(root);
        var written = new List<string>();
        bool manifestChanged = false;

        foreach (Taxonomy tax in DefaultTaxonomies.All())
        {
            string path = Path.Combine(dir, tax.Id + ".json");
            string shipped = JsonSerializer.Serialize(tax, AnalysisJsonContext.Default.Taxonomy);
            string shippedHash = Sha(shipped);

            if (!File.Exists(path))
            {
                File.WriteAllText(path, shipped);
                manifest[tax.Id] = shippedHash;
                manifestChanged = true;
                written.Add(tax.Id);
                continue;
            }

            string onDisk;
            try { onDisk = File.ReadAllText(path); }
            catch { continue; }
            string onDiskHash = Sha(onDisk);

            if (onDiskHash == shippedHash)
            {
                if (!manifest.TryGetValue(tax.Id, out string? mh) || mh != shippedHash)
                {
                    manifest[tax.Id] = shippedHash;
                    manifestChanged = true;
                }
                continue;
            }

            // The on-disk file differs from the current built-in. Upgrade it only when forced, or when
            // it is untouched — i.e. it still matches whatever we last wrote (manifest) or a known prior
            // shipped version. Otherwise it carries user edits and must be preserved.
            bool untouched =
                (manifest.TryGetValue(tax.Id, out string? last) && last == onDiskHash) ||
                (KnownPriorHashes.TryGetValue(tax.Id, out HashSet<string>? priors) && priors.Contains(onDiskHash));

            if (force || untouched)
            {
                File.WriteAllText(path, shipped);
                manifest[tax.Id] = shippedHash;
                manifestChanged = true;
                written.Add(tax.Id);
            }
        }

        if (manifestChanged)
        {
            SaveManifest(root, manifest);
        }
        return written;
    }

    private static Dictionary<string, string> LoadManifest(string root)
    {
        string path = ManifestPath(root);
        if (!File.Exists(path))
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
        try
        {
            Dictionary<string, string>? m = JsonSerializer.Deserialize(File.ReadAllText(path), AnalysisJsonContext.Default.DictionaryStringString);
            return m is null ? new(StringComparer.OrdinalIgnoreCase) : new(m, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void SaveManifest(string root, Dictionary<string, string> manifest)
    {
        try
        {
            File.WriteAllText(ManifestPath(root), JsonSerializer.Serialize(manifest, AnalysisJsonContext.Default.DictionaryStringString));
        }
        catch
        {
            // best-effort: a missing manifest just means we re-evaluate against KnownPriorHashes next time
        }
    }

    private static string Sha(string content) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

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
