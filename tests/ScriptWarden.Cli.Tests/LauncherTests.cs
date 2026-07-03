using ScriptWarden.Interop;

namespace ScriptWarden.Cli.Tests;

/// <summary>
/// Integration tests for the transparent relaunch mechanism. These launch real child processes
/// (cmd.exe) via the same CreateProcess(DEBUG_ONLY_THIS_PROCESS) + detach path used by the shim,
/// which directly guards exit-code propagation and the bare-name resolution fix.
/// </summary>
public class LauncherTests
{
    private static string CmdPath => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public void ResolveImagePath_BareName_ResolvesViaPath()
    {
        string? resolved = TransparentLauncher.ResolveImagePath("cmd.exe");
        Assert.NotNull(resolved);
        Assert.True(Path.IsPathRooted(resolved));
        Assert.True(File.Exists(resolved));
        Assert.EndsWith("cmd.exe", resolved, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveImagePath_FullExistingPath_RoundTrips()
    {
        string? resolved = TransparentLauncher.ResolveImagePath(CmdPath);
        Assert.Equal(CmdPath, resolved, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveImagePath_Nonexistent_ReturnsNull()
    {
        Assert.Null(TransparentLauncher.ResolveImagePath("sw-no-such-interpreter-xyz-987.exe"));
    }

    [Fact]
    public void Start_FullPath_PropagatesExitCode()
    {
        StartedProcess p = TransparentLauncher.Start(CmdPath, $"\"{CmdPath}\" /c exit 7");
        Assert.Equal(7, TransparentLauncher.WaitForExit(p));
    }

    [Fact]
    public void Start_ResolvedBareName_Runs()
    {
        // Simulates the IFEO case: caller launched by bare name; we resolve then launch, with the
        // original bare-name command line preserved as argv.
        string? resolved = TransparentLauncher.ResolveImagePath("cmd.exe");
        StartedProcess p = TransparentLauncher.Start(resolved, "cmd.exe /c exit 4");
        Assert.Equal(4, TransparentLauncher.WaitForExit(p));
    }

    [Fact]
    public void Start_NullApplicationName_ResolvesFromCommandLine()
    {
        // The fallback path: no application name -> CreateProcess resolves the image from the
        // command line (which does search PATH).
        StartedProcess p = TransparentLauncher.Start(null, "cmd.exe /c exit 5");
        Assert.Equal(5, TransparentLauncher.WaitForExit(p));
    }
}
