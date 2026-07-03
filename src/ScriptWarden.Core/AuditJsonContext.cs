using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScriptWarden.Core;

/// <summary>
/// System.Text.Json source-generation context. All (de)serialization goes through this so the
/// tool never relies on reflection-based serialization (required for Native AOT).
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AuditEvent))]
[JsonSerializable(typeof(CapturedScript))]
[JsonSerializable(typeof(List<AuditEvent>))]
[JsonSerializable(typeof(List<CapturedScript>))]
[JsonSerializable(typeof(WardenConfig))]
public partial class AuditJsonContext : JsonSerializerContext
{
}
