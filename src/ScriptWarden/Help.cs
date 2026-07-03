using System.Reflection;

namespace ScriptWarden;

internal static class Help
{
    public static void PrintVersion()
    {
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0";
        Console.WriteLine($"script-warden {version}");
    }

    public static void Print()
    {
        Console.WriteLine(
            """
            script-warden — see and review the scripts that run on this machine.

            Records every script launched through Windows' script hosts (PowerShell, cmd,
            cscript, wscript) — including ones your IT department or automation runs in the
            background — keeps a copy of each script, and lets you browse the trail.

            USAGE:
              script-warden <command> [options]

            COMMANDS:
              install            Start monitoring the script hosts on this machine
                                 (elevates automatically; machine-wide).
                --images a,b     Comma-separated interpreters to monitor (default: powershell.exe,
                                 cmd.exe, pwsh.exe, cscript.exe, wscript.exe).
              uninstall          Stop monitoring (elevates automatically).
                --images a,b     Limit to specific interpreters (default: all we installed).
              status             Show which interpreters are currently monitored.
              serve              Open the audit trail in a local web viewer and launch a browser.
                --port N         Port to listen on (default: 8787).
                --no-open        Do not open a browser automatically.
              list               Print recent activity to the console.
                --json           Emit JSON.
                --image NAME     Filter by interpreter.
                --since DATE     Only events at/after this UTC time (e.g. 2026-01-01).
              clear              Delete all captured data (events + scripts).
                --yes            Skip the confirmation prompt.
              config             Show or edit configuration (exclusions).
                --enable         Resume recording.
                --disable        Pause recording (scripts still run, nothing is captured).
                --exclude-parent NAME   Skip launches started by NAME (e.g. copilot.exe).
                --remove-parent NAME    Remove a parent exclusion.
                --exclude-image NAME    Skip this interpreter entirely.
                --remove-image NAME     Remove an interpreter exclusion.
              diagnose           Self-test: resolved data roots, write access, monitoring state,
                                 and a simulated capture.
              version            Print the version.
              help               Show this help.

            Data is stored per-user under %LOCALAPPDATA%\script-warden (override with
            SCRIPT_WARDEN_DATA). The viewer also reads the SYSTEM account's data when accessible,
            so you can see scripts that ran with full privileges.
            """);
    }
}
