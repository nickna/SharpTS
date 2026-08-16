namespace SharpTS.Declaration;

/// <summary>Whether a <see cref="DiscoveryReport"/> describes one type in detail or lists many.</summary>
public enum DiscoveryKind
{
    /// <summary>Full member breakdown for a single resolved type.</summary>
    TypeDetail,

    /// <summary>Flat "table of contents" list of the types in a namespace or assembly.</summary>
    TableOfContents
}

/// <summary>
/// One member (constructor, method, property, or enum value) in a <see cref="TypeReport"/>,
/// already rendered to a faithful signature and classified as usable or unsupported.
/// </summary>
/// <param name="Category">Display group, e.g. "Constructors", "Instance methods".</param>
/// <param name="Signature">Faithful signature, e.g. <c>append(value: ReadOnlySpan&lt;char&gt;): StringBuilder</c>.</param>
/// <param name="Usable">True if SharpTS interop can call this member today.</param>
/// <param name="UnsupportedReason">Why it can't be used (null when <paramref name="Usable"/>).</param>
public record MemberReport(
    string Category,
    string Signature,
    bool Usable,
    string? UnsupportedReason
);

/// <summary>Full discovery detail for a single .NET type.</summary>
/// <param name="FullName">Fully-qualified CLR name (e.g. <c>System.Text.StringBuilder</c>).</param>
/// <param name="SimpleName">Short name used in the import binding.</param>
/// <param name="Kind">"class", "abstract class", "static class", "interface", or "enum".</param>
/// <param name="ImportLine">The <c>import … from "dotnet:…";</c> line, or null if the type is unusable.</param>
/// <param name="UnsupportedTypeReason">Why the type as a whole is unusable (null when importable).</param>
/// <param name="Members">All discovered members in display order.</param>
public record TypeReport(
    string FullName,
    string SimpleName,
    string Kind,
    string? ImportLine,
    string? UnsupportedTypeReason,
    List<MemberReport> Members
);

/// <summary>One row in a table-of-contents listing.</summary>
public record TocEntry(
    string FullName,
    string Kind,
    bool Usable,
    string? UnsupportedReason
);

/// <summary>
/// The result of a <c>--gen-decl</c> discovery run: either a single type's detail or a
/// table-of-contents listing for a namespace/assembly.
/// </summary>
public record DiscoveryReport(
    DiscoveryKind Kind,
    string Query,
    TypeReport? Type = null,
    string? Scope = null,
    List<TocEntry>? Types = null
);
