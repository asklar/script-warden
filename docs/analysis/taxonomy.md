# Analysis taxonomy — starting point

> Status: **design starting point** for discussion, not implemented. Lives on the
> `asklar/analysis-taxonomy` worktree/branch. Regex/rule-first; models optional later.

## The idea in one line

Open-ended questions ("how much time on IT scripts?", "is anyone installing things without me
knowing?", "what is IT monitoring?") all decompose into **attribute → classify behavior →
group/aggregate**. So we enrich each launch with two derived label families — **source**
(who/what is responsible) and **behavior** (what the script does) — plus an **action object**
(the noun it acts on). Questions then become slices of a labeled cube; the counting is
deterministic and never touches a model's context window.

## Design principles

- **Enrich once, query cheaply.** Label at ingest (per unique script + per event). Aggregation is
  plain group-by afterward.
- **Regex/rules first.** Start with deterministic, local, explainable heuristics. A model is an
  optional later refinement, fed the rule labels as features — not a dependency.
- **Multi-label.** A launch can be both `install` and `network`. Don't force a single bucket.
- **Evidence + confidence on every label**, so findings are auditable and tunable.
- **Cache by content hash.** Behavior labels attach to the *unique script* (sha256) and are reused
  across every launch of it; source attaches per event (from that event's own ancestry).

---

## A. Source / attribution taxonomy

Derived from the ancestor chain (already captured) + `UserSid` + image path (+ signing later).
Multiple sources can apply; evaluate all, keep matches.

| Source label | Signals (any of) |
|---|---|
| **ConfigMgr (SCCM)** | ancestor `ccmexec.exe`; path under `C:\Windows\CCM\` or `…\ccmcache\` |
| **Intune / MDM** | ancestor `IntuneManagementExtension.exe`, `Microsoft.Management.Services.IntuneWindowsAgent.exe`, `deviceenroller.exe`, `omadmclient.exe` |
| **WMI (remote mgmt)** | ancestor `wmiprvse.exe` (SCCM, remote admin, some EDR) |
| **Group Policy** | ancestor `gpscript.exe` |
| **Scheduled task** | ancestor `taskhostw.exe`, `taskeng.exe`, or `svchost.exe` hosting the Schedule service |
| **Windows servicing** | ancestor `TrustedInstaller.exe`, `TiWorker.exe`, `usoclient.exe`, `wuauclt.exe` |
| **Logon / session init** | ancestor `userinit.exe`, `winlogon.exe` |
| **EDR / AV** | ancestor known security agents (`MsMpEng`, `SenseIR`, `CSFalconService`, …) |
| **Interactive (me)** | ancestor `explorer.exe` / `WindowsTerminal.exe` / interactive shell; `Visibility=Visible`; non-zero session |
| **Dev tools (mine)** | ancestor `Code.exe`, `devenv.exe`, `copilot.exe`, `git.exe`, `node.exe` |
| **SYSTEM / machine** | `UserSid = S-1-5-18`; session 0 |
| **Unknown** | fallthrough |

**"From my IT dept"** = a user-definable named segment, default = union of {ConfigMgr, Intune, WMI,
Group Policy, Scheduled task, Windows servicing, EDR/AV, SYSTEM}. Define it once, reuse everywhere.

## B. Behavior taxonomy (multi-label)

Classify the command line + decoded/captured script content. Seed rules (illustrative, case-insensitive):

| Behavior | Seed signals |
|---|---|
| **Install** | `Add-AppxPackage`, `msiexec\s+/i`, `Install-(Module\|Package)`, `winget install`, `choco install`, `dism .*/add-package`, `\.msi\b`, `setup\.exe` |
| **Uninstall** | `Remove-AppxPackage`, `msiexec\s+/x`, `Uninstall-\w+`, `winget uninstall`, `dism .*/remove-package` |
| **Update / patch** | `wusa`, `usoclient`, `Install-WindowsUpdate`, `\.msu\b` |
| **Inventory / monitor** | `Get-(CimInstance\|WmiObject)`, `Win32_\w+`, `Get-Hotfix`, `Get-ComputerInfo`, `systeminfo`, registry *reads* under `HKLM`, compliance/baseline keywords |
| **Config change** | `Set-ItemProperty`, `reg\s+add`, `New-ItemProperty`, `Set-Service`, `bcdedit`, `netsh`, `Set-\w*Policy` |
| **Persistence** | `schtasks\s+/create`, `New-ScheduledTask`, `CurrentVersion\\Run`, `New-Service`, startup folder |
| **Network / download** | `Invoke-WebRequest`, `irm`, `Net\.WebClient`, `curl`, `bitsadmin`, `Start-BitsTransfer`, **any captured URL** |
| **Remote execution** | `Invoke-Command`, `Enter-PSSession`, `wmic .* process call create`, `psexec` |
| **Credential / security** | `Get-Credential`, `cmdkey`, `certutil`, `Export-\w*Certificate`, `secedit` |
| **Obfuscation** | `-EncodedCommand`, `FromBase64String`, high entropy, heavy backtick/`+`-concatenation, `[char]` building |
| **Cleanup / self-delete** | `Remove-Item` on `$MyInvocation`/temp, `del "%~f0"` |

## C. Action-object extraction

From a matched behavior, pull the target noun so "install **what**?" / "what registry keys does IT
touch?" are answerable:

- **package** ← `-Path …\x.appx`, `msiexec /i "Foo.msi"`, `winget install <id>`
- **service** ← `Set-Service <name>`, `New-Service -Name`
- **registry path** ← `reg add <key>`, `Set-ItemProperty <path>`
- **url host** ← captured `Urls`
- **file path** ← referenced script / `-Path`

Shape: `{ type: package|service|registry|urlHost|file, value: string }`.

## D. How the example questions map

| Question | Query over labels |
|---|---|
| "Time spent on scripts from IT" | `source ∈ IT`; sum `DurationMs`; group by `source` |
| "Anyone installing/uninstalling without my knowledge" | `behavior ∈ {install, uninstall}` **and** `source ∉ {interactive-me, dev-me}`; list with action objects |
| "What behavior is IT monitoring" | `source ∈ IT`; `behavior = inventory/monitor`; group by action object (which WMI classes / registry paths / files) |

## E. Enrichment record (proposed)

Per unique script (cached by sha256):

```jsonc
{
  "sha256": "…",
  "behaviors": ["install", "network"],
  "actionObjects": [{ "type": "package", "value": "code_x64.appx" }],
  "signals": ["Add-AppxPackage", "url:example.test"],
  "confidence": 0.9,
  "firstSeen": "2026-07-04T…Z",
  "engineVersion": 1
}
```

Per event = its own `source[]` (from ancestry) + the referenced scripts' cached behavior labels.
Store alongside events — a small **SQLite** table fits the earlier "responsive/paginated UI" goal
and makes re-runs cheap (skip already-labeled hashes).

## F. Build order (regex-first, incremental)

1. **Source classifier** — pure ancestry/SID/path rules. No model. Highest value, easiest, fully
   deterministic. Ship first.
2. **Behavior classifier v0** — the seed regex table over command line + decoded content.
3. **Action-object extractors** per behavior.
4. **Query/roll-up surface** in the viewer (group-by source × behavior, with drill-in).
5. *(Later, optional)* a small **local** model to refine fuzzy behavior calls and cut false
   positives — fed the rule labels + features, kept local for privacy (scripts can hold secrets).

## G. Open questions

1. Is "IT" the default union above, or fully user-editable per-source?
2. Label at the **unique-script** level (cache by sha256) for behaviors + **per-event** for source —
   agree?
3. Labels in a new **SQLite** DB (aligns with the earlier responsiveness idea) vs. JSON sidecars?
4. Which behaviors matter most to you first? (I'd prioritize install/uninstall + inventory/monitor
   + network, since those map directly to your example questions.)
