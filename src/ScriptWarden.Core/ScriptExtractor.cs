using System.Text;

namespace ScriptWarden.Core;

/// <summary>A script/command found on an interpreter command line, before its content is stored.</summary>
public sealed class ExtractionResult
{
    public ScriptKind Kind { get; init; }
    public ScriptLanguage Language { get; init; }

    /// <summary>Absolute path to a file to copy, when <see cref="Kind"/> is a file/script reference.</summary>
    public string? FilePath { get; init; }

    /// <summary>Inline/decoded content bytes, when the script did not come from a file.</summary>
    public byte[]? InlineContent { get; init; }

    /// <summary>Suggested store extension (including the dot).</summary>
    public string Extension { get; init; } = ".txt";

    /// <summary>The reference exactly as written on the command line (for display).</summary>
    public string? OriginalPath { get; init; }

    public string? Note { get; init; }
}

/// <summary>
/// Extracts referenced script files and inline/encoded commands from an interpreter's argument
/// vector. Pure (no IO beyond path combination); the <see cref="Capturer"/> reads/stores content.
/// </summary>
public static class ScriptExtractor
{
    /// <summary>
    /// Inspects the argument vector for the given hooked <paramref name="imageName"/> (lowercase,
    /// e.g. "powershell.exe") and returns any scripts/commands referenced.
    /// </summary>
    public static List<ExtractionResult> Extract(string imageName, string[] args, string workingDirectory)
    {
        imageName = imageName.ToLowerInvariant();
        return imageName switch
        {
            "powershell.exe" or "pwsh.exe" or "powershell_ise.exe" => ExtractPowerShell(args, workingDirectory),
            "cmd.exe" => ExtractCmd(args, workingDirectory),
            "cscript.exe" or "wscript.exe" => ExtractScriptHost(args, workingDirectory),
            _ => [],
        };
    }

    private static List<ExtractionResult> ExtractPowerShell(string[] args, string cwd)
    {
        var results = new List<ExtractionResult>();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];

            if (IsSwitch(a))
            {
                string name = a.TrimStart('-', '/', '\u2013', '\u2014').ToLowerInvariant();

                if ((MatchesPowerShellParam(name, "encodedcommand", 'e') || name == "ec") && i + 1 < args.Length)
                {
                    string decoded = TryDecodeBase64Utf16(args[i + 1], out bool ok);
                    results.Add(new ExtractionResult
                    {
                        Kind = ScriptKind.EncodedCommand,
                        Language = ScriptLanguage.PowerShell,
                        InlineContent = Encoding.UTF8.GetBytes(decoded),
                        Extension = ".ps1",
                        Note = ok ? "decoded from -EncodedCommand" : "could not decode -EncodedCommand (stored raw)",
                    });
                    return results;
                }

                if (MatchesPowerShellParam(name, "command", 'c'))
                {
                    string inline = JoinRemaining(args, i + 1);
                    if (!string.IsNullOrWhiteSpace(inline) && inline.Trim() != "-")
                    {
                        results.Add(new ExtractionResult
                        {
                            Kind = ScriptKind.InlineCommand,
                            Language = ScriptLanguage.PowerShell,
                            InlineContent = Encoding.UTF8.GetBytes(inline),
                            Extension = ".ps1",
                            Note = "captured from -Command",
                        });
                    }
                    return results;
                }

                if (MatchesPowerShellParam(name, "file", 'f') && i + 1 < args.Length)
                {
                    string path = args[i + 1];
                    results.Add(FileRef(path, cwd, ScriptLanguage.PowerShell));
                    return results;
                }

                // Unknown switch (e.g. -NoProfile, -ExecutionPolicy). Skip; keep scanning.
                continue;
            }

            // Positional argument. PowerShell runs a positional .ps1 as a script file.
            if (LooksLikeScriptPath(a, ".ps1"))
            {
                results.Add(FileRef(a, cwd, ScriptLanguage.PowerShell));
                return results;
            }
        }

        return results;
    }

    private static List<ExtractionResult> ExtractCmd(string[] args, string cwd)
    {
        var results = new List<ExtractionResult>();

        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a.Length == 2 && (a[0] == '/' || a[0] == '-') &&
                (a[1] == 'c' || a[1] == 'C' || a[1] == 'k' || a[1] == 'K'))
            {
                string first = i + 1 < args.Length ? args[i + 1] : "";
                string command = JoinRemaining(args, i + 1);

                string firstPath = first.Trim('"');
                if (LooksLikeScriptPath(firstPath, ".bat", ".cmd", ".ps1", ".vbs", ".js", ".wsf"))
                {
                    results.Add(FileRef(firstPath, cwd, LanguageForExtension(GetExtension(firstPath))));
                }
                else if (!string.IsNullOrWhiteSpace(command))
                {
                    results.Add(new ExtractionResult
                    {
                        Kind = ScriptKind.InlineCommand,
                        Language = ScriptLanguage.Batch,
                        InlineContent = Encoding.UTF8.GetBytes(command),
                        Extension = ".cmd",
                        Note = $"captured from cmd {a}",
                    });
                }
                return results;
            }
        }

        return results;
    }

    private static List<ExtractionResult> ExtractScriptHost(string[] args, string cwd)
    {
        var results = new List<ExtractionResult>();

        foreach (string a in args)
        {
            // Windows Script Host options start with "//"; the first other token is the script.
            if (a.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            string path = a.Trim('"');
            var lang = LanguageForExtension(GetExtension(path));
            results.Add(FileRef(path, cwd, lang, ScriptKind.ScriptArgument));
            return results;
        }

        return results;
    }

    private static ExtractionResult FileRef(string path, string cwd, ScriptLanguage lang, ScriptKind kind = ScriptKind.FileReference)
    {
        string resolved = ResolvePath(path, cwd);
        string ext = GetExtension(resolved);
        return new ExtractionResult
        {
            Kind = kind,
            Language = lang,
            FilePath = resolved,
            Extension = string.IsNullOrEmpty(ext) ? ".txt" : ext,
            OriginalPath = path,
        };
    }

    private static bool IsSwitch(string a) =>
        a.Length >= 1 && (a[0] == '-' || a[0] == '/' || a[0] == '\u2013' || a[0] == '\u2014');

    /// <summary>
    /// PowerShell parameter prefix matching: <paramref name="name"/> must be a non-empty prefix of
    /// <paramref name="full"/> and begin with <paramref name="firstChar"/> (disambiguates e.g. -e
    /// [EncodedCommand] from -ex [ExecutionPolicy], which is not a prefix of "encodedcommand").
    /// </summary>
    private static bool MatchesPowerShellParam(string name, string full, char firstChar) =>
        name.Length > 0 && name[0] == firstChar && full.StartsWith(name, StringComparison.Ordinal);

    private static string JoinRemaining(string[] args, int start)
    {
        if (start >= args.Length)
        {
            return "";
        }
        return string.Join(' ', args[start..]);
    }

    private static bool LooksLikeScriptPath(string a, params string[] extensions)
    {
        if (string.IsNullOrWhiteSpace(a) || IsSwitch(a))
        {
            return false;
        }
        string ext = GetExtension(a);
        foreach (string e in extensions)
        {
            if (string.Equals(ext, e, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static string GetExtension(string path)
    {
        try
        {
            return Path.GetExtension(path);
        }
        catch
        {
            return "";
        }
    }

    private static string ResolvePath(string path, string cwd)
    {
        try
        {
            if (Path.IsPathRooted(path))
            {
                return Path.GetFullPath(path);
            }
            return Path.GetFullPath(Path.Combine(cwd, path));
        }
        catch
        {
            return path;
        }
    }

    private static ScriptLanguage LanguageForExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".ps1" or ".psm1" or ".psd1" => ScriptLanguage.PowerShell,
        ".bat" or ".cmd" => ScriptLanguage.Batch,
        ".vbs" or ".vbe" => ScriptLanguage.VBScript,
        ".js" or ".jse" => ScriptLanguage.JScript,
        ".wsf" => ScriptLanguage.WindowsScriptFile,
        _ => ScriptLanguage.Unknown,
    };

    /// <summary>Decodes a PowerShell -EncodedCommand (base64 of UTF-16LE). Falls back to the raw string.</summary>
    private static string TryDecodeBase64Utf16(string value, out bool ok)
    {
        try
        {
            byte[] bytes = Convert.FromBase64String(value);
            ok = true;
            return Encoding.Unicode.GetString(bytes);
        }
        catch
        {
            ok = false;
            return value;
        }
    }
}
