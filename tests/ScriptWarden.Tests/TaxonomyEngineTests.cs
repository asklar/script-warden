using ScriptWarden.Core;
using ScriptWarden.Core.Analysis;

namespace ScriptWarden.Tests;

public class TaxonomyEngineTests
{
    private static Taxonomy Source() => new()
    {
        Id = "source", Name = "Source", MultiLabel = true,
        Rules =
        [
            new Rule { Label = "ConfigMgr (SCCM)", Any = [
                new Predicate { Field = "ancestorName", Op = "equals", Value = "ccmexec.exe" },
                new Predicate { Field = "ancestorPath", Op = "contains", Value = "\\CCM\\" } ] },
            new Rule { Label = "Intune / MDM", Any = [
                new Predicate { Field = "ancestorName", Op = "in", Values = ["IntuneManagementExtension.exe", "Microsoft.Management.Services.IntuneWindowsAgent.exe"] } ] },
            new Rule { Label = "WMI (remote mgmt)", Any = [
                new Predicate { Field = "ancestorName", Op = "equals", Value = "wmiprvse.exe" } ] },
            new Rule { Label = "SYSTEM", Any = [
                new Predicate { Field = "userSid", Op = "equals", Value = "S-1-5-18" } ] },
            new Rule { Label = "Unknown", Default = true },
        ],
    };

    private static EventFacts Facts(string[] ancestorNames, string? sid = null, string? cmd = null,
        string[]? urls = null, string[]? scripts = null, string[]? ancestorPaths = null)
        => new()
        {
            AncestorNames = ancestorNames,
            AncestorPaths = ancestorPaths ?? [],
            UserSid = sid,
            CommandLine = cmd,
            Urls = urls ?? [],
            ScriptContents = scripts ?? [],
        };

    [Fact]
    public void AncestorName_Equals_Matches()
    {
        List<string> labels = TaxonomyEngine.Classify(Source(), Facts(["pwsh.exe", "wmiprvse.exe", "svchost.exe"]));
        Assert.Contains("WMI (remote mgmt)", labels);
    }

    [Fact]
    public void AncestorPath_Contains_Matches()
    {
        List<string> labels = TaxonomyEngine.Classify(Source(),
            Facts(["cmd.exe"], ancestorPaths: [@"C:\Windows\CCM\ccmexec.exe"]));
        Assert.Contains("ConfigMgr (SCCM)", labels);
    }

    [Fact]
    public void In_Operator_Matches()
    {
        List<string> labels = TaxonomyEngine.Classify(Source(), Facts(["IntuneManagementExtension.exe"]));
        Assert.Contains("Intune / MDM", labels);
    }

    [Fact]
    public void MultiLabel_CollectsAllMatches()
    {
        List<string> labels = TaxonomyEngine.Classify(Source(), Facts(["wmiprvse.exe"], sid: "S-1-5-18"));
        Assert.Contains("WMI (remote mgmt)", labels);
        Assert.Contains("SYSTEM", labels);
    }

    [Fact]
    public void Default_AppliesWhenNothingMatched()
    {
        List<string> labels = TaxonomyEngine.Classify(Source(), Facts(["explorer.exe"]));
        Assert.Equal(["Unknown"], labels);
    }

    [Fact]
    public void Default_NotAppliedWhenSomethingMatched()
    {
        List<string> labels = TaxonomyEngine.Classify(Source(), Facts(["wmiprvse.exe"]));
        Assert.DoesNotContain("Unknown", labels);
    }

    [Fact]
    public void SingleLabel_ReturnsFirstMatchOnly()
    {
        Taxonomy t = Source();
        t.MultiLabel = false;
        List<string> labels = TaxonomyEngine.Classify(t, Facts(["wmiprvse.exe"], sid: "S-1-5-18"));
        Assert.Single(labels);
        Assert.Equal("WMI (remote mgmt)", labels[0]);
    }

    [Fact]
    public void Regex_OverCommandLine_Matches()
    {
        var t = new Taxonomy { Id = "behavior", MultiLabel = true, Rules =
        [
            new Rule { Label = "Install", Any = [ new Predicate { Field = "commandLine", Op = "regex", Value = @"Add-AppxPackage|msiexec\s+/i" } ] },
        ] };
        Assert.Contains("Install", TaxonomyEngine.Classify(t, Facts([], cmd: "powershell -Command \"Add-AppxPackage -Path x.appx\"")));
        Assert.Empty(TaxonomyEngine.Classify(t, Facts([], cmd: "powershell -Command \"Get-Date\"")));
    }

    [Fact]
    public void ScriptContent_Contains_Matches()
    {
        var t = new Taxonomy { Id = "mentions", MultiLabel = true, Rules =
        [
            new Rule { Label = "Touches Outlook", Any = [ new Predicate { Field = "scriptContent", Op = "contains", Value = "outlook.exe" } ] },
        ] };
        Assert.Contains("Touches Outlook", TaxonomyEngine.Classify(t, Facts([], scripts: ["Stop-Process -Name OUTLOOK.EXE -Force"])));
        Assert.Empty(TaxonomyEngine.Classify(t, Facts([], scripts: ["Write-Host hello"])));
    }

    [Fact]
    public void HasUrl_IsTrue_Matches()
    {
        var t = new Taxonomy { Id = "net", MultiLabel = true, Rules =
        [
            new Rule { Label = "Network", Any = [ new Predicate { Field = "hasUrl", Op = "isTrue" } ] },
        ] };
        Assert.Contains("Network", TaxonomyEngine.Classify(t, Facts([], urls: ["https://x.test/a.ps1"])));
        Assert.Empty(TaxonomyEngine.Classify(t, Facts([])));
    }

    [Fact]
    public void UnknownFieldOrOp_NeverMatches()
    {
        var t = new Taxonomy { Id = "x", MultiLabel = true, Rules =
        [
            new Rule { Label = "Bogus", Any = [ new Predicate { Field = "notAField", Op = "equals", Value = "x" } ] },
            new Rule { Label = "BadOp", Any = [ new Predicate { Field = "commandLine", Op = "frobnicate", Value = "x" } ] },
        ] };
        Assert.Empty(TaxonomyEngine.Classify(t, Facts([], cmd: "x")));
    }

    [Fact]
    public void From_FoldsImmediateParent_WhenAncestorChainEmpty()
    {
        // Events recorded before ancestor-chain capture have ParentProcessName but no Ancestors.
        var ev = new AuditEvent { ParentProcessName = "copilot.exe", Ancestors = [] };
        EventFacts facts = EventFacts.From(ev);
        Assert.Contains("copilot.exe", facts.AncestorNames);

        var tax = new Taxonomy
        {
            Id = "source", MultiLabel = true,
            Rules =
            [
                new Rule { Label = "Dev tools (mine)", Any = [new Predicate { Field = "ancestorName", Op = "in", Values = ["copilot.exe"] }] },
                new Rule { Label = "Unknown", Default = true },
            ],
        };
        Assert.Equal(["Dev tools (mine)"], TaxonomyEngine.Classify(tax, facts));
    }

    private static Taxonomy DefaultBehavior() =>
        DefaultTaxonomies.All().First(t => t.Id == "behavior");

    private static Taxonomy DefaultSource() =>
        DefaultTaxonomies.All().First(t => t.Id == "source");

    [Theory]
    [InlineData("cargo.exe")]
    [InlineData("go.exe")]
    [InlineData("cmake.exe")]
    [InlineData("ninja.exe")]
    [InlineData("cl.exe")]
    [InlineData("clang.exe")]
    [InlineData("nmake.exe")]
    [InlineData("pnpm.exe")]
    [InlineData("vcpkg.exe")]
    [InlineData("rider64.exe")]
    [InlineData("dotnet.exe")]
    [InlineData("devenv.exe")]
    public void Source_AttributesCommonDevToolsAsDevTools(string ancestor)
    {
        Assert.Contains("Dev tools (mine)", TaxonomyEngine.Classify(DefaultSource(), Facts([ancestor])));
    }

    [Theory]
    // Ambiguous runtimes were intentionally left out; management agents stay their own source.
    [InlineData("python.exe")]
    [InlineData("java.exe")]
    public void Source_DoesNotAttributeAmbiguousRuntimesAsDevTools(string ancestor)
    {
        Assert.DoesNotContain("Dev tools (mine)", TaxonomyEngine.Classify(DefaultSource(), Facts([ancestor])));
    }

    // Obfuscation is now command-line-argument aware: it only fires when a real encoded-command flag
    // is a discrete argument, never when the same text appears inside a quoted string / comment / data.
    private static EventFacts CmdFacts(string commandLine, string[]? scripts = null) =>
        EventFacts.From(new AuditEvent { CommandLine = commandLine }, scripts);

    [Theory]
    // The flag text appears only inside a quoted string arg, a hyphenated path, or as -Encoding — none count.
    [InlineData("pwsh.exe -NoProfile -Command \"Remove-Item (Join-Path $env:TEMP 'sw-enc-test') -Recurse\"")]
    [InlineData("pwsh.exe -Command \"it requires a real -EncodedCommand flag or FromBase64String\"")]
    [InlineData("powershell.exe -Command \"$d = [Convert]::FromBase64String($b)\"")]
    [InlineData("pwsh.exe -Command \"Get-Content x -Encoding UTF8\"")]
    [InlineData("powershell.exe -ExecutionPolicy Bypass -File c:\\x.ps1")]
    public void Obfuscation_DoesNotFlagWhenFlagIsInsideAStringOrWord(string cmd)
    {
        Assert.DoesNotContain("Obfuscation", TaxonomyEngine.Classify(DefaultBehavior(), CmdFacts(cmd)));
    }

    [Fact]
    public void Obfuscation_DoesNotFlagFromBase64StringInCapturedContent()
    {
        // Captured script content that merely mentions the API (e.g. our own PR prose) must not flag.
        var facts = CmdFacts("pwsh.exe -Command \"echo hi\"", scripts: ["needs a real -EncodedCommand flag, or FromBase64String."]);
        Assert.DoesNotContain("Obfuscation", TaxonomyEngine.Classify(DefaultBehavior(), facts));
    }

    [Theory]
    // A real encoded-command invocation: the flag is its own argument.
    [InlineData("powershell.exe -EncodedCommand ZQBjAGgAbwAgAGgAaQAgAHcAbwByAGwAZAA=")]
    [InlineData("pwsh.exe -NoProfile -enc ZQBjAGgAbwAgAGgAaQA=")]
    [InlineData("powershell -e ZQBjAGgAbwA=")]
    public void Obfuscation_FlagsRealEncodedCommandArgument(string cmd)
    {
        Assert.Contains("Obfuscation", TaxonomyEngine.Classify(DefaultBehavior(), CmdFacts(cmd)));
    }
}
