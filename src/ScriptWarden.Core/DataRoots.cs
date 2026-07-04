namespace ScriptWarden.Core;

/// <summary>A resolved audit root with its origin and read accessibility.</summary>
public sealed class ResolvedRoot
{
    public required string Path { get; init; }
    public required AuditOrigin Origin { get; init; }
    public bool Exists { get; init; }
    public bool Readable { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Resolves the on-disk audit roots. Data is stored per-user under
/// <c>%LOCALAPPDATA%\script-warden</c> (overridable via <c>SCRIPT_WARDEN_DATA</c>). Launches that
/// run as SYSTEM land under the systemprofile; the viewer reads both.
/// </summary>
public static class DataRoots
{
    public const string EnvOverride = "SCRIPT_WARDEN_DATA";
    public const string FolderName = "script-warden";

    /// <summary>The root the current process writes to (systemprofile when running as SYSTEM).</summary>
    public static string CurrentUserRoot()
    {
        string? overridden = Environment.GetEnvironmentVariable(EnvOverride);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden;
        }
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(localAppData, FolderName);
    }

    /// <summary>The SYSTEM account's audit root (systemprofile LocalAppData).</summary>
    public static string SystemRoot()
    {
        string systemRoot = Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";
        return Path.Combine(systemRoot, "System32", "config", "systemprofile", "AppData", "Local", FolderName);
    }

    public static string EventsDir(string root) => Path.Combine(root, "events");

    public static string ScriptsDir(string root) => Path.Combine(root, "scripts");

    /// <summary>Where the viewer moves already-ingested event files (per day) to keep <c>events\</c> small.</summary>
    public static string ArchiveDir(string root) => Path.Combine(root, "archive");

    public static void EnsureLayout(string root)
    {
        Directory.CreateDirectory(EventsDir(root));
        Directory.CreateDirectory(ScriptsDir(root));
    }

    /// <summary>
    /// The roots the viewer should merge (current user + SYSTEM), de-duplicated, each annotated with
    /// existence and read accessibility so the UI can surface an "unreadable root" notice.
    /// </summary>
    public static IReadOnlyList<ResolvedRoot> ForViewer()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var roots = new List<ResolvedRoot>();

        foreach (var (path, origin) in new[]
                 {
                     (CurrentUserRoot(), AuditOrigin.CurrentUser),
                     (SystemRoot(), AuditOrigin.System),
                 })
        {
            string normalized;
            try
            {
                normalized = Path.GetFullPath(path);
            }
            catch
            {
                normalized = path;
            }

            if (!seen.Add(normalized))
            {
                continue;
            }

            roots.Add(Probe(normalized, origin));
        }

        return roots;
    }

    private static ResolvedRoot Probe(string path, AuditOrigin origin)
    {
        string eventsDir = EventsDir(path);
        bool exists;
        try
        {
            exists = Directory.Exists(path);
        }
        catch
        {
            exists = false;
        }

        bool readable = false;
        string? error = null;
        try
        {
            if (Directory.Exists(eventsDir))
            {
                using var _ = Directory.EnumerateFileSystemEntries(eventsDir).GetEnumerator();
                _.MoveNext();
                readable = true;
            }
            else
            {
                // No events dir yet; readable if the root itself is accessible (or absent).
                readable = true;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
        }
        catch (Exception ex)
        {
            error = ex.Message;
        }

        return new ResolvedRoot
        {
            Path = path,
            Origin = origin,
            Exists = exists,
            Readable = readable,
            Error = error,
        };
    }
}
