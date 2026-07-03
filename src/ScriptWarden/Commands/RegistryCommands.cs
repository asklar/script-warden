using Microsoft.Win32;
using ScriptWarden.Core;

namespace ScriptWarden.Commands;

/// <summary>install / uninstall / status — manage the IFEO hooks (HKLM, elevated).</summary>
internal static class RegistryCommands
{
    private static readonly HashSet<string> ValueKeys =
        new(StringComparer.OrdinalIgnoreCase) { "images", "exe-path", "result" };

    public static int Install(string[] args)
    {
        CliOptions opts = CliOptions.Parse(args, 1, ValueKeys);
        string[] images = ResolveImages(opts.Get("images"), ImageCatalog.Default, out string? imageError);
        if (imageError is not null)
        {
            Console.Error.WriteLine(imageError);
            return 2;
        }

        string? resultFile = opts.Get("result");
        if (!Elevation.IsElevated() && resultFile is null)
        {
            return ElevateAndReport(args);
        }

        var lines = new List<string>();
        int code = 0;
        try
        {
            string installedExe = opts.Has("no-copy")
                ? Environment.ProcessPath ?? throw new InvalidOperationException("Cannot determine exe path.")
                : CopyExe(opts.Get("exe-path"), lines);

            string debuggerValue = IfeoRegistry.DebuggerValueFor(installedExe);
            foreach (string image in images)
            {
                foreach (RegistryView view in IfeoRegistry.Views)
                {
                    IfeoRegistry.SetDebugger(image, debuggerValue, view);
                }
                lines.Add($"  hooked  {image}");
            }

            lines.Insert(0, $"Registered IFEO debugger for {images.Length} image(s):");
            lines.Add("");
            lines.Add($"Debugger value : {debuggerValue}");
            lines.Add($"Audit data     : {DataRoots.CurrentUserRoot()} (per-user)");
            lines.Add("Run 'script-warden status' to verify, or 'script-warden serve' to view the audit trail.");
        }
        catch (Exception ex)
        {
            lines.Add($"ERROR: {ex.Message}");
            code = 1;
        }

        Emit(lines, resultFile);
        return code;
    }

    public static int Uninstall(string[] args)
    {
        CliOptions opts = CliOptions.Parse(args, 1, ValueKeys);
        string[] images = ResolveImages(opts.Get("images"), ImageCatalog.Known, out string? imageError);
        if (imageError is not null)
        {
            Console.Error.WriteLine(imageError);
            return 2;
        }

        string? resultFile = opts.Get("result");
        if (!Elevation.IsElevated() && resultFile is null)
        {
            return ElevateAndReport(args);
        }

        var lines = new List<string>();
        int code = 0;
        int removed = 0;
        try
        {
            foreach (string image in images)
            {
                bool any = false;
                foreach (RegistryView view in IfeoRegistry.Views)
                {
                    any |= IfeoRegistry.RemoveOurDebugger(image, view);
                }
                if (any)
                {
                    removed++;
                    lines.Add($"  removed  {image}");
                }
            }

            lines.Insert(0, removed > 0
                ? $"Removed script-warden IFEO hooks from {removed} image(s):"
                : "No script-warden IFEO hooks were found.");
            lines.Add("");
            lines.Add("Note: the installed executable and existing audit data were left in place.");
        }
        catch (Exception ex)
        {
            lines.Add($"ERROR: {ex.Message}");
            code = 1;
        }

        Emit(lines, resultFile);
        return code;
    }

    public static int Status(string[] args)
    {
        Console.WriteLine("IFEO hook status (image : 64-bit view / 32-bit view):");
        Console.WriteLine();

        var images = new List<string>(ImageCatalog.Known);
        foreach (string image in images)
        {
            HookInfo x64 = IfeoRegistry.GetStatus(image, RegistryView.Registry64);
            HookInfo x86 = IfeoRegistry.GetStatus(image, RegistryView.Registry32);
            Console.WriteLine($"  {image,-20} {Describe(x64.State),-16} {Describe(x86.State)}");

            if (x64.State == HookState.HookedByOther)
            {
                Console.WriteLine($"       (64-bit debugger: {x64.Value})");
            }
            if (x86.State == HookState.HookedByOther)
            {
                Console.WriteLine($"       (32-bit debugger: {x86.Value})");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"Audit data (per-user): {DataRoots.CurrentUserRoot()}");
        return 0;
    }

    private static string Describe(HookState state) => state switch
    {
        HookState.HookedByUs => "hooked (us)",
        HookState.HookedByOther => "other debugger",
        _ => "not hooked",
    };

    private static string CopyExe(string? exePathDir, List<string> lines)
    {
        string src = Environment.ProcessPath
                     ?? throw new InvalidOperationException("Cannot determine executable path.");
        string dir = exePathDir
                     ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "script-warden");
        Directory.CreateDirectory(dir);
        string dst = Path.Combine(dir, "script-warden.exe");

        if (!string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                File.Copy(src, dst, overwrite: true);
                lines.Add($"  copied exe -> {dst}");
            }
            catch (IOException)
            {
                lines.Add($"  NOTE: could not overwrite {dst} (in use?); keeping existing copy");
            }
        }
        else
        {
            lines.Add($"  using exe in place -> {dst}");
        }

        return dst;
    }

    private static int ElevateAndReport(string[] args)
    {
        string resultFile = Path.Combine(Path.GetTempPath(), $"sw-{Guid.NewGuid():N}.txt");
        Console.WriteLine("Requesting administrator privileges (this action modifies HKLM)...");

        var elevatedArgs = new List<string>(args) { "--result", resultFile };
        int code = Elevation.Relaunch(elevatedArgs);

        if (code == -1)
        {
            Console.Error.WriteLine("Elevation was declined; no changes were made.");
            return 1;
        }

        if (File.Exists(resultFile))
        {
            try
            {
                foreach (string line in File.ReadAllLines(resultFile))
                {
                    Console.WriteLine(line);
                }
            }
            finally
            {
                try { File.Delete(resultFile); } catch { /* ignore */ }
            }
        }

        return code;
    }

    private static void Emit(List<string> lines, string? resultFile)
    {
        foreach (string line in lines)
        {
            Console.WriteLine(line);
        }
        if (resultFile is not null)
        {
            try
            {
                File.WriteAllLines(resultFile, lines);
            }
            catch
            {
                // best-effort; parent will simply show nothing extra
            }
        }
    }

    private static string[] ResolveImages(string? csv, string[] fallback, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(csv))
        {
            return fallback;
        }

        var result = new List<string>();
        foreach (string raw in csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string image = ImageCatalog.Normalize(raw);
            if (!ImageCatalog.IsKnown(image))
            {
                error = $"script-warden: unknown image '{raw}'. Known: {string.Join(", ", ImageCatalog.Known)}";
                return [];
            }
            result.Add(image);
        }
        return result.ToArray();
    }
}
