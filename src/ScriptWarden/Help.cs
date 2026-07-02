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
            script-warden — audit scripts run through Windows interpreters via IFEO.

            USAGE:
              script-warden <command> [options]

            COMMANDS:
              install            Register script-warden as the IFEO debugger for the interpreters
                                 (elevates automatically; machine-wide).
                --images a,b     Comma-separated images to hook (default: powershell.exe, cmd.exe,
                                 pwsh.exe, cscript.exe, wscript.exe).
              uninstall          Remove script-warden's IFEO hooks (elevates automatically).
                --images a,b     Limit to specific images (default: all we installed).
              status             Show which interpreters are currently hooked.
              serve              Start the local audit web viewer (localhost) and open a browser.
                --port N         Port to listen on (default: 8787).
                --no-open        Do not open a browser automatically.
              list               Print recent audit events to the console.
                --json           Emit JSON.
                --image NAME     Filter by hooked image.
                --since DATE     Only events at/after this UTC time (e.g. 2026-01-01).
              diagnose           Self-test: resolved data roots, write access, live IFEO values,
                                 and a simulated capture.
              version            Print the version.
              help               Show this help.

            Data is stored per-user under %LOCALAPPDATA%\script-warden (override with
            SCRIPT_WARDEN_DATA). The viewer also reads the SYSTEM account's root when accessible.
            """);
    }
}
