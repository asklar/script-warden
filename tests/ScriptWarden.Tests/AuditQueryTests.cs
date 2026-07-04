using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public class AuditQueryTests
{
    private static List<AuditEvent> Sample()
    {
        var baseTime = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var list = new List<AuditEvent>();
        for (int i = 0; i < 10; i++)
        {
            list.Add(new AuditEvent
            {
                EventId = "e" + i,
                TimestampUtc = baseTime.AddMinutes(i),
                HookedImage = i % 2 == 0 ? "powershell.exe" : "cmd.exe",
                CommandLine = $"cmd number {i}",
                Origin = i < 5 ? AuditOrigin.CurrentUser : AuditOrigin.System,
                ParentProcessName = i % 3 == 0 ? "explorer.exe" : "copilot.exe",
                Window = i % 2 == 0 ? WindowVisibility.Windowed : WindowVisibility.NoWindow,
            });
        }
        return list;
    }

    [Fact]
    public void Query_SortsNewestFirst_AndPages()
    {
        var page = AuditQuery.Query(Sample(), offset: 0, limit: 3);
        Assert.Equal(10, page.Total);
        Assert.Equal(3, page.Events.Count);
        // Newest (e9) first.
        Assert.Equal("e9", page.Events[0].EventId);
        Assert.Equal("e8", page.Events[1].EventId);
        Assert.Equal("e7", page.Events[2].EventId);
    }

    [Fact]
    public void Query_SecondPage_ContinuesInOrder()
    {
        var page = AuditQuery.Query(Sample(), offset: 3, limit: 3);
        Assert.Equal(10, page.Total);
        Assert.Equal(["e6", "e5", "e4"], page.Events.Select(e => e.EventId));
    }

    [Fact]
    public void Query_OffsetBeyondTotal_ReturnsEmptyPageButRealTotal()
    {
        var page = AuditQuery.Query(Sample(), offset: 100, limit: 10);
        Assert.Equal(10, page.Total);
        Assert.Empty(page.Events);
    }

    [Fact]
    public void Query_FilterByImage()
    {
        var page = AuditQuery.Query(Sample(), image: "cmd.exe", limit: 100);
        Assert.Equal(5, page.Total);
        Assert.All(page.Events, e => Assert.Equal("cmd.exe", e.HookedImage));
    }

    [Fact]
    public void Query_FilterByOriginParentWindow()
    {
        Assert.Equal(5, AuditQuery.Query(Sample(), origin: "System", limit: 100).Total);
        Assert.Equal(6, AuditQuery.Query(Sample(), parent: "copilot.exe", limit: 100).Total);
        Assert.Equal(5, AuditQuery.Query(Sample(), window: "NoWindow", limit: 100).Total);
    }

    [Fact]
    public void Query_ParentFilter_AcceptsCommaSeparatedIncludeSet()
    {
        // 6 copilot.exe + 4 explorer.exe = 10 total.
        Assert.Equal(10, AuditQuery.Query(Sample(), parent: "copilot.exe,explorer.exe", limit: 100).Total);
        // Whitespace around entries is tolerated; unknown entries are simply ignored.
        Assert.Equal(4, AuditQuery.Query(Sample(), parent: " explorer.exe , notreal.exe ", limit: 100).Total);
        // Empty selection means no filter.
        Assert.Equal(10, AuditQuery.Query(Sample(), parent: "", limit: 100).Total);
    }

    [Fact]
    public void Query_AllSentinel_IsNoFilter()
    {
        Assert.Equal(10, AuditQuery.Query(Sample(), image: "all", origin: "all", limit: 100).Total);
    }

    [Fact]
    public void Query_Search_MatchesCommandLine()
    {
        var page = AuditQuery.Query(Sample(), search: "number 7", limit: 100);
        var e = Assert.Single(page.Events);
        Assert.Equal("e7", e.EventId);
    }

    [Fact]
    public void Facets_ReturnsDistinctSortedValues()
    {
        AuditFacets f = AuditQuery.Facets(Sample());
        Assert.Equal(["cmd.exe", "powershell.exe"], f.Images);
        Assert.Equal(["copilot.exe", "explorer.exe"], f.Parents);
        Assert.Contains("Windowed", f.Windows);
        Assert.Contains("NoWindow", f.Windows);
    }
}
