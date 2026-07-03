using System.Text;
using Microsoft.Win32;
using ScriptWarden.Core;

namespace ScriptWarden.Commands;

/// <summary>
/// Verbose self-test: resolved data roots + write access, live IFEO values, and a simulated capture
/// of representative command lines. Helps validate an install end-to-end without running scripts.
/// </summary>
internal static class DiagnoseCommand
{
    public static int Run(string[] args)
    {
        Console.WriteLine("script-warden diagnose");
        Console.WriteLine(new string('=', 60));

        Help.PrintVersion();
        Console.WriteLine($"Elevated        : {Elevation.IsElevated()}");
        Console.WriteLine($"Executable      : {Environment.ProcessPath}");
        Console.WriteLine($"Command line    : {Environment.CommandLine}");
        Console.WriteLine($"Current dir     : {Environment.CurrentDirectory}");

        var identity = Interop.ProcessDetails.GetIdentity();
        Console.WriteLine($"Identity        : {identity.User} (session {identity.SessionId})");

        Console.WriteLine();
        Console.WriteLine("Data roots");
        Console.WriteLine(new string('-', 60));
        ReportRoot("current-user (writes)", DataRoots.CurrentUserRoot(), probeWrite: true);
        ReportRoot("system (read by viewer)", DataRoots.SystemRoot(), probeWrite: false);

        Console.WriteLine();
        Console.WriteLine("Monitoring (64-bit / 32-bit)");
        Console.WriteLine(new string('-', 60));
        foreach (string image in ImageCatalog.Known)
        {
            HookInfo x64 = IfeoRegistry.GetStatus(image, RegistryView.Registry64);
            HookInfo x86 = IfeoRegistry.GetStatus(image, RegistryView.Registry32);
            Console.WriteLine($"  {image,-20} {DescribeState(x64.State),-16} {DescribeState(x86.State)}");
        }

        Console.WriteLine();
        Console.WriteLine("Capture simulation (no scripts are executed, nothing is written)");
        Console.WriteLine(new string('-', 60));
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes("Write-Host 'hello from encoded'"));
        Simulate("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", @"C:\it\deploy.ps1"]);
        Simulate("powershell.exe", ["-EncodedCommand", encoded]);
        Simulate("powershell.exe", ["-Command", "Get-Process | Stop-Process"]);
        Simulate("cmd.exe", ["/c", @"C:\it\logon.bat", "/silent"]);
        Simulate("cmd.exe", ["/c", "echo", "hi", "&&", "shutdown", "/r"]);
        Simulate("cscript.exe", ["//nologo", @"C:\it\task.vbs"]);

        return 0;
    }

    private static string DescribeState(HookState state) => state switch
    {
        HookState.HookedByUs => "monitored",
        HookState.HookedByOther => "other tool",
        _ => "not monitored",
    };

    private static void ReportRoot(string label, string path, bool probeWrite)
    {
        Console.WriteLine($"  {label}");
        Console.WriteLine($"    path     : {path}");
        Console.WriteLine($"    exists   : {SafeExists(path)}");

        if (!probeWrite)
        {
            Console.WriteLine($"    readable : {SafeReadable(path)}");
            return;
        }

        string status;
        try
        {
            DataRoots.EnsureLayout(path);
            string probe = Path.Combine(DataRoots.EventsDir(path), $".probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            status = "OK";
        }
        catch (Exception ex)
        {
            status = $"DENIED ({ex.GetType().Name}: {ex.Message})";
        }
        Console.WriteLine($"    writable : {status}");
    }

    private static void Simulate(string image, string[] args)
    {
        Console.WriteLine($"  {image}  {string.Join(' ', args)}");
        List<ExtractionResult> results = ScriptExtractor.Extract(image, args, @"C:\work");
        if (results.Count == 0)
        {
            Console.WriteLine("      (no script captured)");
            return;
        }

        foreach (ExtractionResult r in results)
        {
            string what = r.FilePath ?? Preview(r.InlineContent);
            Console.WriteLine($"      -> {r.Kind} [{r.Language}] {what}");
            if (r.Note is not null)
            {
                Console.WriteLine($"         note: {r.Note}");
            }
        }
    }

    private static string Preview(byte[]? content)
    {
        if (content is null)
        {
            return "";
        }
        string text = Encoding.UTF8.GetString(content);
        text = text.ReplaceLineEndings(" ");
        return text.Length > 60 ? text[..60] + "…" : text;
    }

    private static string SafeExists(string path)
    {
        try
        {
            return Directory.Exists(path).ToString();
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static string SafeReadable(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return "n/a (absent)";
            }
            _ = Directory.EnumerateFileSystemEntries(path).Any();
            return "yes";
        }
        catch (Exception ex)
        {
            return $"no ({ex.GetType().Name})";
        }
    }
}
