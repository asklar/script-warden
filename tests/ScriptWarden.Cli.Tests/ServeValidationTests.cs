using ScriptWarden.Web;

namespace ScriptWarden.Cli.Tests;

public class ServeValidationTests
{
    private static readonly string ValidSha = new('a', 64);

    [Theory]
    [InlineData(true, ".ps1")]
    [InlineData(true, ".cmd")]
    [InlineData(true, ".txt")]
    public void IsValidScriptReference_AcceptsGoodInput(bool _, string ext)
    {
        Assert.True(ServeCommand.IsValidScriptReference(ValidSha, ext));
    }

    [Theory]
    [InlineData("", ".ps1")]                                   // empty sha
    [InlineData("abc", ".ps1")]                                // too short
    [InlineData("g0000000000000000000000000000000000000000000000000000000000000000", ".ps1")] // non-hex
    [InlineData("../../secret", ".ps1")]                       // traversal in sha
    public void IsValidScriptReference_RejectsBadSha(string sha, string ext)
    {
        Assert.False(ServeCommand.IsValidScriptReference(sha, ext));
    }

    [Theory]
    [InlineData("ps1")]          // no leading dot
    [InlineData(".ps1/..")]      // traversal
    [InlineData(".ps 1")]        // space
    [InlineData(".waytoolongextension")] // > 10 chars
    [InlineData(".")]            // dot only
    public void IsValidScriptReference_RejectsBadExtension(string ext)
    {
        Assert.False(ServeCommand.IsValidScriptReference(ValidSha, ext));
    }

    [Theory]
    [InlineData(@"C:\a\scripts", @"C:\a\scripts\abc.ps1", true)]
    [InlineData(@"C:\a\scripts", @"C:\a\other\abc.ps1", false)]
    [InlineData(@"C:\a\scripts", @"C:\a\scripts", true)]
    public void IsInside_GuardsDirectory(string dir, string candidate, bool expected)
    {
        Assert.Equal(expected, ServeCommand.IsInside(dir, candidate));
    }
}
