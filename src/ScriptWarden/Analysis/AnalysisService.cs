using ScriptWarden.Core;
using ScriptWarden.Core.Analysis;

namespace ScriptWarden.Analysis;

/// <summary>
/// Serve-side wrapper around <see cref="AnalysisStore"/> for the viewer's Analysis tab. The store is
/// opened lazily and reused; <see cref="Refresh"/> is the explicit user-initiated gesture (ingest +
/// enrich), reloading taxonomies each time so edits are picked up.
/// </summary>
internal sealed class AnalysisService : IDisposable
{
    private readonly object _lock = new();
    private AnalysisStore? _store;

    private AnalysisStore Store()
    {
        lock (_lock)
        {
            if (_store is null)
            {
                var store = new AnalysisStore(AnalysisStore.DefaultDbPath());
                store.Open();
                _store = store;
            }
            return _store;
        }
    }

    public RefreshResponse Refresh()
    {
        List<Taxonomy> taxonomies = TaxonomyStore.Load(DataRoots.CurrentUserRoot());
        var content = new ScriptContentProvider(DataRoots.ForViewer());
        AnalysisStore.RefreshResult r = Store().Refresh(taxonomies, content);
        return new RefreshResponse { Ingested = r.Ingested, Total = r.Total, Reclassified = r.Reclassified };
    }

    public List<TaxonomyInfoDto> Taxonomies() =>
        TaxonomyStore.Load(DataRoots.CurrentUserRoot())
            .Select(t => new TaxonomyInfoDto
            {
                Id = t.Id,
                Name = t.Name,
                MultiLabel = t.MultiLabel,
                Labels = t.Rules.Select(r => r.Label).Where(l => !string.IsNullOrEmpty(l)).Distinct().ToList(),
            })
            .ToList();

    public RollupResponse Rollup(AnalysisRequest req)
    {
        AnalysisStore store = Store();
        List<AnalysisStore.RollupRow> rows = store.Rollup(req.GroupBy, req.Filters);
        return new RollupResponse
        {
            Taxonomy = req.GroupBy,
            TotalEvents = store.Count(),
            MatchedEvents = store.MatchCount(req.Filters),
            Rows = rows.Select(r => new RollupRowDto { Label = r.Label, Count = r.Count, TotalMs = r.TotalMs }).ToList(),
        };
    }

    public (int Total, List<AuditEvent> Events) Drill(AnalysisRequest req) =>
        Store().DrillEvents(req.GroupBy, req.DrillLabel, req.Filters, req.Offset, req.Limit);

    public void Dispose()
    {
        lock (_lock)
        {
            _store?.Dispose();
            _store = null;
        }
    }
}
