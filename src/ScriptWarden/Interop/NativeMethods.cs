using System.Runtime.InteropServices;

namespace ScriptWarden.Interop;

/// <summary>
/// Native interop for the shim. Uses source-generated P/Invoke (<see cref="LibraryImportAttribute"/>)
/// so it is Native AOT friendly (no reflection-based marshalling).
/// </summary>
internal static partial class NativeMethods
{
    public const uint DEBUG_PROCESS = 0x00000001;
    public const uint DEBUG_ONLY_THIS_PROCESS = 0x00000002;
    public const uint STARTF_USESTDHANDLES = 0x00000100;
    public const uint INFINITE = 0xFFFFFFFF;

    public const int STD_INPUT_HANDLE = -10;
    public const int STD_OUTPUT_HANDLE = -11;
    public const int STD_ERROR_HANDLE = -12;

    public const uint TH32CS_SNAPPROCESS = 0x00000002;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x00001000;

    public const uint STARTF_USESHOWWINDOW = 0x00000001;
    public const ushort SW_HIDE = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOW
    {
        public uint cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public uint dwX;
        public uint dwY;
        public uint dwXSize;
        public uint dwYSize;
        public uint dwXCountChars;
        public uint dwYCountChars;
        public uint dwFillAttribute;
        public uint dwFlags;
        public ushort wShowWindow;
        public ushort cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct PROCESSENTRY32W
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        public fixed char szExeFile[260];
    }

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool CreateProcessW(
        string? lpApplicationName,
        char* lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        in STARTUPINFOW lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetExitCodeProcess(IntPtr hProcess, out uint lpExitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DebugActiveProcessStop(uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DebugSetProcessKillOnExit([MarshalAs(UnmanagedType.Bool)] bool KillOnExit);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32FirstW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Process32NextW(IntPtr hSnapshot, ref PROCESSENTRY32W lppe);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    public static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, char[] lpExeName, ref uint lpdwSize);

    [LibraryImport("kernel32.dll", EntryPoint = "SearchPathW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial uint SearchPath(
        string? lpPath,
        string lpFileName,
        string? lpExtension,
        uint nBufferLength,
        char[] lpBuffer,
        IntPtr lpFilePart);

    [LibraryImport("kernel32.dll", SetLastError = false)]
    public static partial IntPtr GetConsoleWindow();

    [LibraryImport("kernel32.dll", EntryPoint = "GetCommandLineW")]
    public static partial IntPtr GetCommandLineW();

    /// <summary>
    /// Returns the raw process command line exactly as Windows passed it. Unlike
    /// <c>Environment.CommandLine</c> (which is re-serialized from argv and loses the original
    /// quoting — see dotnet/runtime#25841 "Value of Environment.CommandLine is different with the
    /// actual input"), this is byte-for-byte what the OS provided, which is essential for forwarding
    /// an interpreter's command line verbatim.
    /// </summary>
    public static string GetRawCommandLine()
    {
        IntPtr ptr = GetCommandLineW();
        return ptr == IntPtr.Zero ? "" : Marshal.PtrToStringUni(ptr) ?? "";
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetStartupInfoW")]
    public static partial void GetStartupInfo(out STARTUPINFOW lpStartupInfo);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);
}
