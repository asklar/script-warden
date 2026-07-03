using System.Text.Json;

namespace ScriptWarden.Core;

/// <summary>
/// User-configurable settings, stored as <c>config.json</c> in the audit root. Read by the shim on
/// each launch to decide whether to audit; edited via the viewer's Settings page or the CLI.
/// </summary>
public sealed class WardenConfig
{
    /// <summary>Master switch. When false, nothing is audited (launches still run transparently).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Parent process names to exclude from auditing (e.g. "copilot.exe"). Case-insensitive.</summary>
    public List<string> ExcludedParents { get; set; } = [];

    /// <summary>Hooked image names to exclude from auditing (e.g. "cmd.exe"). Case-insensitive.</summary>
    public List<string> ExcludedImages { get; set; } = [];

    /// <summary>True if a launch with the given hooked image and parent should NOT be audited.</summary>
    public bool IsExcluded(string? hookedImage, string? parentName)
    {
        if (!Enabled)
        {
            return true;
        }
        if (parentName is not null && MatchesAny(ExcludedParents, parentName))
        {
            return true;
        }
        if (hookedImage is not null && MatchesAny(ExcludedImages, hookedImage))
        {
            return true;
        }
        return false;
    }

    private static bool MatchesAny(List<string> list, string name)
    {
        string target = NormalizeName(name);
        foreach (string entry in list)
        {
            string e = NormalizeName(entry);
            if (e.Length > 0 && e == target)
            {
                return true;
            }
        }
        return false;
    }

    private static string NormalizeName(string s)
    {
        s = s.Trim().ToLowerInvariant();
        if (s.EndsWith(".exe", StringComparison.Ordinal))
        {
            s = s[..^4];
        }
        return s;
    }
}

/// <summary>Loads and saves <see cref="WardenConfig"/> from a root's <c>config.json</c>.</summary>
public static class ConfigStore
{
    public static string ConfigPath(string root) => Path.Combine(root, "config.json");

    public static WardenConfig Load(string root)
    {
        try
        {
            string path = ConfigPath(root);
            if (!File.Exists(path))
            {
                return new WardenConfig();
            }
            using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize(fs, AuditJsonContext.Default.WardenConfig) ?? new WardenConfig();
        }
        catch
        {
            return new WardenConfig();
        }
    }

    public static void Save(string root, WardenConfig config)
    {
        Directory.CreateDirectory(root);
        string path = ConfigPath(root);
        string tmp = path + ".tmp";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(config, AuditJsonContext.Default.WardenConfig);
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
