using ScriptWarden.Core;

namespace ScriptWarden.Web;

/// <summary>
/// Owns the <see cref="SqliteIndex"/> for the <c>serve</c> process: opens it, keeps it caught up with
/// the JSON events on disk (a background reconcile loop plus <see cref="FileSystemWatcher"/> wakeups),
/// and exposes fast query methods. The viewer reads exclusively from the index — reconcile is the only
/// thing that touches files (ingesting new events and archiving completed ones).
/// </summary>
internal sealed class IndexService : IDisposable
{
    private readonly SqliteIndex _index;
    private readonly List<FileSystemWatcher> _watchers = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly object _stateLock = new();
    private Thread? _worker;
    private volatile bool _running;
    private volatile bool _indexing = true;
    private IReadOnlyList<ResolvedRoot> _roots = [];
    private int _pendingOnDisk;

    public IndexService(string dbPath) => _index = new SqliteIndex(dbPath);

    public bool Indexing => _indexing;
    public int EventCount => _index.Count();

    public int TotalOnDisk
    {
        get { lock (_stateLock) { return _index.Count() + _pendingOnDisk; } }
    }

    public IReadOnlyList<ResolvedRoot> Roots
    {
        get { lock (_stateLock) { return _roots; } }
    }

    public void Open()
    {
        _index.Open();
        RefreshRoots();
    }

    public void Start()
    {
        _running = true;
        _worker = new Thread(Loop) { IsBackground = true, Name = "sw-index" };
        _worker.Start();
        SetupWatchers();
    }

    public SqliteIndex.Page Query(string? image, string? origin, string? parent, string? window, string? search, int offset, int limit)
        => _index.Query(image, origin, parent, window, search, offset, limit);

    public List<string> Distinct(string column) => _index.Distinct(column);

    public void ClearRows() => _index.ClearRows();

    /// <summary>Runs one reconcile immediately (used by the clear endpoint to reflect changes at once).</summary>
    public void ReconcileNow()
    {
        try { _index.Reconcile(); } catch { /* best-effort */ }
        RefreshRoots();
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                RefreshRoots();
                _indexing = true;
                _index.Reconcile();
            }
            catch
            {
                // best-effort; retry next tick
            }
            finally
            {
                _indexing = false;
            }
            // Periodic safety net; woken immediately by the file watchers on change.
            _wake.WaitOne(TimeSpan.FromSeconds(3));
        }
    }

    private void SetupWatchers()
    {
        foreach (ResolvedRoot root in _roots)
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
                var w = new FileSystemWatcher(dir, "*.json")
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true,
                };
                w.Created += (_, _) => _wake.Set();
                w.Changed += (_, _) => _wake.Set();
                w.Renamed += (_, _) => _wake.Set();
                _watchers.Add(w);
            }
            catch
            {
                // A watcher is a best-effort optimization; the periodic loop still catches changes.
            }
        }
    }

    private void RefreshRoots()
    {
        IReadOnlyList<ResolvedRoot> roots = DataRoots.ForViewer();
        int pending = 0;
        foreach (ResolvedRoot r in roots)
        {
            if (!r.Readable)
            {
                continue;
            }
            string dir = DataRoots.EventsDir(r.Path);
            if (!Directory.Exists(dir))
            {
                continue;
            }
            try
            {
                pending += Directory.EnumerateFiles(dir, "*.json").Count();
            }
            catch
            {
                // ignore
            }
        }
        lock (_stateLock)
        {
            _roots = roots;
            _pendingOnDisk = pending;
        }
    }

    public void Dispose()
    {
        _running = false;
        _wake.Set();
        foreach (FileSystemWatcher w in _watchers)
        {
            try { w.Dispose(); } catch { /* ignore */ }
        }
        _index.Dispose();
        _wake.Dispose();
    }
}
