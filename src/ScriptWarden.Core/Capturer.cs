namespace ScriptWarden.Core;

/// <summary>
/// Runs script extraction and persists captured content into the store, returning the reference
/// records to attach to an <see cref="AuditEvent"/>. Robust by design (individual failures do not
/// throw), since it runs on the latency-sensitive, must-not-break shim path.
/// </summary>
public static class Capturer
{
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

        foreach (ExtractionResult e in extracted)
        {
            try
            {
                if (e.InlineContent is not null)
                {
                    captured.Add(store.StoreBytes(e.InlineContent, e.Extension, e.Kind, e.Language, e.OriginalPath, e.Note));
                }
                else if (e.FilePath is not null)
                {
                    // Attempt the read directly (rather than File.Exists first) so we can distinguish
                    // "not found" from "access denied". A referenced path that doesn't exist yet (e.g.
                    // an output file the script will create) or that we can't read is still recorded.
                    try
                    {
                        byte[] content = ReadCapped(e.FilePath, out bool truncated);
                        captured.Add(store.StoreBytes(content, e.Extension, e.Kind, e.Language, e.OriginalPath, e.Note, truncated));
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
        }

        return captured;
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
