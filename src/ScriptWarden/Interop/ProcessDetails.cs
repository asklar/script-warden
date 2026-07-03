using System.Diagnostics;
using System.Security.Principal;
using ScriptWarden.Core;

namespace ScriptWarden.Interop;

/// <summary>Best-effort details about the launching (ancestor) processes and the current identity.</summary>
internal static class ProcessDetails
{
    public readonly record struct Identity(string? User, string? Sid, int SessionId);

    /// <summary>
    /// Walks the process ancestor chain (immediate parent first) using a single Toolhelp snapshot,
    /// resolving each ancestor's full path best-effort. Bounded by <paramref name="maxDepth"/> and
    /// guarded against cycles from PID reuse. May be partial for protected/exited processes.
    /// </summary>
    public static List<ProcessRef> GetAncestors(int maxDepth = 32)
    {
        var chain = new List<ProcessRef>();
        try
        {
            Dictionary<int, (int Parent, string Name)> map = BuildProcessMap();
            int pid = Environment.ProcessId;
            var seen = new HashSet<int> { pid };

            for (int i = 0; i < maxDepth; i++)
            {
                if (!map.TryGetValue(pid, out var cur))
                {
                    break;
                }
                int parent = cur.Parent;
                if (parent == 0 || !seen.Add(parent))
                {
                    break; // reached the root or detected a cycle (PID reuse)
                }

                string? name = map.TryGetValue(parent, out var p) ? p.Name : null;
                chain.Add(new ProcessRef { Pid = parent, Name = name, Path = TryGetFullPath(parent) });
                pid = parent;
            }
        }
        catch
        {
            // best-effort
        }
        return chain;
    }

    private static Dictionary<int, (int Parent, string Name)> BuildProcessMap()
    {
        var map = new Dictionary<int, (int Parent, string Name)>();
        IntPtr snapshot = NativeMethods.CreateToolhelp32Snapshot(NativeMethods.TH32CS_SNAPPROCESS, 0);
        if (snapshot == new IntPtr(-1))
        {
            return map;
        }
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
        return map;
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
    /// Reports whether this process (launched by IFEO in place of the interpreter, inheriting the
    /// caller's creation flags) has a console window. No console window means it was launched with
    /// CREATE_NO_WINDOW / DETACHED_PROCESS (a "shadow" background launch). The window handle itself
    /// is never recorded — only the boolean state.
    /// </summary>
    public static WindowVisibility GetWindowVisibility()
    {
        try
        {
            return NativeMethods.GetConsoleWindow() == IntPtr.Zero
                ? WindowVisibility.NoWindow
                : WindowVisibility.Windowed;
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
