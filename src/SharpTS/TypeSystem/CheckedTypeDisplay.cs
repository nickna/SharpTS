using SharpTS.Declaration;

namespace SharpTS.TypeSystem;

/// <summary>A checked type whose source display cannot yet be represented faithfully.</summary>
public sealed record UnsupportedTypeDisplay(string TypeKind, string Reason);

/// <summary>A complete type display or an explicit unsupported result; unsupported types have no text.</summary>
public sealed record CheckedTypeDisplayResult(string? Text, UnsupportedTypeDisplay? Unsupported);

/// <summary>Produces deterministic, culture-invariant, non-truncated checked type displays for conformance tools.</summary>
public static class CheckedTypeDisplay
{
    /// <summary>
    /// Renders a checked type with source literals, union/intersection order and declared overloads retained.
    /// Unresolved or unsupported types return an explicit result instead of a substitute type.
    /// The method is synchronous and safe for concurrent callers; no checker state is changed.
    /// </summary>
    public static CheckedTypeDisplayResult Render(TypeInfo type)
    {
        ArgumentNullException.ThrowIfNull(type);
        try { return new(new CheckedTypeRenderer(true).Render(type), null); }
        catch (DeclarationEmitException exception)
        {
            return new(null, new(type.GetType().Name, exception.Message));
        }
    }
}
