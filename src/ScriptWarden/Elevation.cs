using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;

namespace ScriptWarden;

/// <summary>Elevation detection and re-launch (UAC) for the admin-only commands.</summary>
internal static class Elevation
{
    public static bool IsElevated()
    {
        try
        {
            using WindowsIdentity id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Relaunches this executable elevated with the given arguments, waits, and returns its exit
    /// code. Returns -1 if the user declined the UAC prompt.
    /// </summary>
    public static int Relaunch(IEnumerable<string> args)
    {
        string exe = Environment.ProcessPath
                     ?? throw new InvalidOperationException("Cannot determine executable path for elevation.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
        };
        foreach (string a in args)
        {
            psi.ArgumentList.Add(a);
        }

        try
        {
            using Process? p = Process.Start(psi);
            if (p is null)
            {
                return 1;
            }
            p.WaitForExit();
            return p.ExitCode;
        }
        catch (Win32Exception)
        {
            // ERROR_CANCELLED etc. — user declined the prompt.
            return -1;
        }
    }
}
