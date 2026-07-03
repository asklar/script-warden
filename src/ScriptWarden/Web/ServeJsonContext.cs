using System.Text.Json.Serialization;

namespace ScriptWarden.Web;

internal sealed class RootDto
{
    public string Path { get; set; } = "";
    public string Origin { get; set; } = "";
    public bool Exists { get; set; }
    public bool Readable { get; set; }
    public string? Error { get; set; }
}

internal sealed class ServeStatus
{
    public string Version { get; set; } = "";
    public int EventCount { get; set; }
    public List<RootDto> Roots { get; set; } = [];
    public List<string> Images { get; set; } = [];
    public List<string> Parents { get; set; } = [];
    public List<string> Windows { get; set; } = [];
}

internal sealed class ClearResult
{
    public int Events { get; set; }
    public int Scripts { get; set; }
    public List<string> Roots { get; set; } = [];
}

/// <summary>Source-gen context for the viewer's status DTO (events reuse <c>AuditJsonContext</c>).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ServeStatus))]
[JsonSerializable(typeof(ClearResult))]
internal partial class ServeJsonContext : JsonSerializerContext
{
}
