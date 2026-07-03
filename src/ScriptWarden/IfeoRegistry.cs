using Microsoft.Win32;

namespace ScriptWarden;

internal enum HookState
{
    NotHooked,
    HookedByUs,
    HookedByOther,
}

/// <summary>Current IFEO Debugger state for an image in a specific registry view.</summary>
internal readonly record struct HookInfo(string Image, RegistryView View, HookState State, string? Value);

/// <summary>
/// Reads and writes the IFEO <c>Debugger</c> value for interpreter images. IFEO lives only in HKLM,
/// so writes require elevation. Both the 64-bit and 32-bit (WOW6432Node) views are handled so that
/// interpreter launches initiated by either 64-bit or 32-bit parents are caught.
/// </summary>
internal static class IfeoRegistry
{
    public const string BasePath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options";
    private const string OwnerMarker = "script-warden.exe";

    public static readonly RegistryView[] Views = [RegistryView.Registry64, RegistryView.Registry32];

    public static string DebuggerValueFor(string installedExePath) => $"\"{installedExePath}\" shim";

    public static string? GetDebugger(string image, RegistryView view)
    {
        try
        {
            using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
            using RegistryKey? key = baseKey.OpenSubKey($@"{BasePath}\{image}");
            return key?.GetValue("Debugger") as string;
        }
        catch
        {
            return null;
        }
    }

    public static void SetDebugger(string image, string debuggerValue, RegistryView view)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using RegistryKey key = baseKey.CreateSubKey($@"{BasePath}\{image}", writable: true);
        key.SetValue("Debugger", debuggerValue, RegistryValueKind.String);
    }

    /// <summary>Removes our Debugger value only if it currently points at script-warden.</summary>
    public static bool RemoveOurDebugger(string image, RegistryView view)
    {
        using RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
        using RegistryKey? key = baseKey.OpenSubKey($@"{BasePath}\{image}", writable: true);
        if (key is null)
        {
            return false;
        }

        if (key.GetValue("Debugger") is string current && IsOurs(current))
        {
            key.DeleteValue("Debugger", throwOnMissingValue: false);
            return true;
        }
        return false;
    }

    public static HookInfo GetStatus(string image, RegistryView view)
    {
        string? value = GetDebugger(image, view);
        HookState state = value is null
            ? HookState.NotHooked
            : IsOurs(value) ? HookState.HookedByUs : HookState.HookedByOther;
        return new HookInfo(image, view, state, value);
    }

    public static bool IsOurs(string debuggerValue) =>
        debuggerValue.Contains(OwnerMarker, StringComparison.OrdinalIgnoreCase);
}
