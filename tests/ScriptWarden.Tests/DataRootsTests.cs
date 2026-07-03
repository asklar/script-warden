using ScriptWarden.Core;

namespace ScriptWarden.Tests;

/// <summary>
/// Tests for root resolution. Kept in one class so the environment-variable override (process-global)
/// is exercised sequentially and never observed by concurrent tests.
/// </summary>
[Collection("DataRoots")]
public class DataRootsTests
{
    [Fact]
    public void CurrentUserRoot_HonorsEnvOverride()
    {
        string? previous = Environment.GetEnvironmentVariable(DataRoots.EnvOverride);
        string custom = Path.Combine(Path.GetTempPath(), "sw-custom-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable(DataRoots.EnvOverride, custom);
            Assert.Equal(custom, DataRoots.CurrentUserRoot());
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataRoots.EnvOverride, previous);
        }
    }

    [Fact]
    public void CurrentUserRoot_DefaultsUnderLocalAppData()
    {
        string? previous = Environment.GetEnvironmentVariable(DataRoots.EnvOverride);
        try
        {
            Environment.SetEnvironmentVariable(DataRoots.EnvOverride, null);
            string root = DataRoots.CurrentUserRoot();
            Assert.EndsWith(DataRoots.FolderName, root);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataRoots.EnvOverride, previous);
        }
    }

    [Fact]
    public void SystemRoot_PointsAtSystemProfile()
    {
        string root = DataRoots.SystemRoot();
        Assert.Contains("systemprofile", root, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(DataRoots.FolderName, root);
    }

    [Fact]
    public void EventsAndScriptsDirs_AreUnderRoot()
    {
        Assert.Equal(Path.Combine("root", "events"), DataRoots.EventsDir("root"));
        Assert.Equal(Path.Combine("root", "scripts"), DataRoots.ScriptsDir("root"));
    }

    [Fact]
    public void ForViewer_IncludesUserAndSystemOrigins()
    {
        string? previous = Environment.GetEnvironmentVariable(DataRoots.EnvOverride);
        try
        {
            // Ensure the user root differs from the system root so both are listed.
            Environment.SetEnvironmentVariable(DataRoots.EnvOverride,
                Path.Combine(Path.GetTempPath(), "sw-viewer-root-" + Guid.NewGuid().ToString("N")));

            var origins = DataRoots.ForViewer().Select(r => r.Origin).ToList();
            Assert.Contains(AuditOrigin.CurrentUser, origins);
            Assert.Contains(AuditOrigin.System, origins);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DataRoots.EnvOverride, previous);
        }
    }
}
