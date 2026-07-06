using System.Text.Json.Serialization;

namespace ScriptWarden.Analysis;

/// <summary>One grouped row of an analysis rollup (a taxonomy label with its measures).</summary>
public sealed class RollupRowDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public long TotalMs { get; set; }
}

/// <summary>A full rollup response: the grouping plus total + matched event counts (for percentages).</summary>
public sealed class RollupResponse
{
    public string Taxonomy { get; set; } = "";
    public int TotalEvents { get; set; }
    public int MatchedEvents { get; set; }
    public List<RollupRowDto> Rows { get; set; } = [];
}

/// <summary>One filter predicate. Predicates are AND-ed; within a taxonomy predicate labels are OR-ed.</summary>
public sealed class AnalysisFilter
{
    /// <summary>"taxonomy" | "time" | "duration" | "content" | "parent".</summary>
    public string Type { get; set; } = "";

    // taxonomy
    public string? Taxonomy { get; set; }
    public string? Op { get; set; } // "include" | "exclude"
    public List<string>? Labels { get; set; }

    // time (epoch ms)
    public long? SinceMs { get; set; }
    public long? UntilMs { get; set; }

    // duration (ms)
    public long? MinDurationMs { get; set; }

    // content (FTS)
    public string? Query { get; set; }

    // parent (immediate parent process names to include, OR-ed)
    public List<string>? Values { get; set; }
}

/// <summary>A viewer analysis request: group by a taxonomy, constrained by an AND-ed filter set.</summary>
public sealed class AnalysisRequest
{
    public string GroupBy { get; set; } = "source";
    public List<AnalysisFilter> Filters { get; set; } = [];

    // Drill: also constrain to events with this label in the group-by taxonomy.
    public string? DrillLabel { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; } = 100;
}

/// <summary>Result of the refresh (ingest + enrich) gesture.</summary>
public sealed class RefreshResponse
{
    public int Ingested { get; set; }
    public int Total { get; set; }
    public bool Reclassified { get; set; }
}

/// <summary>A taxonomy's identity + its label set (for filter pickers).</summary>
public sealed class TaxonomyInfoDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool MultiLabel { get; set; }
    public List<string> Labels { get; set; } = [];
}

/// <summary>System.Text.Json source-gen context for the analysis API/CLI DTOs (AOT-safe).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<RollupRowDto>))]
[JsonSerializable(typeof(RollupResponse))]
[JsonSerializable(typeof(RefreshResponse))]
[JsonSerializable(typeof(List<TaxonomyInfoDto>))]
[JsonSerializable(typeof(AnalysisRequest))]
[JsonSerializable(typeof(AnalysisFilter))]
[JsonSerializable(typeof(List<AnalysisFilter>))]
public partial class AnalysisApiJsonContext : JsonSerializerContext
{
}
