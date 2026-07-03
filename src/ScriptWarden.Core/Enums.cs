namespace ScriptWarden.Core;

/// <summary>How a script/command was referenced on the interpreter command line.</summary>
public enum ScriptKind
{
    Unknown = 0,

    /// <summary>A script file referenced on disk (e.g. <c>powershell -File x.ps1</c>, <c>cmd /c a.bat</c>).</summary>
    FileReference,

    /// <summary>Base64 payload from <c>-EncodedCommand</c>, decoded to text.</summary>
    EncodedCommand,

    /// <summary>Inline command text (e.g. <c>-Command "..."</c> or <c>cmd /c "echo hi"</c>).</summary>
    InlineCommand,

    /// <summary>A script passed as a positional argument (e.g. <c>cscript foo.vbs</c>).</summary>
    ScriptArgument,
}

/// <summary>Best-effort language classification for a captured script.</summary>
public enum ScriptLanguage
{
    Unknown = 0,
    PowerShell,
    Batch,
    VBScript,
    JScript,
    WindowsScriptFile,
}

/// <summary>Which audit root an event was read from.</summary>
public enum AuditOrigin
{
    Unknown = 0,
    CurrentUser,
    System,
}

/// <summary>
/// Whether the launch had a visible window/console. Lets you distinguish interactive launches from
/// "shadow" background launches (e.g. created with CREATE_NO_WINDOW or a hidden window).
/// </summary>
public enum WindowVisibility
{
    Unknown = 0,

    /// <summary>A console window exists and is visible (typical interactive launch).</summary>
    Visible,

    /// <summary>A console exists but the window is hidden (e.g. STARTF_USESHOWWINDOW + SW_HIDE).</summary>
    Hidden,

    /// <summary>No console window (CREATE_NO_WINDOW / DETACHED_PROCESS, or a GUI host).</summary>
    None,
}
