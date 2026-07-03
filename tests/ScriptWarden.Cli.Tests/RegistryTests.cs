using Microsoft.Win32;
using ScriptWarden;

namespace ScriptWarden.Cli.Tests;

public class RegistryTests
{
    [Fact]
    public void GetStatus_UnhookedImage_ReturnsNotHooked()
    {
        // A random image name that no one has an IFEO Debugger for.
        string image = $"sw-unhooked-{Guid.NewGuid():N}.exe";
        HookInfo info = IfeoRegistry.GetStatus(image, RegistryView.Registry64);
        Assert.Equal(HookState.NotHooked, info.State);
        Assert.Null(info.Value);
    }

    [Fact]
    public void DebuggerValueFor_ProducesQuotedShimValue()
    {
        string value = IfeoRegistry.DebuggerValueFor(@"C:\Program Files\script-warden\script-warden.exe");
        Assert.Equal("\"C:\\Program Files\\script-warden\\script-warden.exe\" shim", value);
        Assert.True(IfeoRegistry.IsOurs(value));
    }

    [Theory]
    [InlineData("\"C:\\tools\\script-warden.exe\" shim", true)]
    [InlineData("C:\\WINDOWS\\system32\\vsjitdebugger.exe", false)]
    [InlineData("\"C:\\Debuggers\\windbg.exe\" -p %ld -e %ld -g", false)]
    public void IsOurs_DetectsOwnership(string debuggerValue, bool expected)
    {
        Assert.Equal(expected, IfeoRegistry.IsOurs(debuggerValue));
    }
}
