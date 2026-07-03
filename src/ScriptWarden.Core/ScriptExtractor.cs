using System.Text;
using System.Text.RegularExpressions;

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
/// vector. Combines structured per-interpreter parsing with a general "looks like a path to a
/// script file" heuristic that scans every argument (and decoded/inline text), so scripts
/// referenced in less-structured ways (e.g. <c>powershell … "&amp; 'C:\path\x.ps1'"</c>) are still
/// captured. Pure (no IO beyond path combination); the <see cref="Capturer"/> reads/stores content
/// and records whether each referenced path actually exists / is accessible.
/// </summary>
public static partial class ScriptExtractor
{
    private static readonly string[] ScriptExtensions =
        [".ps1", ".psm1", ".psd1", ".cmd", ".bat", ".vbs", ".vbe", ".js", ".jse", ".wsf"];

    // PowerShell host parameters that consume the following token as their value (so we don't
    // mistake that value for a positional command). Matched by prefix (e.g. -ep -> ExecutionPolicy).
    private static readonly string[] PsValueParams =
    [
        "psconsolefile", "version", "configurationname", "executionpolicy",
        "inputformat", "outputformat", "windowstyle", "custompipename",
        "workingdirectory", "settingsfile",
    ];

    public static List<ExtractionResult> Extract(string imageName, string[] args, string workingDirectory)
    {
        imageName = imageName.ToLowerInvariant();
        List<ExtractionResult> results = imageName switch
        {
            "powershell.exe" or "pwsh.exe" or "powershell_ise.exe" => ExtractPowerShell(args, workingDirectory),
            "cmd.exe" => ExtractCmd(args, workingDirectory),
            "cscript.exe" or "wscript.exe" => ExtractScriptHost(args, workingDirectory),
            _ => [],
        };

        // Heuristic safety net: capture any script-file path referenced anywhere in the arguments,
        // regardless of how it was passed. Deduplicated against what structured parsing found.
        ScanForScriptPaths(string.Join(' ', args), workingDirectory, results, "referenced in command line");

        return results;
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
                    if (ok)
                    {
                        // The decoded command may itself reference script files.
                        ScanForScriptPaths(decoded, cwd, results, "referenced inside -EncodedCommand");
                    }
                    return results;
                }

                if (MatchesPowerShellParam(name, "command", 'c'))
                {
                    AddInline(JoinRemaining(args, i + 1), results, "captured from -Command");
                    return results;
                }

                if (MatchesPowerShellParam(name, "file", 'f') && i + 1 < args.Length)
                {
                    results.Add(FileRef(args[i + 1], cwd, ScriptLanguage.PowerShell));
                    return results;
                }

                if (IsValueTakingParam(name) && i + 1 < args.Length)
                {
                    i++; // skip this parameter's value
                    continue;
                }

                continue; // flag switch (e.g. -NoProfile, -NonInteractive)
            }

            // Positional argument: PowerShell's default parameter is -Command. A lone token that is
            // itself a script path is treated as a file; otherwise it's an inline command.
            if (i == args.Length - 1 && LooksLikeScriptPath(a, ScriptExtensions))
            {
                results.Add(FileRef(a, cwd, LanguageForExtension(GetExtension(a))));
                return results;
            }

            AddInline(JoinRemaining(args, i), results, "captured from positional command");
            return results;
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
                if (LooksLikeScriptPath(firstPath, ScriptExtensions))
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
            results.Add(FileRef(path, cwd, LanguageForExtension(GetExtension(path)), ScriptKind.ScriptArgument));
            return results;
        }

        return results;
    }

    /// <summary>
    /// Scans free text for tokens that look like a path to a script file (quoted paths, or rooted /
    /// UNC / relative paths ending in a script extension) and adds a file reference for each new one.
    /// Paths that don't exist or aren't accessible are still recorded; the capturer notes their state.
    /// </summary>
    private static void ScanForScriptPaths(string text, string cwd, List<ExtractionResult> results, string note)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ExtractionResult r in results)
        {
            if (r.FilePath is not null)
            {
                seen.Add(r.FilePath);
            }
        }

        foreach (Match m in ScriptPathRegex().Matches(text))
        {
            string candidate = (m.Groups["q"].Success ? m.Groups["q"].Value : m.Groups["u"].Value).Trim();
            if (candidate.Length == 0)
            {
                continue;
            }

            string resolved = ResolvePath(candidate, cwd);
            if (!seen.Add(resolved))
            {
                continue;
            }

            string ext = GetExtension(candidate);
            results.Add(new ExtractionResult
            {
                Kind = ScriptKind.FileReference,
                Language = LanguageForExtension(ext),
                FilePath = resolved,
                Extension = string.IsNullOrEmpty(ext) ? ".txt" : ext,
                OriginalPath = candidate,
                Note = note,
            });
        }
    }

    private static void AddInline(string text, List<ExtractionResult> results, string note)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Trim() == "-")
        {
            return;
        }
        results.Add(new ExtractionResult
        {
            Kind = ScriptKind.InlineCommand,
            Language = ScriptLanguage.PowerShell,
            InlineContent = Encoding.UTF8.GetBytes(text),
            Extension = ".ps1",
            Note = note,
        });
    }

    private static ExtractionResult FileRef(string path, string cwd, ScriptLanguage lang, ScriptKind kind = ScriptKind.FileReference)
    {
        string resolved = ResolvePath(path.Trim('"'), cwd);
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

    private static bool IsValueTakingParam(string name) =>
        name.Length > 0 && Array.Exists(PsValueParams, p => p.StartsWith(name, StringComparison.Ordinal));

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

    // Matches a quoted path (group "q") ending in a script extension, OR an unquoted rooted / UNC /
    // relative path (group "u") ending in a script extension. Unquoted bare filenames (no path
    // separator) are intentionally not matched here to avoid false positives; those are handled by
    // the structured parsers when passed as a script argument.
    [GeneratedRegex(
        "['\"](?<q>[^'\"\\r\\n]{1,320}?\\.(?:ps1|psm1|psd1|cmd|bat|vbs|vbe|js|jse|wsf))['\"]" +
        "|(?<![\\w.])(?<u>(?:[A-Za-z]:[\\\\/]|\\\\\\\\|\\.{1,2}[\\\\/]|[\\\\/])[^\\s'\"()|&;,<>]{0,320}?\\.(?:ps1|psm1|psd1|cmd|bat|vbs|vbe|js|jse|wsf))",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ScriptPathRegex();
}
