using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ScriptWarden.Interop;

/// <summary>A started child process (already detached from the debugger).</summary>
internal readonly record struct StartedProcess(IntPtr Handle, IntPtr Thread, int Pid)
{
    public bool IsValid => Handle != IntPtr.Zero;
}

/// <summary>
/// Relaunches the real interpreter transparently, forwarding stdio/cwd/environment and propagating
/// the exit code.
///
/// Recursion avoidance: the interpreter has an IFEO <c>Debugger</c> value pointing at us, so a normal
/// <c>CreateProcess</c> would re-invoke us forever. We create it with <c>DEBUG_ONLY_THIS_PROCESS</c>,
/// which the loader excludes from IFEO redirection (verified against ReactOS CreateProcessInternalW:
/// the IFEO Debugger read is gated behind
/// <c>!(dwCreationFlags &amp; (DEBUG_PROCESS | DEBUG_ONLY_THIS_PROCESS))</c>). We then immediately detach
/// so the child runs exactly as if it had never been debugged.
/// </summary>
internal static class TransparentLauncher
{
    /// <summary>
    /// Resolves an interpreter reference to a full path. Windows hands the shim whatever the caller
    /// used to launch the interpreter — often a bare name like <c>pwsh.exe</c> (resolved via PATH by
    /// the caller). <c>lpApplicationName</c> does NOT search PATH, so we resolve it here via
    /// <c>SearchPathW</c>. Returns null if it can't be resolved (the caller then falls back to letting
    /// CreateProcess resolve the image from the command line).
    /// </summary>
    public static string? ResolveImagePath(string target)
    {
        try
        {
            if (Path.IsPathRooted(target))
            {
                return File.Exists(target) ? target : null;
            }

            char[] buffer = new char[1024];
            uint len = NativeMethods.SearchPath(null, target, ".exe", (uint)buffer.Length, buffer, IntPtr.Zero);
            if (len > 0 && len < buffer.Length)
            {
                return new string(buffer, 0, (int)len);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Creates the child (bypassing IFEO), detaches, and returns without waiting.</summary>
    public static StartedProcess Start(string? applicationPath, string commandLine)
    {
        NativeMethods.STARTUPINFOW si = BuildStartupInfo();

        // Try with the (resolved) application path first. If that fails, fall back to letting
        // CreateProcess resolve the image from the command line (which does perform PATH search) —
        // this covers bare exe names handed to us by IFEO.
        bool created = TryCreate(applicationPath, commandLine, in si, out NativeMethods.PROCESS_INFORMATION pi, out int error);
        if (!created && applicationPath is not null)
        {
            created = TryCreate(null, commandLine, in si, out pi, out error);
        }

        if (!created)
        {
            throw new Win32Exception(error, $"CreateProcess failed for '{applicationPath ?? commandLine}'");
        }

        // Detach: the debug flag was only to bypass IFEO. Ensure the child survives our exit, then
        // stop debugging so it runs with completely normal semantics.
        NativeMethods.DebugSetProcessKillOnExit(false);
        NativeMethods.DebugActiveProcessStop(pi.dwProcessId);

        return new StartedProcess(pi.hProcess, pi.hThread, (int)pi.dwProcessId);
    }

    private static unsafe NativeMethods.STARTUPINFOW BuildStartupInfo()
    {
        var si = new NativeMethods.STARTUPINFOW
        {
            cb = (uint)sizeof(NativeMethods.STARTUPINFOW),
        };

        // Forward our std handles (console or redirected pipes) to the child. Skip when we have no
        // usable handles (e.g. a GUI host launched without a console) so we don't break it.
        IntPtr hIn = NativeMethods.GetStdHandle(NativeMethods.STD_INPUT_HANDLE);
        IntPtr hOut = NativeMethods.GetStdHandle(NativeMethods.STD_OUTPUT_HANDLE);
        IntPtr hErr = NativeMethods.GetStdHandle(NativeMethods.STD_ERROR_HANDLE);
        if (IsUsable(hIn) || IsUsable(hOut) || IsUsable(hErr))
        {
            si.dwFlags = NativeMethods.STARTF_USESTDHANDLES;
            si.hStdInput = hIn;
            si.hStdOutput = hOut;
            si.hStdError = hErr;
        }
        return si;
    }

    private static unsafe bool TryCreate(
        string? applicationPath,
        string commandLine,
        in NativeMethods.STARTUPINFOW si,
        out NativeMethods.PROCESS_INFORMATION pi,
        out int error)
    {
        // CreateProcessW may write to lpCommandLine, so pass a fresh mutable, null-terminated buffer
        // on each attempt.
        char[] buffer = new char[commandLine.Length + 1];
        commandLine.CopyTo(0, buffer, 0, commandLine.Length);
        buffer[commandLine.Length] = '\0';

        fixed (char* pCommandLine = buffer)
        {
            bool ok = NativeMethods.CreateProcessW(
                lpApplicationName: applicationPath,
                lpCommandLine: pCommandLine,
                lpProcessAttributes: IntPtr.Zero,
                lpThreadAttributes: IntPtr.Zero,
                bInheritHandles: true,
                dwCreationFlags: NativeMethods.DEBUG_ONLY_THIS_PROCESS,
                lpEnvironment: IntPtr.Zero,
                lpCurrentDirectory: null,
                lpStartupInfo: in si,
                lpProcessInformation: out pi);
            error = ok ? 0 : Marshal.GetLastPInvokeError();
            return ok;
        }
    }

    /// <summary>Waits for the child to exit and returns its exit code, closing handles.</summary>
    public static int WaitForExit(StartedProcess p)
    {
        NativeMethods.WaitForSingleObject(p.Handle, NativeMethods.INFINITE);
        NativeMethods.GetExitCodeProcess(p.Handle, out uint exitCode);
        NativeMethods.CloseHandle(p.Thread);
        NativeMethods.CloseHandle(p.Handle);
        return unchecked((int)exitCode);
    }

    private static bool IsUsable(IntPtr h) => h != IntPtr.Zero && h != new IntPtr(-1);
}
