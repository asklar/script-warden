using System.Text;
using ScriptWarden.Core;

namespace ScriptWarden.Tests;

public class ScriptExtractorTests
{
    [Fact]
    public void PowerShell_File_ReturnsFileReference()
    {
        var results = ScriptExtractor.Extract("powershell.exe",
            ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"C:\temp\deploy.ps1", "-Foo", "bar"],
            @"C:\work");

        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.FileReference, r.Kind);
        Assert.Equal(ScriptLanguage.PowerShell, r.Language);
        Assert.Equal(@"C:\temp\deploy.ps1", r.FilePath);
        Assert.Equal(".ps1", r.Extension);
    }

    [Fact]
    public void PowerShell_RelativeFile_ResolvedAgainstWorkingDir()
    {
        var results = ScriptExtractor.Extract("powershell.exe", ["-File", "sub\\a.ps1"], @"C:\work");
        var r = Assert.Single(results);
        Assert.Equal(@"C:\work\sub\a.ps1", r.FilePath);
    }

    [Fact]
    public void PowerShell_EncodedCommand_IsDecoded()
    {
        const string script = "Write-Host 'hello world'";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

        var results = ScriptExtractor.Extract("powershell.exe", ["-EncodedCommand", encoded], @"C:\work");
        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.EncodedCommand, r.Kind);
        Assert.Equal(script, Encoding.UTF8.GetString(r.InlineContent!));
        Assert.Equal(".ps1", r.Extension);
    }

    [Theory]
    [InlineData("-e")]
    [InlineData("-ec")]
    [InlineData("-enc")]
    [InlineData("-EncodedCommand")]
    public void PowerShell_EncodedCommand_PrefixForms(string flag)
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("1"));
        var results = ScriptExtractor.Extract("powershell.exe", [flag, encoded], @"C:\work");
        Assert.Equal(ScriptKind.EncodedCommand, Assert.Single(results).Kind);
    }

    [Fact]
    public void PowerShell_ExecutionPolicyShortFlag_NotTreatedAsEncoded()
    {
        // "-ex" is a prefix of ExecutionPolicy, not EncodedCommand, and must be skipped.
        var results = ScriptExtractor.Extract("powershell.exe", ["-ex", "Bypass", "-File", "a.ps1"], @"C:\w");
        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.FileReference, r.Kind);
    }

    [Fact]
    public void PowerShell_Command_CapturesInline()
    {
        var results = ScriptExtractor.Extract("powershell.exe", ["-Command", "Write-Host hi"], @"C:\w");
        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.InlineCommand, r.Kind);
        Assert.Equal("Write-Host hi", Encoding.UTF8.GetString(r.InlineContent!));
    }

    [Fact]
    public void PowerShell_PositionalScript_IsFileReference()
    {
        var results = ScriptExtractor.Extract("powershell.exe", [@"C:\temp\run.ps1"], @"C:\w");
        Assert.Equal(ScriptKind.FileReference, Assert.Single(results).Kind);
    }

    [Fact]
    public void Cmd_BatchFile_IsFileReference()
    {
        var results = ScriptExtractor.Extract("cmd.exe", ["/c", @"C:\it\logon.bat", "arg1"], @"C:\w");
        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.FileReference, r.Kind);
        Assert.Equal(ScriptLanguage.Batch, r.Language);
        Assert.Equal(@"C:\it\logon.bat", r.FilePath);
    }

    [Fact]
    public void Cmd_InlineCommand_IsCaptured()
    {
        var results = ScriptExtractor.Extract("cmd.exe", ["/c", "echo", "hello"], @"C:\w");
        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.InlineCommand, r.Kind);
        Assert.Equal("echo hello", Encoding.UTF8.GetString(r.InlineContent!));
    }

    [Fact]
    public void Cscript_FindsScriptAfterHostOptions()
    {
        var results = ScriptExtractor.Extract("cscript.exe", ["//nologo", "//B", @"C:\it\task.vbs", "x"], @"C:\w");
        var r = Assert.Single(results);
        Assert.Equal(ScriptKind.ScriptArgument, r.Kind);
        Assert.Equal(ScriptLanguage.VBScript, r.Language);
        Assert.Equal(@"C:\it\task.vbs", r.FilePath);
    }

    [Fact]
    public void Wscript_JScript()
    {
        var results = ScriptExtractor.Extract("wscript.exe", [@"C:\it\popup.js"], @"C:\w");
        Assert.Equal(ScriptLanguage.JScript, Assert.Single(results).Language);
    }

    [Fact]
    public void InteractiveCmd_NoScripts()
    {
        Assert.Empty(ScriptExtractor.Extract("cmd.exe", [], @"C:\w"));
    }

    [Fact]
    public void PowerShell_PositionalCallOperator_CapturesEmbeddedScriptFile()
    {
        // The real-world ConfigMgr pattern: no -File/-Command, a positional "& 'C:\...ps1'".
        var results = ScriptExtractor.Extract(
            "powershell.exe",
            ["-NoLogo", "-NonInteractive", "-NoProfile", "-ExecutionPolicy", "Bypass", @"& 'C:\Windows\CCM\SystemTemp\56c78008.ps1'"],
            @"C:\Windows\system32");

        Assert.Contains(results, r => r.Kind == ScriptKind.FileReference &&
            r.FilePath!.EndsWith(@"\Windows\CCM\SystemTemp\56c78008.ps1", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(results, r => r.Kind == ScriptKind.InlineCommand);
    }

    [Fact]
    public void PowerShell_CommandWithEmbeddedQuotedPath_CapturesBoth()
    {
        var results = ScriptExtractor.Extract("powershell.exe", ["-Command", @"& 'C:\it\a.ps1' -Verbose"], @"C:\w");
        Assert.Contains(results, r => r.Kind == ScriptKind.InlineCommand);
        Assert.Contains(results, r => r.Kind == ScriptKind.FileReference && r.FilePath == @"C:\it\a.ps1");
    }

    [Fact]
    public void PowerShell_ExecutionPolicyValue_NotMistakenForCommand()
    {
        var results = ScriptExtractor.Extract("powershell.exe", ["-ExecutionPolicy", "Bypass", "-File", @"C:\x.ps1"], @"C:\w");
        var fileRefs = results.Where(r => r.Kind == ScriptKind.FileReference).ToList();
        Assert.Single(fileRefs);
        Assert.Equal(@"C:\x.ps1", fileRefs[0].FilePath);
        Assert.DoesNotContain(results, r => r.Kind == ScriptKind.InlineCommand);
    }

    [Fact]
    public void Heuristic_UncScriptPath_IsCaptured()
    {
        var results = ScriptExtractor.Extract("powershell.exe", ["-Command", @"\\server\share\deploy.ps1"], @"C:\w");
        Assert.Contains(results, r => r.Kind == ScriptKind.FileReference && r.OriginalPath == @"\\server\share\deploy.ps1");
    }

    [Fact]
    public void EncodedCommand_WithEmbeddedPath_CapturesInnerScript()
    {
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(@"& 'C:\ProgramData\Corp\run.ps1'"));
        var results = ScriptExtractor.Extract("powershell.exe", ["-EncodedCommand", encoded], @"C:\w");
        Assert.Contains(results, r => r.Kind == ScriptKind.EncodedCommand);
        Assert.Contains(results, r => r.Kind == ScriptKind.FileReference && r.FilePath == @"C:\ProgramData\Corp\run.ps1");
    }
}
