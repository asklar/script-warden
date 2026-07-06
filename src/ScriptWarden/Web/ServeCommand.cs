using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ScriptWarden.Analysis;
using ScriptWarden.Core;

namespace ScriptWarden.Web;

/// <summary>Starts the local audit web viewer and serves its JSON API + embedded SPA.</summary>
internal static partial class ServeCommand
{
    private const string ResourceName = "ScriptWarden.WebUI.index.html";
    private static readonly HashSet<string> ValueKeys = new(StringComparer.OrdinalIgnoreCase) { "port" };

    public static int Run(string[] args)
    {
        CliOptions opts = CliOptions.Parse(args, 1, ValueKeys);
        int port = 8787;
        if (opts.Get("port") is { } p && int.TryParse(p, out int parsed) && parsed is > 0 and < 65536)
        {
            port = parsed;
        }
        bool open = !opts.Has("no-open");
        bool verbose = opts.Has("verbose") || opts.Has("v");

        string url = $"http://127.0.0.1:{port}/";

        // --verbose traces connection + request + shutdown activity to stderr, so you can see exactly
        // what the viewer is doing (e.g. that Ctrl+C was received and the accept loop exited).
        Action<string>? log = verbose
            ? m => Console.Error.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] serve: {m}")
            : null;

        // Bind + start listening BEFORE opening the browser, so the first navigation always lands on
        // a live server (previously the browser could open before the listener was up → blank/404).
        var server = new HttpServer(port, Route, log);
        try
        {
            server.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"script-warden: viewer failed to start on port {port}: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"script-warden viewer listening at {url}");
        foreach (ResolvedRoot root in DataRoots.ForViewer())
        {
            string state = root.Readable ? (root.Exists ? "ok" : "absent") : "NOT READABLE";
            Console.WriteLine($"  {root.Origin,-12} {root.Path}  [{state}]");
        }
        Console.WriteLine("Press Ctrl+C to stop.");

        // Handle Ctrl+C / Ctrl+Break / console-close via the Win32 control handler rather than
        // Console.CancelKeyPress (the .NET handler initializes console input with side effects).
        _activeServer = server;
        _ctrlLog = log;
        unsafe { SetConsoleCtrlHandler(&HandleConsoleCtrl, 1); }

        // Shells with a line editor (pwsh/PSReadLine) clear ENABLE_PROCESSED_INPUT so they can handle
        // Ctrl+C as input themselves; a child console app inherits that mode, so Ctrl+C arrives as a
        // 0x03 *keystroke* (queued in the input buffer) instead of a CTRL_C_EVENT — and since we never
        // read input, it's ignored until we exit. Re-enable processed input so Ctrl+C fires our
        // handler, and restore quick-edit so click-drag text selection works again.
        EnsureConsoleCtrlAndSelection(log);

        if (open)
        {
            TryOpenBrowser(url);
        }

        // Prime the newest events synchronously for a fast first paint, then index the rest (and
        // keep watching for new events) on a background thread so `serve` responds immediately even
        // with a large audit trail.
        Cache.Prime(200);
        Cache.Start();

        server.AcceptLoop();
        return 0;
    }

    private static HttpServer? _activeServer;
    private static Action<string>? _ctrlLog;
    private static int _interrupts;
    private static int _watchdogArmed;

    private const int StdInputHandle = -10;
    private const uint EnableProcessedInput = 0x0001;
    private const uint EnableQuickEditMode = 0x0040;
    private const uint EnableExtendedFlags = 0x0080;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern unsafe int SetConsoleCtrlHandler(delegate* unmanaged[Stdcall]<uint, int> handler, int add);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int GetConsoleMode(nint handle, out uint mode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int SetConsoleMode(nint handle, uint mode);

    [DllImport("kernel32.dll")]
    private static extern nint GetCurrentProcess();

    [DllImport("kernel32.dll")]
    private static extern int TerminateProcess(nint handle, uint exitCode);

    private static void EnsureConsoleCtrlAndSelection(Action<string>? log)
    {
        try
        {
            nint hIn = GetStdHandle(StdInputHandle);
            if (hIn == 0 || hIn == -1 || GetConsoleMode(hIn, out uint mode) == 0)
            {
                return; // no console (input redirected) — nothing to fix
            }
            uint desired = mode | EnableProcessedInput | EnableExtendedFlags | EnableQuickEditMode;
            if (desired != mode && SetConsoleMode(hIn, desired) != 0)
            {
                log?.Invoke($"console input mode 0x{mode:x} -> 0x{desired:x} (Ctrl+C as signal, quick-edit on)");
            }
        }
        catch
        {
            // best-effort; never block startup on console tweaks
        }
    }

    // Windows console control types: CTRL_C=0, CTRL_BREAK=1, CTRL_CLOSE=2, CTRL_LOGOFF=5, CTRL_SHUTDOWN=6.
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int HandleConsoleCtrl(uint ctrlType)
    {
        int n = Interlocked.Increment(ref _interrupts);
        // A second Ctrl+C hard-kills immediately.
        if (n >= 2)
        {
            TerminateProcess(GetCurrentProcess(), 0);
            return 1;
        }
        // Stop FIRST, before any console I/O (a paused/quick-edit-selected console can block writes,
        // which must never prevent shutdown). Arm a watchdog that force-exits if the main thread is
        // wedged, so Ctrl+C is guaranteed to win.
        ArmExitWatchdog();
        _activeServer?.Stop();
        _ctrlLog?.Invoke($"console control event {ctrlType} received (#{n}); stopping");
        return 1; // handled — we own the shutdown (graceful, or the watchdog)
    }

    private static void ArmExitWatchdog()
    {
        if (Interlocked.Exchange(ref _watchdogArmed, 1) != 0)
        {
            return;
        }
        var t = new Thread(static () =>
        {
            // Graceful shutdown is normally a few ms; if we're still alive after the grace period the
            // main thread is stuck (e.g. blocked on a paused console), so force the process to exit.
            Thread.Sleep(1200);
            TerminateProcess(GetCurrentProcess(), 0);
        })
        { IsBackground = true, Name = "sw-shutdown-watchdog" };
        t.Start();
    }

    private static HttpResponse Route(HttpRequest req)
    {
        try
        {
            if (req.Path == "/api/clear")
            {
                return string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase)
                    ? ApiClear()
                    : HttpResponse.Text("Method Not Allowed", 405);
            }

            if (req.Path == "/api/config")
            {
                return string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase)
                    ? ApiSetConfig(req)
                    : ApiGetConfig();
            }

            if (req.Path == "/api/analysis/refresh")
            {
                return string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase)
                    ? ApiAnalysisRefresh()
                    : HttpResponse.Text("Method Not Allowed", 405);
            }

            if (req.Path == "/api/analysis/rollup")
            {
                return string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase)
                    ? ApiAnalysisRollup(req)
                    : HttpResponse.Text("Method Not Allowed", 405);
            }

            if (req.Path == "/api/analysis/events")
            {
                return string.Equals(req.Method, "POST", StringComparison.OrdinalIgnoreCase)
                    ? ApiAnalysisEvents(req)
                    : HttpResponse.Text("Method Not Allowed", 405);
            }

            return req.Path switch
            {
                "/" or "/index.html" => ServeIndex(),
                "/api/status" => ApiStatus(),
                "/api/events" => ApiEvents(req),
                "/api/script" => ApiScript(req),
                "/api/analysis/taxonomies" => ApiAnalysisTaxonomies(),
                _ => HttpResponse.Text("Not Found", 404),
            };
        }
        catch (Exception ex)
        {
            return HttpResponse.Text($"error: {ex.Message}", 500);
        }
    }

    private static HttpResponse ApiClear()
    {
        int events = 0;
        int scripts = 0;
        var cleared = new List<string>();
        foreach (ResolvedRoot root in DataRoots.ForViewer())
        {
            if (!root.Readable)
            {
                continue;
            }
            (int e, int s) = AuditStore.ClearRoot(root.Path);
            events += e;
            scripts += s;
            if (e > 0 || s > 0)
            {
                cleared.Add(root.Origin.ToString());
            }
        }

        var result = new ClearResult { Events = events, Scripts = scripts, Roots = cleared };
        return HttpResponse.Json(JsonSerializer.Serialize(result, ServeJsonContext.Default.ClearResult));
    }

    private static HttpResponse ApiGetConfig()
    {
        WardenConfig config = ConfigStore.Load(DataRoots.CurrentUserRoot());
        return HttpResponse.Json(JsonSerializer.Serialize(config, AuditJsonContext.Default.WardenConfig));
    }

    private static HttpResponse ApiSetConfig(HttpRequest req)
    {
        WardenConfig? config;
        try
        {
            config = JsonSerializer.Deserialize(req.Body, AuditJsonContext.Default.WardenConfig);
        }
        catch (Exception ex)
        {
            return HttpResponse.Text($"invalid config: {ex.Message}", 400);
        }

        if (config is null)
        {
            return HttpResponse.Text("invalid config", 400);
        }

        ConfigStore.Save(DataRoots.CurrentUserRoot(), config);
        return HttpResponse.Json(JsonSerializer.Serialize(config, AuditJsonContext.Default.WardenConfig));
    }

    private static readonly EventCache Cache = new();

    private static HttpResponse ApiStatus()
    {
        List<AuditEvent> events = Cache.Snapshot();
        IReadOnlyList<ResolvedRoot> roots = Cache.Roots;
        AuditFacets facets = AuditQuery.Facets(events);
        var status = new ServeStatus
        {
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            EventCount = events.Count,
            TotalOnDisk = Cache.TotalOnDisk,
            Indexing = Cache.Indexing,
            Roots = roots.Select(r => new RootDto
            {
                Path = r.Path,
                Origin = r.Origin.ToString(),
                Exists = r.Exists,
                Readable = r.Readable,
                Error = r.Error,
            }).ToList(),
            Images = facets.Images,
            Parents = facets.Parents,
            Windows = facets.Windows,
        };
        return HttpResponse.Json(JsonSerializer.Serialize(status, ServeJsonContext.Default.ServeStatus));
    }

    private static HttpResponse ApiEvents(HttpRequest req)
    {
        List<AuditEvent> events = Cache.Snapshot();
        int offset = ParseInt(req.Query.GetValueOrDefault("offset"), 0);
        int limit = Math.Clamp(ParseInt(req.Query.GetValueOrDefault("limit"), 100), 1, 500);

        EventsPage page = AuditQuery.Query(
            events,
            image: req.Query.GetValueOrDefault("image"),
            origin: req.Query.GetValueOrDefault("origin"),
            parent: req.Query.GetValueOrDefault("parent"),
            window: req.Query.GetValueOrDefault("window"),
            search: req.Query.GetValueOrDefault("q"),
            offset: offset,
            limit: limit);

        return HttpResponse.Json(JsonSerializer.Serialize(page, AuditJsonContext.Default.EventsPage));
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out int n) ? n : fallback;

    private static HttpResponse ApiScript(HttpRequest req)
    {
        string sha = req.Query.GetValueOrDefault("sha", "");
        string ext = req.Query.GetValueOrDefault("ext", "");
        string origin = req.Query.GetValueOrDefault("origin", "CurrentUser");
        bool download = req.Query.GetValueOrDefault("download", "") is "1" or "true";

        if (!IsValidScriptReference(sha, ext))
        {
            return HttpResponse.Text("invalid script reference", 400);
        }

        string root = string.Equals(origin, "System", StringComparison.OrdinalIgnoreCase)
            ? DataRoots.SystemRoot()
            : DataRoots.CurrentUserRoot();

        string scriptsDir = Path.GetFullPath(DataRoots.ScriptsDir(root));
        string full = Path.GetFullPath(new ScriptStore(root).ScriptPath(sha, ext));

        // Defense in depth against traversal (sha/ext are already validated).
        if (!IsInside(scriptsDir, full) || !File.Exists(full))
        {
            return HttpResponse.Text("script not found", 404);
        }

        byte[] raw = File.ReadAllBytes(full);
        if (download)
        {
            // Downloads are byte-exact (forensic copy): serve raw bytes untouched.
            return new HttpResponse
            {
                Status = 200,
                ContentType = "application/octet-stream",
                Body = raw,
                ContentDisposition = $"attachment; filename=\"{sha}{ext}\"",
            };
        }

        // For inline viewing, decode from the content's real encoding (UTF-16 with or without a
        // BOM is common for management-tooling scripts) and re-emit as UTF-8 so it renders correctly.
        byte[] text = Encoding.UTF8.GetBytes(ScriptText.DecodeToText(raw));
        return new HttpResponse
        {
            Status = 200,
            ContentType = "text/plain; charset=utf-8",
            Body = text,
        };
    }

    /// <summary>Validates a script reference: SHA-256 hex (64 chars) and a simple dotted extension.</summary>
    internal static bool IsValidScriptReference(string sha, string ext) =>
        ShaPattern().IsMatch(sha) && ExtPattern().IsMatch(ext);

    /// <summary>True if <paramref name="candidate"/> is within <paramref name="directory"/>.</summary>
    internal static bool IsInside(string directory, string candidate) =>
        candidate.StartsWith(directory, StringComparison.OrdinalIgnoreCase);

    // ---- analysis (data-driven taxonomies over analysis.db; the Analysis tab) ----

    private static readonly AnalysisService Analysis = new();

    private static HttpResponse ApiAnalysisRefresh()
    {
        RefreshResponse result = Analysis.Refresh();
        return HttpResponse.Json(JsonSerializer.Serialize(result, AnalysisApiJsonContext.Default.RefreshResponse));
    }

    private static HttpResponse ApiAnalysisTaxonomies()
    {
        List<TaxonomyInfoDto> taxonomies = Analysis.Taxonomies();
        return HttpResponse.Json(JsonSerializer.Serialize(taxonomies, AnalysisApiJsonContext.Default.ListTaxonomyInfoDto));
    }

    private static HttpResponse ApiAnalysisRollup(HttpRequest req)
    {
        AnalysisRequest request = ParseAnalysisRequest(req.Body);
        RollupResponse result = Analysis.Rollup(request);
        return HttpResponse.Json(JsonSerializer.Serialize(result, AnalysisApiJsonContext.Default.RollupResponse));
    }

    private static HttpResponse ApiAnalysisEvents(HttpRequest req)
    {
        AnalysisRequest request = ParseAnalysisRequest(req.Body);
        (int total, List<AuditEvent> events) = Analysis.Drill(request);
        var page = new EventsPage { Total = total, Offset = request.Offset, Limit = request.Limit, Events = events };
        return HttpResponse.Json(JsonSerializer.Serialize(page, AuditJsonContext.Default.EventsPage));
    }

    private static AnalysisRequest ParseAnalysisRequest(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return new AnalysisRequest();
        }
        try
        {
            return JsonSerializer.Deserialize(body, AnalysisApiJsonContext.Default.AnalysisRequest) ?? new AnalysisRequest();
        }
        catch
        {
            return new AnalysisRequest();
        }
    }

    private static HttpResponse ServeIndex()
    {
        using Stream? stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
        if (stream is not null)
        {
            using var reader = new StreamReader(stream);
            return HttpResponse.Html(reader.ReadToEnd());
        }
        return HttpResponse.Html(FallbackHtml);
    }

    private static void TryOpenBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch
        {
            Console.WriteLine($"(open {url} in your browser)");
        }
    }

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex ShaPattern();

    [GeneratedRegex(@"^\.[A-Za-z0-9]{1,10}$")]
    private static partial Regex ExtPattern();

    // Minimal built-in viewer used when the React SPA has not been embedded (dev builds without the
    // web bundle). The production build embeds the Fluent UI SPA and this is never shown.
    private const string FallbackHtml = """
        <!doctype html><html><head><meta charset="utf-8"><title>script-warden</title>
        <style>body{font:14px system-ui;margin:1rem;background:#faf9f8}
        table{border-collapse:collapse;width:100%}th,td{border-bottom:1px solid #ddd;padding:6px;text-align:left}
        th{cursor:pointer}code{background:#eee;padding:1px 4px}a{color:#0b5cad}</style></head>
        <body><h2>script-warden — audit trail (fallback view)</h2>
        <div id="notice"></div><input id="q" placeholder="filter..." style="padding:6px;width:280px">
        <p id="count"></p><table id="t"><thead><tr><th>time (UTC)</th><th>image</th><th>origin</th>
        <th>user</th><th>parent</th><th>scripts</th><th>exit</th></tr></thead><tbody></tbody></table>
        <script>
        let all=[];
        async function load(){
          const st=await (await fetch('/api/status')).json();
          document.getElementById('notice').innerHTML=st.roots.filter(r=>!r.readable)
            .map(r=>`<p style="color:#a80000">⚠ ${r.origin} root not readable: ${r.path}</p>`).join('');
          all=await (await fetch('/api/events')).json();
          render();
        }
        function render(){
          const q=document.getElementById('q').value.toLowerCase();
          const rows=all.filter(e=>JSON.stringify(e).toLowerCase().includes(q));
          document.getElementById('count').textContent=rows.length+' event(s)';
          document.querySelector('tbody').innerHTML=rows.map(e=>`<tr><td>${e.timestampUtc?.replace('T',' ').slice(0,19)}</td>
            <td>${e.hookedImage||''}</td><td>${e.origin||''}</td><td>${e.user||''}</td><td>${e.parentProcessName||''}</td>
            <td>${(e.scripts||[]).map(s=>s.sha256?`<a href="/api/script?sha=${s.sha256}&ext=${encodeURIComponent(s.extension)}&origin=${e.origin}" target="_blank">${s.kind}</a>`:s.kind).join(', ')}</td>
            <td>${e.exitCode??''}</td></tr>`).join('');
        }
        document.getElementById('q').addEventListener('input',render);
        load();
        </script></body></html>
        """;
}
