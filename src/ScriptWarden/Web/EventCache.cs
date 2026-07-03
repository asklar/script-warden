using ScriptWarden.Core;

namespace ScriptWarden.Web;

/// <summary>
/// Thread-safe in-memory event index for the viewer. <see cref="Prime"/> parses the newest N events
/// synchronously so <c>serve</c> renders immediately; <see cref="Start"/> then indexes the rest — and
/// keeps watching for new/removed files — on a background thread. It exposes progress
/// (<see cref="Indexing"/>, <see cref="IndexedCount"/>, <see cref="TotalOnDisk"/>) so the UI can poll
/// progressively and show results as the cache fills. Event file names begin with a sortable UTC
/// timestamp, so ordering by file name (newest first) surfaces the latest events without parsing
/// everything up front.
/// </summary>
internal sealed class EventCache
{
    private readonly object _lock = new();
    private readonly Dictionary<string, AuditEvent> _events = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _attempted = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<ResolvedRoot> _roots = [];
    private int _totalOnDisk;
    private volatile bool _caughtUp;
    private volatile bool _running;

    public IReadOnlyList<ResolvedRoot> Roots
    {
        get { lock (_lock) { return _roots; } }
    }

    public int IndexedCount
    {
        get { lock (_lock) { return _events.Count; } }
    }

    public int TotalOnDisk
    {
        get { lock (_lock) { return _totalOnDisk; } }
    }

    /// <summary>True while the background indexer has not yet caught up with the files on disk.</summary>
    public bool Indexing => !_caughtUp;

    public List<AuditEvent> Snapshot()
    {
        lock (_lock)
        {
            return _events.Values.ToList();
        }
    }

    /// <summary>Parses the newest <paramref name="max"/> events synchronously for a fast first paint.</summary>
    public void Prime(int max)
    {
        List<FileRef> files = Enumerate(out IReadOnlyList<ResolvedRoot> roots);
        lock (_lock)
        {
            _roots = roots;
            _totalOnDisk = files.Count;
        }
        foreach (FileRef f in files.OrderByDescending(f => f.Name, StringComparer.Ordinal).Take(max))
        {
            AddFile(f);
        }
    }

    /// <summary>Starts background indexing of remaining events and periodic reconciliation with disk.</summary>
    public void Start()
    {
        if (_running)
        {
            return;
        }
        _running = true;
        var worker = new Thread(Loop) { IsBackground = true, Name = "sw-index" };
        worker.Start();
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                Reconcile();
            }
            catch
            {
                // best-effort; retry next tick
            }
            Thread.Sleep(2000);
        }
    }

    private void Reconcile()
    {
        List<FileRef> files = Enumerate(out IReadOnlyList<ResolvedRoot> roots);
        var current = new HashSet<string>(files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);

        var toParse = new List<FileRef>();
        lock (_lock)
        {
            _roots = roots;
            _totalOnDisk = files.Count;
            foreach (string stale in _events.Keys.Where(k => !current.Contains(k)).ToList())
            {
                _events.Remove(stale);
            }
            _attempted.RemoveWhere(p => !current.Contains(p));

            foreach (FileRef f in files)
            {
                if (!_attempted.Contains(f.Path))
                {
                    toParse.Add(f);
                }
            }
        }

        if (toParse.Count > 0)
        {
            _caughtUp = false;
            foreach (FileRef f in toParse.OrderByDescending(f => f.Name, StringComparer.Ordinal))
            {
                AddFile(f);
            }
        }

        _caughtUp = true;
    }

    private void AddFile(FileRef f)
    {
        AuditEvent? ev = AuditStore.ReadEventFile(f.Path, f.Origin);
        lock (_lock)
        {
            _attempted.Add(f.Path);
            if (ev is not null)
            {
                _events[f.Path] = ev;
            }
        }
    }

    private static List<FileRef> Enumerate(out IReadOnlyList<ResolvedRoot> roots)
    {
        roots = DataRoots.ForViewer();
        var list = new List<FileRef>();
        foreach (ResolvedRoot root in roots)
        {
            if (!root.Readable)
            {
                continue;
            }
            string dir = DataRoots.EventsDir(root.Path);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            try
            {
                foreach (string file in Directory.EnumerateFiles(dir, "*.json"))
                {
                    list.Add(new FileRef(file, Path.GetFileName(file), root.Origin));
                }
            }
            catch
            {
                // ignore unreadable roots
            }
        }
        return list;
    }

    private readonly record struct FileRef(string Path, string Name, AuditOrigin Origin);
}
