using ScriptWarden.Core;
using ScriptWarden.Web;

namespace ScriptWarden.Cli.Tests;

[CollectionDefinition("DataRoots")]
public class DataRootsCollection { }

/// <summary>
/// Exercises the viewer's SQLite index end to end against a temp data root. Uses a unique hooked
/// image name so assertions are isolated from any real SYSTEM-root events on the host, and runs in
/// the "DataRoots" collection so the SCRIPT_WARDEN_DATA env override is never observed concurrently.
/// </summary>
[Collection("DataRoots")]
public class SqliteIndexTests : IDisposable
{
    private const string TestImage = "sw-index-test.exe";
    private readonly string _root;
    private readonly string? _previous;

    public SqliteIndexTests()
    {
        _previous = Environment.GetEnvironmentVariable(DataRoots.EnvOverride);
        _root = Path.Combine(Path.GetTempPath(), "sw-index-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Environment.SetEnvironmentVariable(DataRoots.EnvOverride, _root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(DataRoots.EnvOverride, _previous);
        try { Directory.Delete(_root, recursive: true); } catch { /* best-effort */ }
        GC.SuppressFinalize(this);
    }

    private static AuditEvent MakeEvent(int pid, int? exit)
    {
        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString(),
            TimestampUtc = DateTimeOffset.UtcNow,
            HookedImage = TestImage,
            TargetPath = @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe",
            CommandLine = "\"powershell.exe\" -Command \"Get-Process\"",
            ShimProcessId = pid,
            ExitCode = exit,
            DurationMs = exit is null ? null : 42,
        };
    }

    private SqliteIndex OpenIndex()
    {
        var idx = new SqliteIndex(Path.Combine(_root, "index.db"));
        idx.Open();
        return idx;
    }

    [Fact]
    public void Reconcile_IngestsQueries_ArchivesCompleted_KeepsInFlight()
    {
        AuditEvent completed = MakeEvent(1001, exit: 0);
        AuditEvent inflight = MakeEvent(1002, exit: null);
        AuditStore.WriteEvent(_root, completed);
        AuditStore.WriteEvent(_root, inflight);

        using SqliteIndex idx = OpenIndex();
        idx.Reconcile();

        SqliteIndex.Page page = idx.Query(TestImage, null, null, null, null, 0, 100);
        Assert.Equal(2, page.Total);
        Assert.Contains(page.Events, e => e.EventId == completed.EventId && e.ExitCode == 0);
        Assert.Contains(page.Events, e => e.EventId == inflight.EventId && e.ExitCode is null);

        // Completed event moved to archive\<day>\; in-flight event still in events\.
        string day = completed.TimestampUtc.UtcDateTime.ToString("yyyy-MM-dd");
        Assert.True(File.Exists(Path.Combine(DataRoots.ArchiveDir(_root), day, AuditStore.FormatFileName(completed))));
        Assert.False(File.Exists(Path.Combine(DataRoots.EventsDir(_root), AuditStore.FormatFileName(completed))));
        Assert.True(File.Exists(Path.Combine(DataRoots.EventsDir(_root), AuditStore.FormatFileName(inflight))));
    }

    [Fact]
    public void Reconcile_TwoPhaseWrite_UpdatesExitThenArchives()
    {
        AuditEvent ev = MakeEvent(2001, exit: null);
        AuditStore.WriteEvent(_root, ev);

        using SqliteIndex idx = OpenIndex();
        idx.Reconcile();
        Assert.Null(idx.Query(TestImage, null, null, null, null, 0, 100).Events.Single().ExitCode);

        // Second (final) write: same file, now with an exit code + duration.
        ev.ExitCode = 7;
        ev.DurationMs = 99;
        AuditStore.WriteEvent(_root, ev);
        idx.Reconcile();

        AuditEvent row = idx.Query(TestImage, null, null, null, null, 0, 100).Events.Single();
        Assert.Equal(7, row.ExitCode);
        Assert.Equal(99, row.DurationMs);
        // Now completed -> archived out of events\.
        Assert.False(File.Exists(Path.Combine(DataRoots.EventsDir(_root), AuditStore.FormatFileName(ev))));
    }

    [Fact]
    public void Query_SinceFiltersByTimestamp()
    {
        AuditStore.WriteEvent(_root, MakeEvent(3001, exit: 0));
        using SqliteIndex idx = OpenIndex();
        idx.Reconcile();

        long future = DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeMilliseconds();
        SqliteIndex.Page none = idx.Query(TestImage, null, null, null, null, 0, 100, sinceUnixMs: future);
        Assert.Equal(0, none.Total);

        long past = DateTimeOffset.UtcNow.AddMinutes(-5).ToUnixTimeMilliseconds();
        SqliteIndex.Page all = idx.Query(TestImage, null, null, null, null, 0, 100, sinceUnixMs: past);
        Assert.Equal(1, all.Total);
    }
}
