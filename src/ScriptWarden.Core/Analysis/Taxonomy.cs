using System.Text.Json.Serialization;

namespace ScriptWarden.Core.Analysis;

/// <summary>
/// A data-driven classification dimension: a set of ordered <see cref="Rule"/>s that assign one or
/// more <see cref="Rule.Label"/>s to an event. Shipped defaults (Source, Behavior) are just JSON
/// data under the data root and are user-editable; users can add their own taxonomies too.
/// </summary>
public sealed class Taxonomy
{
    /// <summary>Stable id (also the file stem, e.g. <c>source</c>).</summary>
    public string Id { get; set; } = "";

    /// <summary>Human-readable name shown in the viewer.</summary>
    public string Name { get; set; } = "";

    /// <summary>When true, every matching rule's label is applied; otherwise the first match wins.</summary>
    public bool MultiLabel { get; set; }

    public List<Rule> Rules { get; set; } = [];
}

/// <summary>Assigns <see cref="Label"/> when its condition matches. A rule matches when all of
/// <see cref="All"/> (if present) or any of <see cref="Any"/> (if present) match. A <see cref="Default"/>
/// rule's label is applied only when no other rule matched.</summary>
public sealed class Rule
{
    public string Label { get; set; } = "";
    public List<Predicate>? Any { get; set; }
    public List<Predicate>? All { get; set; }
    public bool Default { get; set; }
}

/// <summary>A single condition over one event field. <see cref="Field"/> and <see cref="Op"/> come
/// from a fixed vocabulary the engine understands; unknown values simply never match.</summary>
public sealed class Predicate
{
    /// <summary>e.g. <c>commandLine</c>, <c>ancestorName</c>, <c>scriptContent</c>, <c>userSid</c>.</summary>
    public string? Field { get; set; }

    /// <summary>e.g. <c>equals</c>, <c>in</c>, <c>contains</c>, <c>startsWith</c>, <c>regex</c>, <c>isTrue</c>.</summary>
    public string? Op { get; set; }

    /// <summary>Comparison value for single-value operators.</summary>
    public string? Value { get; set; }

    /// <summary>Comparison values for the <c>in</c> operator.</summary>
    public List<string>? Values { get; set; }
}
