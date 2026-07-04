using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScriptWarden.Core.Analysis;

/// <summary>
/// System.Text.Json source-generation context for the data-driven taxonomy schema, so loading/saving
/// taxonomy JSON never relies on reflection (required for Native AOT).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Taxonomy))]
[JsonSerializable(typeof(List<Taxonomy>))]
public partial class AnalysisJsonContext : JsonSerializerContext
{
}
