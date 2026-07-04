using System.Text.Json.Serialization;

namespace ScriptWarden.Analysis;

/// <summary>One grouped row of an analysis rollup (a taxonomy label with its measures).</summary>
public sealed class RollupRowDto
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public long TotalMs { get; set; }
}

/// <summary>A full rollup response: the grouping plus the total events analyzed (for percentages).</summary>
public sealed class RollupResponse
{
    public string Taxonomy { get; set; } = "";
    public int TotalEvents { get; set; }
    public List<RollupRowDto> Rows { get; set; } = [];
}

/// <summary>Result of the refresh (ingest + enrich) gesture.</summary>
public sealed class RefreshResponse
{
    public int Ingested { get; set; }
    public int Total { get; set; }
    public bool Reclassified { get; set; }
}

/// <summary>A taxonomy's identity for the picker.</summary>
public sealed class TaxonomyInfoDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool MultiLabel { get; set; }
}

/// <summary>System.Text.Json source-gen context for the analysis API/CLI DTOs (AOT-safe).</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(List<RollupRowDto>))]
[JsonSerializable(typeof(RollupResponse))]
[JsonSerializable(typeof(RefreshResponse))]
[JsonSerializable(typeof(List<TaxonomyInfoDto>))]
public partial class AnalysisApiJsonContext : JsonSerializerContext
{
}
