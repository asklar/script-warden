# Security

## Security model

script-warden installs a machine-wide **IFEO `Debugger`** hook (HKLM) for the configured
interpreters, so its shim runs in place of those interpreters on every launch. Design constraints
that keep this safe:

- The shim runs **as the invoker** (`asInvoker` manifest) — it never elevates and never prompts for
  UAC on a launch. Only `install`/`uninstall` require and request elevation.
- The capture path is **fail-open**: any error while auditing still lets the real interpreter run,
  so a bug cannot block the hooked shells.
- The real interpreter is relaunched with `DEBUG_ONLY_THIS_PROCESS` purely to bypass IFEO
  redirection, then immediately detached, so it runs with normal semantics (no attached debugger).
- The web viewer binds to **127.0.0.1 only**, serves read-only data, validates script identifiers
  (SHA-256 + extension), and guards against path traversal.

## Considerations before installing

- Hooks are **machine-wide** and affect every user on the host. Test on a spare machine or VM first.
- Captured scripts and command lines may contain sensitive data (paths, arguments, inline secrets).
  Audit data is stored under the user profile; protect it accordingly.

## Reporting a vulnerability

Please report suspected vulnerabilities privately via GitHub's
[private vulnerability reporting](https://docs.github.com/code-security/security-advisories/guidance-on-reporting-and-writing/privately-reporting-a-security-vulnerability)
for this repository, rather than opening a public issue.
