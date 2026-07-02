namespace ScriptWarden;

/// <summary>The set of interpreter images script-warden can hook via IFEO.</summary>
internal static class ImageCatalog
{
    /// <summary>Images hooked by default (the ones requested for auditing).</summary>
    public static readonly string[] Default =
    [
        "powershell.exe",
        "cmd.exe",
        "pwsh.exe",
        "cscript.exe",
        "wscript.exe",
    ];

    /// <summary>All images recognized by the tool (used to validate --images and for status).</summary>
    public static readonly string[] Known =
    [
        "powershell.exe",
        "cmd.exe",
        "pwsh.exe",
        "cscript.exe",
        "wscript.exe",
        "powershell_ise.exe",
        "mshta.exe",
    ];

    public static string Normalize(string image)
    {
        image = image.Trim().ToLowerInvariant();
        if (!image.EndsWith(".exe", StringComparison.Ordinal))
        {
            image += ".exe";
        }
        return image;
    }

    public static bool IsKnown(string image) =>
        Array.Exists(Known, k => string.Equals(k, Normalize(image), StringComparison.OrdinalIgnoreCase));
}
