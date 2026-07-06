namespace ScriptWarden.Core.Analysis;

/// <summary>
/// The taxonomies shipped out of the box. Written to <c>&lt;root&gt;\taxonomies\</c> on first run so they
/// are fully user-editable; users can also drop in their own. Rules use only the engine's field/op
/// vocabulary, so these are just data — nothing here is special.
/// </summary>
public static class DefaultTaxonomies
{
    public static IEnumerable<Taxonomy> All()
    {
        yield return Source();
        yield return Behavior();
        yield return Visibility();
        yield return Interpreter();
        yield return Origin();
        yield return Outcome();
    }

    private static Taxonomy Source() => new()
    {
        Id = "source",
        Name = "Source",
        MultiLabel = true,
        Rules =
        [
            Rule("ConfigMgr (SCCM)", Eq("ancestorName", "ccmexec.exe"), Contains("ancestorPath", "\\CCM\\")),
            Rule("Intune / MDM", In("ancestorName", "IntuneManagementExtension.exe", "Microsoft.Management.Services.IntuneWindowsAgent.exe", "deviceenroller.exe", "omadmclient.exe")),
            Rule("WMI (remote mgmt)", Eq("ancestorName", "wmiprvse.exe")),
            Rule("Group Policy", Eq("ancestorName", "gpscript.exe")),
            Rule("Scheduled task", In("ancestorName", "taskhostw.exe", "taskeng.exe")),
            Rule("Windows servicing", In("ancestorName", "TrustedInstaller.exe", "TiWorker.exe", "usoclient.exe", "wuauclt.exe")),
            Rule("Logon / session init", In("ancestorName", "userinit.exe", "winlogon.exe")),
            Rule("EDR / AV", In("ancestorName", "MsMpEng.exe", "SenseIR.exe", "CSFalconService.exe")),
            Rule("Dev tools (mine)", In("ancestorName", "Code.exe", "devenv.exe", "copilot.exe", "git.exe", "node.exe", "dotnet.exe", "MSBuild.exe")),
            Rule("Interactive (me)", In("ancestorName", "explorer.exe", "WindowsTerminal.exe")),
            Rule("SYSTEM", Eq("userSid", "S-1-5-18")),
            Default("Unknown"),
        ],
    };

    private static Taxonomy Behavior() => new()
    {
        Id = "behavior",
        Name = "Behavior",
        MultiLabel = true,
        Rules =
        [
            Behavior("Install", @"Add-AppxPackage|msiexec\s+/i|Install-(Module|Package)|winget install|choco install|dism .*/add-package|\.msi\b|setup\.exe"),
            Behavior("Uninstall", @"Remove-AppxPackage|msiexec\s+/x|Uninstall-\w+|winget uninstall|dism .*/remove-package"),
            Behavior("Update / patch", @"\bwusa\b|usoclient|Install-WindowsUpdate|\.msu\b"),
            Behavior("Inventory / monitor", @"Get-(CimInstance|WmiObject)|Win32_\w+|Get-Hotfix|Get-ComputerInfo|\bsysteminfo\b"),
            Behavior("Config change", @"Set-ItemProperty|reg\s+add|New-ItemProperty|Set-Service|bcdedit|netsh|Set-\w*Policy"),
            Behavior("Persistence", @"schtasks\s+/create|New-ScheduledTask|CurrentVersion\\Run|New-Service"),
            new Rule
            {
                Label = "Network / download",
                Any =
                [
                    Rx("commandLine", @"Invoke-WebRequest|Invoke-RestMethod|\birm\b|Net\.WebClient|DownloadString|DownloadFile|bitsadmin|Start-BitsTransfer|\bcurl\b"),
                    Rx("scriptContent", @"Invoke-WebRequest|Invoke-RestMethod|\birm\b|Net\.WebClient|DownloadString|DownloadFile|bitsadmin|Start-BitsTransfer|\bcurl\b"),
                    new Predicate { Field = "hasUrl", Op = "isTrue" },
                ],
            },
            Behavior("Remote execution", @"Invoke-Command|Enter-PSSession|wmic .* process call create|psexec"),
            Behavior("Credential / security", @"Get-Credential|\bcmdkey\b|\bcertutil\b|Export-\w*Certificate|\bsecedit\b"),
            // Obfuscation = the interpreter was launched with an encoded-command flag. Match it as a
            // discrete command-line *argument* (-e / -enc / -EncodedCommand), so the token only counts
            // when it's a real flag — not when the same text appears inside a quoted string, a comment,
            // or data being piped to another program (which is what regex-over-raw-text used to flag).
            new Rule
            {
                Label = "Obfuscation",
                Any = [Rx("commandLineArgs", @"^-e(?:nc(?:odedcommand)?)?$")],
            },
        ],
    };

    private static Taxonomy Visibility() => new()
    {
        Id = "visibility",
        Name = "Visibility",
        MultiLabel = false,
        Rules =
        [
            Rule("Visible", Eq("window", "Windowed")),
            Rule("Hidden", Eq("window", "NoWindow")),
            Default("Unknown"),
        ],
    };

    private static Taxonomy Interpreter() => new()
    {
        Id = "interpreter",
        Name = "Interpreter",
        MultiLabel = false,
        Rules =
        [
            Rule("Windows PowerShell", Eq("hookedImage", "powershell.exe")),
            Rule("PowerShell 7", Eq("hookedImage", "pwsh.exe")),
            Rule("Command Prompt", Eq("hookedImage", "cmd.exe")),
            Rule("Console script host", Eq("hookedImage", "cscript.exe")),
            Rule("Windows script host", Eq("hookedImage", "wscript.exe")),
            Default("Other"),
        ],
    };

    private static Taxonomy Origin() => new()
    {
        Id = "origin",
        Name = "Ran as",
        MultiLabel = false,
        Rules =
        [
            Rule("Me (current user)", Eq("origin", "CurrentUser")),
            Rule("SYSTEM", Eq("origin", "System")),
            Default("Unknown"),
        ],
    };

    private static Taxonomy Outcome() => new()
    {
        Id = "outcome",
        Name = "Outcome",
        MultiLabel = false,
        Rules =
        [
            Rule("Succeeded", Eq("exitCode", "0")),
            Rule("Failed", Rx("exitCode", @"^(?!0$).+")),
            Default("Running / unknown"),
        ],
    };

    private static Rule Rule(string label, params Predicate[] any) => new() { Label = label, Any = [.. any] };
    private static Rule Default(string label) => new() { Label = label, Default = true };
    private static Rule Behavior(string label, string pattern) => new()
    {
        Label = label,
        Any = [Rx("commandLine", pattern), Rx("scriptContent", pattern)],
    };

    private static Predicate Eq(string field, string value) => new() { Field = field, Op = "equals", Value = value };
    private static Predicate Contains(string field, string value) => new() { Field = field, Op = "contains", Value = value };
    private static Predicate In(string field, params string[] values) => new() { Field = field, Op = "in", Values = [.. values] };
    private static Predicate Rx(string field, string pattern) => new() { Field = field, Op = "regex", Value = pattern };
}
