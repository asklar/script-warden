using System.Text;
using System.Text.Json;
using ScriptWarden.Core.Analysis;

namespace ScriptWarden.Tests;

public sealed class TaxonomyStoreTests : IDisposable
{
    private readonly string _root;

    public TaxonomyStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sw-taxstore-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch { /* best-effort */ }
    }

    private string Dir => TaxonomyStore.Dir(_root);
    private string SourcePath => Path.Combine(Dir, "source.json");
    private string ManifestPath => Path.Combine(Dir, ".shipped.json");

    private static string Sha(string s) =>
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(s)));

    [Fact]
    public void EnsureDefaults_WritesAllShippedTaxonomies_AndManifest()
    {
        TaxonomyStore.EnsureDefaults(_root);
        foreach (Taxonomy t in DefaultTaxonomies.All())
        {
            Assert.True(File.Exists(Path.Combine(Dir, t.Id + ".json")), $"missing {t.Id}.json");
        }
        Assert.True(File.Exists(ManifestPath));
    }

    [Fact]
    public void EnsureDefaults_IsIdempotent_LeavesUntouchedFileByteIdentical()
    {
        TaxonomyStore.EnsureDefaults(_root);
        string before = File.ReadAllText(SourcePath);
        TaxonomyStore.EnsureDefaults(_root); // matches manifest → no rewrite
        Assert.Equal(before, File.ReadAllText(SourcePath));
    }

    [Fact]
    public void EnsureDefaults_PreservesUserEditedDefault()
    {
        TaxonomyStore.EnsureDefaults(_root);
        // A default the user edited (hash is neither the manifest hash nor a known prior).
        const string edited = "{\"id\":\"source\",\"name\":\"Mine\",\"rules\":[]}";
        File.WriteAllText(SourcePath, edited);
        TaxonomyStore.EnsureDefaults(_root);
        Assert.Equal(edited, File.ReadAllText(SourcePath));
    }

    [Fact]
    public void EnsureDefaults_UpgradesUntouchedFile_ViaManifestBaseline()
    {
        // Seed defaults, then reproduce the "untouched but from a prior build" case: replace source.json
        // with a variant and point the manifest at THAT variant's hash (i.e. "we last shipped this").
        // Because it still matches the manifest, EnsureDefaults must upgrade it back to the current default.
        TaxonomyStore.EnsureDefaults(_root);
        string current = File.ReadAllText(SourcePath);

        const string variant = "{\"id\":\"source\",\"name\":\"Prior\",\"rules\":[]}";
        File.WriteAllText(SourcePath, variant);
        File.WriteAllText(ManifestPath, JsonSerializer.Serialize(
            new Dictionary<string, string> { ["source"] = Sha(variant) },
            AnalysisJsonContext.Default.DictionaryStringString));

        TaxonomyStore.EnsureDefaults(_root);

        Assert.Equal(current, File.ReadAllText(SourcePath));
    }

    [Fact]
    public void ResetDefaults_OverwritesEditedDefault_ButLeavesCustomTaxonomies()
    {
        TaxonomyStore.EnsureDefaults(_root);
        File.WriteAllText(SourcePath, "{\"id\":\"source\",\"name\":\"Mine\",\"rules\":[]}");
        string customPath = Path.Combine(Dir, "mytax.json");
        const string custom = "{\"id\":\"mytax\",\"name\":\"Custom\",\"rules\":[]}";
        File.WriteAllText(customPath, custom);

        List<string> reset = TaxonomyStore.ResetDefaults(_root);

        Assert.Contains("source", reset);
        Assert.DoesNotContain("mytax", reset);
        Taxonomy? restored = JsonSerializer.Deserialize(File.ReadAllText(SourcePath), AnalysisJsonContext.Default.Taxonomy);
        Assert.NotNull(restored);
        Assert.NotEmpty(restored!.Rules);
        Assert.Equal(custom, File.ReadAllText(customPath));
    }

    [Fact]
    public void Load_UserFileWinsOnIdCollision_AndIsNotClobbered()
    {
        TaxonomyStore.EnsureDefaults(_root);
        File.WriteAllText(SourcePath, "{\"id\":\"source\",\"name\":\"Overridden\",\"rules\":[]}");
        List<Taxonomy> loaded = TaxonomyStore.Load(_root); // Load calls EnsureDefaults internally
        Assert.Equal("Overridden", loaded.First(t => t.Id == "source").Name);
    }
}
