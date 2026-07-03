using System.Diagnostics;
using System.Security.Principal;
using ScriptWarden.Core;

namespace ScriptWarden.Interop;

/// <summary>Best-effort details about the launching (parent) process and the current identity.</summary>
internal static class ProcessDetails
{
    public readonly record struct ParentInfo(int Pid, string? Name, string? Path);

    public readonly record struct Identity(string? User, string? Sid, int SessionId);

    public static ParentInfo GetParent()
    {
        try
        {
            int myPid = Environment.ProcessId;
            IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
            if (snapshot == new IntPtr(-1))
            {
                return default;
            }

            var map = new Dictionary<int, (int Parent, string Name)>();
            try
            {
                var entry = new NativeMethods.PROCESSENTRY32W();
                unsafe
                {
                    entry.dwSize = (uint)sizeof(NativeMethods.PROCESSENTRY32W);
                }

                if (NativeMethods.Process32FirstW(snapshot, ref entry))
                {
                    do
                    {
                        map[(int)entry.th32ProcessID] = ((int)entry.th32ParentProcessID, ReadExeName(ref entry));
                    }
                    while (NativeMethods.Process32NextW(snapshot, ref entry));
                }
            }
            finally
            {
                NativeMethods.CloseHandle(snapshot);
            }

            if (!map.TryGetValue(myPid, out var me))
            {
                return default;
            }

            string? name = map.TryGetValue(me.Parent, out var parent) ? parent.Name : null;
            string? path = me.Parent != 0 ? TryGetFullPath(me.Parent) : null;
            return new ParentInfo(me.Parent, name, path);
        }
        catch
        {
            return default;
        }
    }

    public static Identity GetIdentity()
    {
        string? user = null;
        string? sid = null;
        int session = 0;

        try
        {
            using WindowsIdentity id = WindowsIdentity.GetCurrent();
            user = id.Name;
            sid = id.User?.Value;
        }
        catch
        {
            // best-effort
        }

        try
        {
            session = Process.GetCurrentProcess().SessionId;
        }
        catch
        {
            // best-effort
        }

        return new Identity(user, sid, session);
    }

    /// <summary>
    /// Determines whether this process (launched by IFEO in place of the interpreter, inheriting the
    /// caller's creation flags/startup info) has a visible window, a hidden one, or no console at all.
    /// </summary>
    public static WindowVisibility GetWindowVisibility()
    {
        try
        {
            NativeMethods.GetStartupInfo(out NativeMethods.STARTUPINFOW si);
            if ((si.dwFlags & NativeMethods.STARTF_USESHOWWINDOW) != 0 && si.wShowWindow == NativeMethods.SW_HIDE)
            {
                return WindowVisibility.Hidden;
            }

            IntPtr consoleWindow = NativeMethods.GetConsoleWindow();
            if (consoleWindow == IntPtr.Zero)
            {
                return WindowVisibility.None;
            }

            return NativeMethods.IsWindowVisible(consoleWindow) ? WindowVisibility.Visible : WindowVisibility.Hidden;
        }
        catch
        {
            return WindowVisibility.Unknown;
        }
    }

    private static unsafe string ReadExeName(ref NativeMethods.PROCESSENTRY32W entry)
    {
        fixed (char* p = entry.szExeFile)
        {
            return new string(p);
        }
    }

    private static string? TryGetFullPath(int pid)
    {
        IntPtr h = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero)
        {
            return null;
        }
        try
        {
            char[] buffer = new char[1024];
            uint size = (uint)buffer.Length;
            if (NativeMethods.QueryFullProcessImageName(h, 0, buffer, ref size))
            {
                return new string(buffer, 0, (int)size);
            }
            return null;
        }
        finally
        {
            NativeMethods.CloseHandle(h);
        }
    }
}
