using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public sealed class ScriptStoreTests : IDisposable
{
    private readonly string _root;

    public ScriptStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sw-store-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Theory]
    [InlineData("ps1", ".ps1")]
    [InlineData(".ps1", ".ps1")]
    [InlineData("", ".txt")]
    public void NormalizeExtension_Works(string input, string expected)
    {
        Assert.Equal(expected, ScriptStore.NormalizeExtension(input));
    }

    [Fact]
    public void Sha256Hex_MatchesKnownVector()
    {
        // SHA-256("abc")
        Assert.Equal(
            "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad",
            ScriptStore.Sha256Hex("abc"u8.ToArray()));
    }

    [Fact]
    public void StoreBytes_Truncates_AtMaxBytes()
    {
        var store = new ScriptStore(_root);
        byte[] big = new byte[ScriptStore.MaxBytes + 1024];
        Array.Fill(big, (byte)'x');

        CapturedScript cs = store.StoreBytes(big, ".ps1", ScriptKind.InlineCommand, ScriptLanguage.PowerShell, null, null);

        Assert.True(cs.Truncated);
        Assert.Equal(ScriptStore.MaxBytes, cs.SizeBytes);
        Assert.Equal(ScriptStore.MaxBytes, new FileInfo(store.ScriptPath(cs.Sha256, cs.Extension)).Length);
    }

    [Fact]
    public void StoreBytes_IdenticalContent_WritesOnce()
    {
        var store = new ScriptStore(_root);
        byte[] content = "duplicate"u8.ToArray();

        CapturedScript a = store.StoreBytes(content, ".ps1", ScriptKind.InlineCommand, ScriptLanguage.PowerShell, null, null);
        CapturedScript b = store.StoreBytes(content, ".ps1", ScriptKind.InlineCommand, ScriptLanguage.PowerShell, null, null);

        Assert.Equal(a.Sha256, b.Sha256);
        Assert.Single(Directory.GetFiles(DataRoots.ScriptsDir(_root)));
    }
}
