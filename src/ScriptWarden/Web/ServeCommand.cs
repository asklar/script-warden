using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
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

        string url = $"http://127.0.0.1:{port}/";
        Console.WriteLine($"script-warden viewer listening at {url}");
        foreach (ResolvedRoot root in DataRoots.ForViewer())
        {
            string state = root.Readable ? (root.Exists ? "ok" : "absent") : "NOT READABLE";
            Console.WriteLine($"  {root.Origin,-12} {root.Path}  [{state}]");
        }
        Console.WriteLine("Press Ctrl+C to stop.");

        if (open)
        {
            TryOpenBrowser(url);
        }

        try
        {
            new HttpServer(port, Route).Run();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"script-warden: viewer failed to start on port {port}: {ex.Message}");
            return 1;
        }
        return 0;
    }

    private static HttpResponse Route(HttpRequest req)
    {
        try
        {
            return req.Path switch
            {
                "/" or "/index.html" => ServeIndex(),
                "/api/status" => ApiStatus(),
                "/api/events" => ApiEvents(),
                "/api/script" => ApiScript(req),
                _ => HttpResponse.Text("Not Found", 404),
            };
        }
        catch (Exception ex)
        {
            return HttpResponse.Text($"error: {ex.Message}", 500);
        }
    }

    private static HttpResponse ApiStatus()
    {
        List<AuditEvent> events = AuditStore.ReadAllForViewer(out IReadOnlyList<ResolvedRoot> roots);
        var status = new ServeStatus
        {
            Version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
            EventCount = events.Count,
            Roots = roots.Select(r => new RootDto
            {
                Path = r.Path,
                Origin = r.Origin.ToString(),
                Exists = r.Exists,
                Readable = r.Readable,
                Error = r.Error,
            }).ToList(),
        };
        return HttpResponse.Json(JsonSerializer.Serialize(status, ServeJsonContext.Default.ServeStatus));
    }

    private static HttpResponse ApiEvents()
    {
        List<AuditEvent> events = AuditStore.ReadAllForViewer(out _);
        return HttpResponse.Json(JsonSerializer.Serialize(events, AuditJsonContext.Default.ListAuditEvent));
    }

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

        byte[] body = File.ReadAllBytes(full);
        var response = new HttpResponse
        {
            Status = 200,
            ContentType = download ? "application/octet-stream" : "text/plain; charset=utf-8",
            Body = body,
        };
        if (download)
        {
            response.ContentDisposition = $"attachment; filename=\"{sha}{ext}\"";
        }
        return response;
    }

    /// <summary>Validates a script reference: SHA-256 hex (64 chars) and a simple dotted extension.</summary>
    internal static bool IsValidScriptReference(string sha, string ext) =>
        ShaPattern().IsMatch(sha) && ExtPattern().IsMatch(ext);

    /// <summary>True if <paramref name="candidate"/> is within <paramref name="directory"/>.</summary>
    internal static bool IsInside(string directory, string candidate) =>
        candidate.StartsWith(directory, StringComparison.OrdinalIgnoreCase);

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
