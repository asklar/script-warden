using ScriptWarden;

namespace ScriptWarden.Cli.Tests;

public class ImageCatalogTests
{
    [Theory]
    [InlineData("powershell", "powershell.exe")]
    [InlineData("CMD.EXE", "cmd.exe")]
    [InlineData(" pwsh.exe ", "pwsh.exe")]
    public void Normalize_AddsExeAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, ImageCatalog.Normalize(input));
    }

    [Theory]
    [InlineData("powershell.exe", true)]
    [InlineData("cscript", true)]
    [InlineData("notepad.exe", false)]
    [InlineData("evil", false)]
    public void IsKnown_MatchesCatalog(string image, bool expected)
    {
        Assert.Equal(expected, ImageCatalog.IsKnown(image));
    }

    [Fact]
    public void Default_IsSubsetOfKnown()
    {
        Assert.All(ImageCatalog.Default, d => Assert.Contains(d, ImageCatalog.Known));
    }
}

public class CliOptionsTests
{
    private static readonly HashSet<string> ValueKeys = new(StringComparer.OrdinalIgnoreCase) { "images", "exe-path", "port" };

    [Fact]
    public void Parse_ValueAndFlag()
    {
        CliOptions o = CliOptions.Parse(["install", "--images", "a,b", "--no-copy"], 1, ValueKeys);
        Assert.Equal("a,b", o.Get("images"));
        Assert.True(o.Has("no-copy"));
        Assert.Null(o.Get("exe-path"));
        Assert.False(o.Has("exe-path"));
    }

    [Fact]
    public void Parse_ValueKeyWithoutValue_TreatedAsFlag()
    {
        CliOptions o = CliOptions.Parse(["x", "--images"], 1, ValueKeys);
        Assert.True(o.Has("images"));
        Assert.Null(o.Get("images"));
    }

    [Fact]
    public void Parse_IgnoresNonSwitchTokens()
    {
        CliOptions o = CliOptions.Parse(["serve", "--port", "9000", "leftover"], 1, ValueKeys);
        Assert.Equal("9000", o.Get("port"));
        Assert.False(o.Has("leftover"));
    }
}
