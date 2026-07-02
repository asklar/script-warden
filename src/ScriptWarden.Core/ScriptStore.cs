using System.Security.Cryptography;

namespace ScriptWarden.Core;

/// <summary>
/// Content-addressed store for captured scripts: files are written as <c>scripts\&lt;sha256&gt;.&lt;ext&gt;</c>,
/// so identical scripts (a common case for IT-pushed scripts) are stored once.
/// </summary>
public sealed class ScriptStore
{
    /// <summary>Maximum bytes captured per script; larger content is truncated.</summary>
    public const long MaxBytes = 5L * 1024 * 1024;

    private readonly string _scriptsDir;

    public ScriptStore(string root)
    {
        _scriptsDir = DataRoots.ScriptsDir(root);
    }

    /// <summary>Stores content (capping at <see cref="MaxBytes"/>) and returns a reference record.</summary>
    public CapturedScript StoreBytes(
        byte[] content,
        string extension,
        ScriptKind kind,
        ScriptLanguage language,
        string? originalPath,
        string? note,
        bool alreadyTruncated = false)
    {
        bool truncated = alreadyTruncated;
        if (content.LongLength > MaxBytes)
        {
            content = content[..(int)MaxBytes];
            truncated = true;
        }

        string ext = NormalizeExtension(extension);
        string sha = Sha256Hex(content);

        Directory.CreateDirectory(_scriptsDir);
        string target = Path.Combine(_scriptsDir, sha + ext);
        if (!File.Exists(target))
        {
            string tmp = target + "." + Environment.ProcessId + ".tmp";
            File.WriteAllBytes(tmp, content);
            try
            {
                File.Move(tmp, target, overwrite: false);
            }
            catch (IOException)
            {
                // Another process stored the same content first; discard our temp.
                TryDelete(tmp);
            }
        }

        return new CapturedScript
        {
            Kind = kind,
            Language = language,
            Sha256 = sha,
            Extension = ext,
            SizeBytes = content.Length,
            Truncated = truncated,
            OriginalPath = originalPath,
            Note = note,
        };
    }

    /// <summary>Resolves the on-disk path of a stored script by hash + extension.</summary>
    public string ScriptPath(string sha256, string extension) =>
        Path.Combine(_scriptsDir, sha256 + NormalizeExtension(extension));

    public static string NormalizeExtension(string ext)
    {
        if (string.IsNullOrEmpty(ext))
        {
            return ".txt";
        }
        return ext[0] == '.' ? ext : "." + ext;
    }

    public static string Sha256Hex(byte[] content)
    {
        byte[] hash = SHA256.HashData(content);
        return Convert.ToHexStringLower(hash);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // best-effort cleanup
        }
    }
}
