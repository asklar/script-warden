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
    /// <summary>Creates the child (bypassing IFEO), detaches, and returns without waiting.</summary>
    public static unsafe StartedProcess Start(string applicationPath, string commandLine)
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

        // CreateProcessW may write to lpCommandLine, so pass a mutable, null-terminated buffer.
        char[] buffer = new char[commandLine.Length + 1];
        commandLine.CopyTo(0, buffer, 0, commandLine.Length);
        buffer[commandLine.Length] = '\0';

        NativeMethods.PROCESS_INFORMATION pi;
        bool created;
        fixed (char* pCommandLine = buffer)
        {
            created = NativeMethods.CreateProcessW(
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
        }

        if (!created)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"CreateProcess failed for '{applicationPath}'");
        }

        // Detach: the debug flag was only to bypass IFEO. Ensure the child survives our exit, then
        // stop debugging so it runs with completely normal semantics.
        NativeMethods.DebugSetProcessKillOnExit(false);
        NativeMethods.DebugActiveProcessStop(pi.dwProcessId);

        return new StartedProcess(pi.hProcess, pi.hThread, (int)pi.dwProcessId);
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
