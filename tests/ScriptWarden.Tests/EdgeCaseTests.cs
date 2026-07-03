using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public class EdgeCaseTests
{
    [Fact]
    public void Cmd_K_AlsoCapturesReferencedScript()
    {
        var results = ScriptExtractor.Extract("cmd.exe", ["/k", @"C:\it\setup.cmd"], @"C:\w");
        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.FileReference, r.Kind);
        Assert.Equal(@"C:\it\setup.cmd", r.FilePath);
    }

    [Fact]
    public void Pwsh_BehavesLikePowerShell()
    {
        var results = ScriptExtractor.Extract("pwsh.exe", ["-File", @"C:\a.ps1"], @"C:\w");
        Assert.Equal(ScriptKind.FileReference, Assert.Single(results).Kind);
    }

    [Fact]
    public void UnknownImage_ReturnsNothing()
    {
        Assert.Empty(ScriptExtractor.Extract("notepad.exe", ["foo.txt"], @"C:\w"));
    }

    [Fact]
    public void PowerShell_CommandDash_IsNotCaptured()
    {
        // `-Command -` means "read from stdin"; there is nothing to capture.
        Assert.Empty(ScriptExtractor.Extract("powershell.exe", ["-Command", "-"], @"C:\w"));
    }

    [Fact]
    public void EventFileName_IsSortableAndSafe()
    {
        var ev = new AuditEvent
        {
            EventId = "abcdef01-2345-6789-abcd-ef0123456789",
            TimestampUtc = new DateTimeOffset(2026, 7, 2, 23, 55, 8, 302, TimeSpan.Zero),
            ShimProcessId = 4242,
        };

        string name = AuditStore.FormatFileName(ev);
        Assert.Equal("20260702T235508302Z-4242-abcdef012345.json", name);
    }

    [Fact]
    public void Cscript_HostOptionOnly_NoScript_ReturnsNothing()
    {
        Assert.Empty(ScriptExtractor.Extract("cscript.exe", ["//nologo"], @"C:\w"));
    }
}
