using System.Text;
using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public sealed class CaptureAndStoreTests : IDisposable
{
    private readonly string _root;
    private readonly string _work;

    public CaptureAndStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "sw-tests", Guid.NewGuid().ToString("N"));
        _work = Path.Combine(_root, "work");
        Directory.CreateDirectory(_work);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch
        {
            // best-effort
        }
    }

    [Fact]
    public void Capture_FileReference_StoresContentByHash()
    {
        string scriptPath = Path.Combine(_work, "deploy.ps1");
        const string content = "Write-Host 'from file'";
        File.WriteAllText(scriptPath, content);

        var captured = Capturer.Capture(_root, "powershell.exe", ["-File", scriptPath], _work);

        var cs = Assert.Single(captured);
        Assert.NotEqual("", cs.Sha256);
        Assert.Equal(".ps1", cs.Extension);

        string stored = new ScriptStore(_root).ScriptPath(cs.Sha256, cs.Extension);
        Assert.True(File.Exists(stored));
        Assert.Equal(content, File.ReadAllText(stored));
    }

    [Fact]
    public void Capture_InlineCommand_StoresText()
    {
        var captured = Capturer.Capture(_root, "cmd.exe", ["/c", "echo", "hi"], _work);
        var cs = Assert.Single(captured);
        string stored = new ScriptStore(_root).ScriptPath(cs.Sha256, cs.Extension);
        Assert.Equal("echo hi", File.ReadAllText(stored));
    }

    [Fact]
    public void Capture_IdenticalContent_IsDeduplicated()
    {
        var a = Capturer.Capture(_root, "cmd.exe", ["/c", "echo same"], _work);
        var b = Capturer.Capture(_root, "cmd.exe", ["/c", "echo same"], _work);

        Assert.Equal(a[0].Sha256, b[0].Sha256);
        int fileCount = Directory.GetFiles(DataRoots.ScriptsDir(_root)).Length;
        Assert.Equal(1, fileCount);
    }

    [Fact]
    public void Capture_MissingFile_RecordsNote()
    {
        var captured = Capturer.Capture(_root, "powershell.exe", ["-File", Path.Combine(_work, "nope.ps1")], _work);
        var cs = Assert.Single(captured);
        Assert.Equal("", cs.Sha256);
        Assert.Contains("file not found", cs.Note!);
    }

    [Fact]
    public void AuditStore_WriteThenRead_RoundTrips()
    {
        var ev = new AuditEvent
        {
            EventId = Guid.NewGuid().ToString(),
            TimestampUtc = DateTimeOffset.UtcNow,
            HookedImage = "powershell.exe",
            TargetPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            CommandLine = "\"...powershell.exe\" -File a.ps1",
            Arguments = ["-File", "a.ps1"],
            ShimProcessId = 1234,
            ExitCode = 0,
            Scripts = [new CapturedScript { Kind = ScriptKind.FileReference, Sha256 = "abc", Extension = ".ps1" }],
        };

        AuditStore.WriteEvent(_root, ev);
        var read = AuditStore.ReadEvents(_root, AuditOrigin.CurrentUser).ToList();

        var got = Assert.Single(read);
        Assert.Equal(ev.EventId, got.EventId);
        Assert.Equal(AuditOrigin.CurrentUser, got.Origin);
        Assert.Equal("powershell.exe", got.HookedImage);
        Assert.Equal(0, got.ExitCode);
        Assert.Single(got.Scripts);
    }
}
