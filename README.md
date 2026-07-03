# script-warden

Audit the scripts your IT department (or anything else) runs through Windows interpreters.

script-warden registers itself as the **IFEO `Debugger`** for `powershell.exe`, `cmd.exe`,
`pwsh.exe`, `cscript.exe`, and `wscript.exe`. Every time one of them launches, Windows runs
script-warden instead; it records the launch, keeps a copy of any referenced or inline script, then
transparently relaunches the real interpreter. A local web viewer lets you browse the trail.

## How it works

When an IFEO `Debugger` value is set for `image.exe`, Windows launches
`<debugger> "<full path to image.exe>" <original args>` instead of the image. script-warden:

1. Records metadata (command line, parent process, user, time) and captures any script.
2. Relaunches the real interpreter with `CreateProcess(DEBUG_ONLY_THIS_PROCESS)` and immediately
   detaches. The debug flag makes the loader **skip** the IFEO `Debugger` redirection (otherwise the
   relaunch would re-invoke script-warden forever), and detaching lets the child run with completely
   normal semantics. stdio, working directory, environment, and exit code are all forwarded.
3. Everything on the capture path is **fail-open**: if auditing fails, the interpreter still runs.

The shim manifest is `asInvoker` (no UAC on every launch). Only `install`/`uninstall` need admin;
they self-elevate.

## Commands

```
script-warden install [--images a,b] [--exe-path DIR] [--no-copy]
script-warden uninstall [--images a,b]
script-warden status
script-warden serve [--port N] [--no-open]
script-warden list [--json] [--image NAME] [--since DATE] [--limit N]
script-warden diagnose
```

- **install** — copies the exe to `%ProgramFiles%\script-warden` and writes the IFEO hooks
  (64-bit + WOW6432Node). `--no-copy` registers the current exe path in place.
- **uninstall** — removes only the `Debugger` values that point at script-warden.
- **status** — shows which images are hooked.
- **serve** — starts the localhost viewer and opens a browser.
- **list** — prints recent events to the console.
- **diagnose** — self-test: resolved data roots + write access, live IFEO values, and a simulated
  capture of representative command lines. Run this first if something looks off.

## Data

Per-user, under `%LOCALAPPDATA%\script-warden` (override with `SCRIPT_WARDEN_DATA`):

```
events\<utc-timestamp>-<pid>-<id>.json   one file per launch (lock-free)
scripts\<sha256>.<ext>                   captured scripts, de-duplicated by content
```

Launches that run as **SYSTEM** land under
`C:\Windows\System32\config\systemprofile\AppData\Local\script-warden`. The viewer reads both the
current-user and SYSTEM roots; reading the SYSTEM root generally requires elevation, so `serve`/`list`
run best-effort and flag an unreadable SYSTEM root in the UI.

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
dotnet test          # Core unit tests
```

## Notes / caveats

- IFEO `Debugger` is machine-wide (HKLM). Install affects all users on the machine.
- A broken shim would affect every launch of the hooked interpreters, so the shim is heavily
  guarded and `uninstall` only removes its own values. Verify with a spare/test image first if you
  are cautious.
- The `DEBUG_ONLY_THIS_PROCESS` bypass is skipped only if the shim's process has the (rare) global
  `FLG_ENABLE_EXEC_OPTIONS` flag set; a depth-guard env var bounds any pathological loop.
