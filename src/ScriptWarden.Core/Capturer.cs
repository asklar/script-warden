namespace ScriptWarden.Core;

/// <summary>
/// Runs script extraction and persists captured content into the store, returning the reference
/// records to attach to an <see cref="AuditEvent"/>. Robust by design (individual failures do not
/// throw), since it runs on the latency-sensitive, must-not-break shim path.
/// </summary>
public static class Capturer
{
    private const int MaxDepth = 3;    // how many trampoline hops to follow (cmd -> ps1 -> ps1 ...)
    private const int MaxCaptures = 25; // hard cap on scripts captured per launch

    public static List<CapturedScript> Capture(string root, string imageName, string[] args, string workingDirectory)
    {
        var captured = new List<CapturedScript>();

        List<ExtractionResult> extracted;
        try
        {
            extracted = ScriptExtractor.Extract(imageName, args, workingDirectory);
        }
        catch
        {
            return captured;
        }

        if (extracted.Count == 0)
        {
            return captured;
        }

        var store = new ScriptStore(root);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(ExtractionResult Result, int Depth)>();
        foreach (ExtractionResult e in extracted)
        {
            if (e.FilePath is not null)
            {
                seen.Add(e.FilePath);
            }
            queue.Enqueue((e, 0));
        }

        while (queue.Count > 0 && captured.Count < MaxCaptures)
        {
            (ExtractionResult e, int depth) = queue.Dequeue();
            byte[]? content = StoreOne(store, e, captured);

            // Transitive capture: a captured script (e.g. a cmd trampoline) may itself launch other
            // scripts — often by bare filename in its own folder. Scan its content and follow them.
            if (content is null || content.Length == 0 || depth >= MaxDepth)
            {
                continue;
            }
            string baseDir = BaseDirFor(e, workingDirectory);
            try
            {
                string text = ScriptText.DecodeToText(content);
                foreach (ScriptExtractor.ContentScriptRef r in ScriptExtractor.FindContentScriptReferences(text, baseDir))
                {
                    if (!seen.Add(r.ResolvedPath) || !SafeExists(r.ResolvedPath))
                    {
                        continue;
                    }
                    queue.Enqueue((new ExtractionResult
                    {
                        Kind = ScriptKind.FileReference,
                        Language = r.Language,
                        FilePath = r.ResolvedPath,
                        Extension = r.Extension,
                        OriginalPath = r.Original,
                        Note = "referenced by " + ReferrerName(e),
                    }, depth + 1));
                }
            }
            catch
            {
                // best-effort: content scan must never break capture
            }
        }

        return captured;
    }

    /// <summary>Stores one extraction result; returns the raw content bytes for transitive scanning (or null).</summary>
    private static byte[]? StoreOne(ScriptStore store, ExtractionResult e, List<CapturedScript> captured)
    {
        try
        {
            if (e.InlineContent is not null)
            {
                captured.Add(store.StoreBytes(e.InlineContent, e.Extension, e.Kind, e.Language, e.OriginalPath, e.Note));
                return e.InlineContent;
            }
            if (e.FilePath is not null)
            {
                try
                {
                    byte[] content = ReadCapped(e.FilePath, out bool truncated);
                    captured.Add(store.StoreBytes(content, e.Extension, e.Kind, e.Language, e.OriginalPath, e.Note, truncated));
                    return content;
                }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    captured.Add(Unresolved(e, "referenced path not found (may be created at runtime, or already deleted)"));
                }
                catch (UnauthorizedAccessException)
                {
                    captured.Add(Unresolved(e, "access denied reading referenced path"));
                }
                catch (IOException ex)
                {
                    captured.Add(Unresolved(e, "could not read referenced path: " + ex.Message));
                }
            }
        }
        catch (Exception ex)
        {
            captured.Add(new CapturedScript
            {
                Kind = e.Kind,
                Language = e.Language,
                OriginalPath = e.OriginalPath,
                Note = Combine(e.Note, "capture error: " + ex.Message),
            });
        }
        return null;
    }

    private static string BaseDirFor(ExtractionResult e, string workingDirectory)
    {
        if (e.FilePath is not null)
        {
            try
            {
                string? dir = Path.GetDirectoryName(e.FilePath);
                if (!string.IsNullOrEmpty(dir))
                {
                    return dir;
                }
            }
            catch
            {
                // fall through
            }
        }
        return workingDirectory;
    }

    private static string ReferrerName(ExtractionResult e)
    {
        try
        {
            return e.FilePath is not null ? Path.GetFileName(e.FilePath) : (e.OriginalPath ?? "inline command");
        }
        catch
        {
            return "another script";
        }
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static CapturedScript Unresolved(ExtractionResult e, string note) => new()
    {
        Kind = e.Kind,
        Language = e.Language,
        Extension = ScriptStore.NormalizeExtension(e.Extension),
        OriginalPath = e.OriginalPath,
        Sha256 = "",
        SizeBytes = 0,
        Note = Combine(e.Note, note),
    };

    private static byte[] ReadCapped(string path, out bool truncated)
    {
        truncated = false;
        using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        long toRead = Math.Min(fs.Length, ScriptStore.MaxBytes);
        truncated = fs.Length > ScriptStore.MaxBytes;
        byte[] buffer = new byte[toRead];
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = fs.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
            {
                break;
            }
            offset += read;
        }
        if (offset != buffer.Length)
        {
            Array.Resize(ref buffer, offset);
        }
        return buffer;
    }

    private static string Combine(string? a, string b) =>
        string.IsNullOrEmpty(a) ? b : a + "; " + b;
}
