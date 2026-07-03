# script-warden

**See and review every script that runs on your Windows machine — including the ones you never
started yourself.**

On a managed PC, your IT department, device-management tooling, and background automation
constantly run scripts through PowerShell, `cmd`, and the Windows Script Host — usually silently,
with no window, and gone before you can look. script-warden gives you a durable record: what ran,
when, who started it, whether it was visible or hidden, and **a saved copy of the actual script**,
all browsable in a local web viewer.

## Why you'd want this

- **Know what's running on your own machine.** Logon scripts, scheduled tasks, config-management
  agents, and remote-management tools push scripts constantly. script-warden captures them so you
  can actually read what executed.
- **Keep the evidence.** Even a script that deletes itself, decodes an inline command, or runs for
  a few milliseconds in the background is copied and kept, content-addressed and de-duplicated.
- **Spot the silent stuff.** Every launch is tagged **Visible** or **Hidden**, so you can filter to
  exactly the background scripts that ran with no console.
- **Trace where it came from.** Each launch records its full parent chain — from the script all the
  way up to the management agent or scheduled-task host that kicked it off.
- **Stay out of your way.** Interpreters keep working exactly as before; capturing is transparent
  and fail-open, so nothing you run breaks even if logging can't.

## What it captures

For every launch of `powershell.exe`, `cmd.exe`, `pwsh.exe`, `cscript.exe`, and `wscript.exe`:

- The full, verbatim command line, working directory, user, and timestamp.
- **The script itself** — referenced `.ps1`/`.bat`/`.cmd`/`.vbs`/`.js` files are copied; inline
  `-Command` / `cmd /c` text is saved; `-EncodedCommand` payloads are decoded and saved as readable
  scripts.
- Whether it ran **Visible** (had a console) or **Hidden** (no window / background).
- How long it ran and its exit code.
- The complete process ancestry that led to the launch.

## The viewer

`script-warden serve` opens a local web app (React + Fluent UI) to browse everything it has
captured:

- Fast, paginated, searchable audit trail with filters by interpreter, origin, parent process, and
  visibility.
- Click any entry for the full command line, the launch chain, and the captured script (view or
  download).
- Auto-refresh, light/dark/system themes, and a settings page to pause recording or ignore noisy
  sources (e.g. your own tooling).
- Reads both your per-user data and the machine's SYSTEM-level data, so you can see scripts that ran
  with full privileges too.

## Quick start

Grab `script-warden.exe` (a single self-contained file — no runtime to install), then:

```powershell
script-warden install      # start monitoring (prompts for admin; machine-wide)
script-warden serve        # open the web viewer in your browser
script-warden status       # see what's being monitored
script-warden uninstall    # stop monitoring
```

## Commands

```
script-warden install [--images a,b] [--exe-path DIR] [--no-copy]
script-warden uninstall [--images a,b]
script-warden status
script-warden serve [--port N] [--no-open]
script-warden list [--json] [--image NAME] [--since DATE] [--limit N]
script-warden diagnose
```

- **install** — begins monitoring the script hosts machine-wide (self-elevates). `--images` limits
  which interpreters; `--no-copy` runs from the current exe location instead of copying to Program
  Files.
- **uninstall** — stops monitoring (removes only what script-warden added; never touches another
  tool's settings).
- **status** — shows which interpreters are currently monitored.
- **serve** — starts the localhost viewer and opens a browser.
- **list** — prints recent activity to the console (`--json` for scripting).
- **diagnose** — self-test: resolved data locations + write access, current monitoring state, and a
  simulated capture of representative command lines. Run this first if something looks off.

## Where data lives

Per-user, under `%LOCALAPPDATA%\script-warden` (override with `SCRIPT_WARDEN_DATA`):

```
events\<utc-timestamp>-<pid>-<id>.json   one file per launch (lock-free)
scripts\<sha256>.<ext>                   captured scripts, de-duplicated by content
```

Scripts that run as **SYSTEM** are captured under
`C:\Windows\System32\config\systemprofile\AppData\Local\script-warden`. The viewer reads both
locations; reading the SYSTEM location generally requires elevation, so the viewer runs best-effort
and flags it in the UI when it can't.

---

## Under the hood

script-warden is a single **.NET 10 Native AOT** executable — fast to start (it runs on *every*
interpreter launch) with no runtime dependency.

It works by registering itself as the **[Image File Execution Options][ifeo] `Debugger`** for each
monitored interpreter. When an IFEO `Debugger` value is set for `image.exe`, Windows launches
`<debugger> "<full path to image.exe>" <original args>` instead of the image. script-warden then:

1. Records the launch metadata and captures any referenced or inline script.
2. Relaunches the real interpreter with `CreateProcess(DEBUG_ONLY_THIS_PROCESS)` and immediately
   detaches. The debug flag makes the loader **skip** the IFEO redirection — otherwise the relaunch
   would re-invoke script-warden forever — and detaching lets the child run with completely normal
   semantics. stdio, working directory, environment, and exit code are all forwarded, and the
   original command line is passed through verbatim (via `GetCommandLineW`, to preserve exact
   quoting under AOT).
3. Everything on the capture path is **fail-open**: if auditing throws, the interpreter still runs.

The shim manifest is `asInvoker`, so it never triggers UAC on a normal launch; only
`install`/`uninstall` need admin, and they self-elevate. All interop uses `[LibraryImport]` source
generators and all JSON uses `System.Text.Json` source generators, so the build is fully AOT-clean.

[ifeo]: https://learn.microsoft.com/en-us/windows-hardware/drivers/debugger/debugging-a-launched-process

## Build

Requires the .NET 10 SDK, Node 20+, and the MSVC C++ toolchain (for Native AOT linking).

```powershell
# Web UI (produces web/dist/index.html, embedded into the exe)
cd web; npm ci; npm run build; cd ..

# Native AOT single-file exe -> src/ScriptWarden/bin/Release/net10.0-windows/win-x64/publish/
dotnet publish src/ScriptWarden/ScriptWarden.csproj -c Release -r win-x64
```

If the AOT link step reports `'vswhere.exe' is not recognized`, add the VS Installer directory
(`%ProgramFiles(x86)%\Microsoft Visual Studio\Installer`) to `PATH` and republish.

```powershell
dotnet test          # unit + CLI tests
```

## Notes / caveats

- The IFEO `Debugger` value lives in HKLM, so monitoring is **machine-wide** and affects all users.
- A broken shim would affect every launch of the monitored interpreters, so the shim is heavily
  guarded and `uninstall` only removes its own values. Verify with a spare/test image first if you
  are cautious.
- The `DEBUG_ONLY_THIS_PROCESS` bypass is skipped only if the shim's process has the (rare) global
  `FLG_ENABLE_EXEC_OPTIONS` flag set; a depth-guard env var bounds any pathological loop.
