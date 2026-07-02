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
                    if (File.Exists(e.FilePath))
                    {
                        byte[] content = ReadCapped(e.FilePath, out bool truncated);
                        captured.Add(store.StoreBytes(content, e.Extension, e.Kind, e.Language, e.OriginalPath, e.Note, truncated));
                    }
                    else
                    {
                        captured.Add(new CapturedScript
                        {
                            Kind = e.Kind,
                            Language = e.Language,
                            Extension = ScriptStore.NormalizeExtension(e.Extension),
                            OriginalPath = e.OriginalPath,
                            Sha256 = "",
                            SizeBytes = 0,
                            Note = Combine(e.Note, "file not found on disk"),
                        });
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
