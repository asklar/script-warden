using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public class CommandLineParserTests
{
    [Theory]
    [InlineData("a b c", new[] { "a", "b", "c" })]
    [InlineData("  a   b\tc ", new[] { "a", "b", "c" })]
    [InlineData("\"a b\" c", new[] { "a b", "c" })]
    [InlineData("\"\"", new[] { "" })]
    [InlineData("", new string[0])]
    [InlineData("   \t  ", new string[0])]
    // Microsoft CommandLineToArgvW documented examples:
    [InlineData("a\\\\b d\"e f\"g h", new[] { "a\\\\b", "de fg", "h" })]
    [InlineData("a\\\\\\\"b c d", new[] { "a\\\"b", "c", "d" })]
    [InlineData("a\\\\\\\\\"b c\" d e", new[] { "a\\\\b c", "d", "e" })]
    public void Tokenize_MatchesWindowsRules(string input, string[] expected)
    {
        Assert.Equal(expected, CommandLineParser.Tokenize(input));
    }

    [Fact]
    public void StripLeadingTokens_ZeroReturnsWhole()
    {
        Assert.Equal("one two three", CommandLineParser.StripLeadingTokens("one two three", 0));
    }

    [Fact]
    public void StripLeadingTokens_PreservesRemainderVerbatim()
    {
        string raw = "\"C:\\pf\\script-warden.exe\" shim \"C:\\win\\powershell.exe\" -File \"x y.ps1\"";
        string remainder = CommandLineParser.StripLeadingTokens(raw, 2);
        Assert.Equal("\"C:\\win\\powershell.exe\" -File \"x y.ps1\"", remainder);
    }

    [Fact]
    public void StripLeadingTokens_BeyondEnd_ReturnsEmpty()
    {
        Assert.Equal("", CommandLineParser.StripLeadingTokens("one two", 5));
    }
}
