using ScriptWarden.Core;
using ScriptWarden.Interop;

namespace ScriptWarden.Commands;

/// <summary>
/// The runtime shim, invoked by Windows via the IFEO Debugger value as:
/// <c>script-warden.exe shim "&lt;real interpreter path&gt;" &lt;original args&gt;</c>.
///
/// It records the launch, captures any referenced/inline script, and transparently relaunches the
/// real interpreter. This is the hot path: it must be fast and MUST NOT break the launch, so every
/// audit step is best-effort (fail-open) and the child is started as early as possible.
/// </summary>
internal static class ShimCommand
{
    private const string DepthEnv = "SCRIPT_WARDEN_DEPTH";
    private const int MaxDepth = 8;

    /// <summary><paramref name="args"/> is the full argv, with args[0] == "shim".</summary>
    public static int Run(string[] args)
    {
        // Belt-and-suspenders recursion guard. The DEBUG_ONLY_THIS_PROCESS relaunch normally prevents
        // any re-entry; this only matters in the rare case where the process PEB has
        // ReadImageFileExecOptions set globally. Bound any runaway loop rather than fork-bombing.
        int depth = ParseDepth();
        if (depth > MaxDepth)
        {
            return 1;
        }
        Environment.SetEnvironmentVariable(DepthEnv, (depth + 1).ToString());

        if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
        {
            // Nothing to launch; nothing sensible to do.
            return 1;
        }

        string targetPath = args[1];
        string[] interpreterArgs = args.Length > 2 ? args[2..] : [];

        // Windows may hand us a bare interpreter name (e.g. "pwsh.exe") that relies on PATH. Resolve
        // it to a full path so CreateProcess (which does not search PATH for lpApplicationName) works.
        string? resolvedTarget = TransparentLauncher.ResolveImagePath(targetPath);

        // Reconstruct the child's command line verbatim: strip our own exe token and the "shim"
        // token, preserving the exact original quoting of the remainder. We must use the RAW OS
        // command line (GetCommandLineW) — under Native AOT, Environment.CommandLine is re-quoted
        // from argv, which corrupts cmd-style quoting (e.g. "" -> \") and breaks the relaunch.
        string childCommandLine = CommandLineParser.StripLeadingTokens(NativeMethods.GetRawCommandLine(), 2);
        if (string.IsNullOrEmpty(childCommandLine))
        {
            childCommandLine = QuoteIfNeeded(targetPath);
        }

        // Start the child first so we add as little latency as possible to the launch.
        long startTick = Environment.TickCount64;
        StartedProcess started;
        try
        {
            started = TransparentLauncher.Start(resolvedTarget, childCommandLine);
        }
        catch (Exception ex)
        {
            // If we cannot start the child, there is no safe fallback (a normal launch would
            // re-trigger IFEO). Report and fail; the operator can `uninstall` to remove hooks.
            TryWriteStdErr($"script-warden: failed to launch '{targetPath}': {ex.Message}");
            return 1;
        }

        string root = DataRoots.CurrentUserRoot();
        var identity = ProcessDetails.GetIdentity();
        List<ProcessRef> ancestors = ProcessDetails.GetAncestors();
        ProcessRef? parent = ancestors.Count > 0 ? ancestors[0] : null;
        string hookedImage = SafeFileName(resolvedTarget ?? targetPath);

        // Honor exclusions (e.g. don't audit when launched by copilot.exe). Excluded launches still
        // run transparently; we simply skip capture + logging.
        WardenConfig config = ConfigStore.Load(root);
        if (config.IsExcluded(hookedImage, parent?.Name))
        {
            return TransparentLauncher.WaitForExit(started);
        }

        AuditEvent ev = BuildEvent(resolvedTarget ?? targetPath, interpreterArgs, childCommandLine, started.Pid, identity, ancestors, hookedImage);

        // Capture + write the initial record while the child runs (visible immediately, even for
        // long-lived interactive shells). All best-effort.
        try
        {
            ev.Scripts = Capturer.Capture(root, ev.HookedImage, interpreterArgs, ev.WorkingDirectory);
        }
        catch
        {
            // ignore capture failures
        }

        TryWriteEvent(root, ev);

        // Wait and propagate the exit code + duration, then update the record.
        int exitCode = TransparentLauncher.WaitForExit(started);
        ev.ExitCode = exitCode;
        ev.DurationMs = Environment.TickCount64 - startTick;
        TryWriteEvent(root, ev);

        return exitCode;
    }

    private static AuditEvent BuildEvent(
        string targetPath,
        string[] interpreterArgs,
        string commandLine,
        int childPid,
        ProcessDetails.Identity identity,
        List<ProcessRef> ancestors,
        string hookedImage)
    {
        ProcessRef? parent = ancestors.Count > 0 ? ancestors[0] : null;
        return new AuditEvent
        {
            EventId = Guid.NewGuid().ToString(),
            TimestampUtc = DateTimeOffset.UtcNow,
            HookedImage = hookedImage,
            TargetPath = targetPath,
            CommandLine = commandLine,
            Arguments = interpreterArgs,
            WorkingDirectory = SafeCwd(),
            User = identity.User,
            UserSid = identity.Sid,
            SessionId = identity.SessionId,
            ShimProcessId = Environment.ProcessId,
            ChildProcessId = childPid,
            ParentProcessId = parent?.Pid ?? 0,
            ParentProcessName = parent?.Name,
            ParentProcessPath = parent?.Path,
            Ancestors = ancestors,
            Window = ProcessDetails.GetWindowVisibility(),
        };
    }

    private static int ParseDepth() =>
        int.TryParse(Environment.GetEnvironmentVariable(DepthEnv), out int d) ? d : 0;

    private static string SafeFileName(string path)
    {
        try
        {
            return Path.GetFileName(path).ToLowerInvariant();
        }
        catch
        {
            return path.ToLowerInvariant();
        }
    }

    private static string SafeCwd()
    {
        try
        {
            return Environment.CurrentDirectory;
        }
        catch
        {
            return "";
        }
    }

    private static string QuoteIfNeeded(string path) =>
        path.Contains(' ', StringComparison.Ordinal) && !path.StartsWith('"') ? $"\"{path}\"" : path;

    private static void TryWriteEvent(string root, AuditEvent ev)
    {
        try
        {
            AuditStore.WriteEvent(root, ev);
        }
        catch
        {
            // audit is best-effort; never break the launch
        }
    }

    private static void TryWriteStdErr(string message)
    {
        try
        {
            Console.Error.WriteLine(message);
        }
        catch
        {
            // ignore
        }
    }
}
