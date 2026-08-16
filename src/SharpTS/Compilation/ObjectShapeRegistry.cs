using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation;

/// <summary>
/// Program-wide cache of generated value-type "shape" structs for promoted object-literal locals (#862).
/// Mirrors the role of <c>DisplayClasses</c>: defined once after analysis, shared across every emit
/// context, and finalized (<c>CreateType()</c>) at the end of compilation. Keyed two ways — by the
/// shape's <see cref="TypeSystem.ObjectShapeInfo.CanonicalKey"/> (so the declaration site resolves the
/// generated type from the <see cref="TypeMap"/> mark) and by the generated CLR <see cref="System.Type"/>
/// (so the property get/set fast paths recognise a promoted local purely from its slot type — the same
/// CLR-type-gated, shadow-safe resolution used by promoted typed-array locals).
/// </summary>
public sealed class ObjectShapeRegistry
{
    /// <summary>Canonical shape key → generated shape type info.</summary>
    public Dictionary<string, ObjectShapeTypeInfo> ByKey { get; } = new(StringComparer.Ordinal);

    /// <summary>Generated CLR type (the <see cref="TypeBuilder"/>) → shape type info, for use-site lookup.</summary>
    public Dictionary<Type, ObjectShapeTypeInfo> ByClrType { get; } = new();

    /// <summary>Monotonic counter for unique <c>$Shape_N</c> type names.</summary>
    public int Counter { get; set; }
}

/// <summary>
/// One generated shape struct: its CLR type, the ordered fields (name + primitive kind), and the
/// <see cref="FieldBuilder"/> for each field name (used to emit <c>ldfld</c>/<c>stfld</c>).
/// </summary>
public sealed class ObjectShapeTypeInfo
{
    /// <summary>The generated value type (a <see cref="TypeBuilder"/> during emit; finalized later).</summary>
    public required Type ClrType { get; init; }

    /// <summary>Ordered fields (name + kind), matching the literal's property order.</summary>
    public required IReadOnlyList<(string Name, TokenType Kind)> Fields { get; init; }

    /// <summary>Field name → its <see cref="FieldBuilder"/> on <see cref="ClrType"/>.</summary>
    public required Dictionary<string, FieldBuilder> FieldBuilders { get; init; }
}
