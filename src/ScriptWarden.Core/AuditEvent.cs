using System.Text.Json.Serialization;

namespace ScriptWarden.Core;

/// <summary>
/// A reference to a script/command captured from an interpreter launch. The actual content is
/// stored content-addressed under <c>scripts\&lt;sha256&gt;.&lt;ext&gt;</c>; this record points at it.
/// </summary>
public sealed class CapturedScript
{
    /// <summary>How the script was referenced on the command line.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScriptKind>))]
    public ScriptKind Kind { get; set; }

    /// <summary>Best-effort language classification.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ScriptLanguage>))]
    public ScriptLanguage Language { get; set; }

    /// <summary>SHA-256 (hex, lowercase) of the captured content; also the store file stem.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Store file extension including the dot (e.g. <c>.ps1</c>).</summary>
    public string Extension { get; set; } = "";

    /// <summary>Original on-disk path, when the script was a file reference.</summary>
    public string? OriginalPath { get; set; }

    /// <summary>Size in bytes of the captured content.</summary>
    public long SizeBytes { get; set; }

    /// <summary>True when the content was truncated because it exceeded the capture cap.</summary>
    public bool Truncated { get; set; }

    /// <summary>Optional human-readable note (e.g. "decoded from -EncodedCommand", "file not found").</summary>
    public string? Note { get; set; }
}

/// <summary>
/// One audit record describing a single launch of a hooked interpreter, intercepted by the shim.
/// Serialized as a standalone JSON file under the audit root's <c>events\</c> directory.
/// </summary>
public sealed class AuditEvent
{
    /// <summary>Schema version for forward compatibility.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Unique id for this event (also part of the event file name).</summary>
    public string EventId { get; set; } = "";

    /// <summary>When the launch was intercepted (UTC).</summary>
    public DateTimeOffset TimestampUtc { get; set; }

    /// <summary>Hooked image name, lowercase (e.g. <c>powershell.exe</c>).</summary>
    public string HookedImage { get; set; } = "";

    /// <summary>Full path to the real interpreter that was launched.</summary>
    public string TargetPath { get; set; } = "";

    /// <summary>The original command line (interpreter path + args) as reconstructed for relaunch.</summary>
    public string CommandLine { get; set; } = "";

    /// <summary>The parsed argument vector passed to the interpreter (excluding the interpreter path).</summary>
    public string[] Arguments { get; set; } = [];

    /// <summary>Working directory of the shim (inherited by the child).</summary>
    public string WorkingDirectory { get; set; } = "";

    /// <summary>User the launch ran as (DOMAIN\user).</summary>
    public string? User { get; set; }

    /// <summary>SID of the user the launch ran as.</summary>
    public string? UserSid { get; set; }

    /// <summary>Windows session id.</summary>
    public int SessionId { get; set; }

    /// <summary>Whether the launch had a visible window/console (interactive vs "shadow" background).</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<WindowVisibility>))]
    public WindowVisibility Window { get; set; } = WindowVisibility.Unknown;

    /// <summary>PID of the shim process.</summary>
    public int ShimProcessId { get; set; }

    /// <summary>PID of the relaunched child interpreter, if it started.</summary>
    public int ChildProcessId { get; set; }

    /// <summary>Parent process image name (who launched the interpreter), if resolvable.</summary>
    public string? ParentProcessName { get; set; }

    /// <summary>Parent process id, if resolvable.</summary>
    public int ParentProcessId { get; set; }

    /// <summary>Parent process full image path, if resolvable.</summary>
    public string? ParentProcessPath { get; set; }

    /// <summary>Exit code of the child interpreter, filled after it exits.</summary>
    public int? ExitCode { get; set; }

    /// <summary>Scripts/commands captured from this launch.</summary>
    public List<CapturedScript> Scripts { get; set; } = [];

    /// <summary>Which root this event was read from. Set by the reader; persisted value is ignored on read.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<AuditOrigin>))]
    public AuditOrigin Origin { get; set; } = AuditOrigin.Unknown;
}
