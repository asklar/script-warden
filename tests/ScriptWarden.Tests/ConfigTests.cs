using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public class ConfigTests
{
    [Fact]
    public void DefaultConfig_ExcludesNothing()
    {
        var c = new WardenConfig();
        Assert.False(c.IsExcluded("powershell.exe", "explorer.exe"));
        Assert.False(c.IsExcluded("cmd.exe", null));
    }

    [Fact]
    public void Disabled_ExcludesEverything()
    {
        var c = new WardenConfig { Enabled = false };
        Assert.True(c.IsExcluded("powershell.exe", "explorer.exe"));
    }

    [Theory]
    [InlineData("copilot.exe", true)]
    [InlineData("Copilot.EXE", true)]
    [InlineData("copilot", true)]        // .exe optional
    [InlineData("explorer.exe", false)]
    public void ExcludedParents_MatchCaseInsensitiveWithOptionalExe(string parent, bool excluded)
    {
        var c = new WardenConfig { ExcludedParents = ["copilot.exe"] };
        Assert.Equal(excluded, c.IsExcluded("powershell.exe", parent));
    }

    [Fact]
    public void ExcludedImages_Match()
    {
        var c = new WardenConfig { ExcludedImages = ["cmd.exe"] };
        Assert.True(c.IsExcluded("cmd.exe", "explorer.exe"));
        Assert.False(c.IsExcluded("powershell.exe", "explorer.exe"));
    }
}

public sealed class ConfigStoreTests : IDisposable
{
    private readonly string _root;

    public ConfigStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sw-config-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void Load_Missing_ReturnsDefault()
    {
        var c = ConfigStore.Load(_root);
        Assert.True(c.Enabled);
        Assert.Empty(c.ExcludedParents);
        Assert.Empty(c.ExcludedImages);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var c = new WardenConfig { Enabled = false, ExcludedParents = ["copilot.exe"], ExcludedImages = ["cmd.exe"] };
        ConfigStore.Save(_root, c);

        var read = ConfigStore.Load(_root);
        Assert.False(read.Enabled);
        Assert.Equal(["copilot.exe"], read.ExcludedParents);
        Assert.Equal(["cmd.exe"], read.ExcludedImages);
    }
}

public sealed class ClearRootTests : IDisposable
{
    private readonly string _root;

    public ClearRootTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sw-clear-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
    }

    [Fact]
    public void ClearRoot_DeletesEventsAndScripts()
    {
        // Seed one event + one captured script.
        Capturer.Capture(_root, "cmd.exe", ["/c", "echo hi"], _root);
        AuditStore.WriteEvent(_root, new AuditEvent { EventId = Guid.NewGuid().ToString(), TimestampUtc = DateTimeOffset.UtcNow, HookedImage = "cmd.exe", ShimProcessId = 1 });

        Assert.NotEmpty(Directory.GetFiles(DataRoots.EventsDir(_root)));
        Assert.NotEmpty(Directory.GetFiles(DataRoots.ScriptsDir(_root)));

        (int events, int scripts) = AuditStore.ClearRoot(_root);

        Assert.True(events >= 1);
        Assert.True(scripts >= 1);
        Assert.Empty(Directory.GetFiles(DataRoots.EventsDir(_root)));
        Assert.Empty(Directory.GetFiles(DataRoots.ScriptsDir(_root)));
    }

    [Fact]
    public void ClearRoot_MissingDirs_ReturnsZero()
    {
        (int events, int scripts) = AuditStore.ClearRoot(Path.Combine(_root, "nope"));
        Assert.Equal(0, events);
        Assert.Equal(0, scripts);
    }
}
