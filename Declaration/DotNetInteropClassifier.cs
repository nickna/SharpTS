namespace SharpTS.Declaration;

/// <summary>
/// Single source of truth for "can SharpTS's <c>@DotNetType</c> interop actually use this
/// today?" Classifies a .NET type slot (a parameter or return type) or a whole type as
/// <em>usable</em> or <em>unsupported (with a reason)</em>.
/// </summary>
/// <remarks>
/// The <c>--gen-decl</c> discovery tool (<see cref="DiscoveryEmitter"/>) and the runtime
/// interop marshaller (<c>Runtime/DotNet/DotNetMarshaller.cs</c>) must never disagree about
/// what is callable, so both classify against these rules. The marshaller has no code path
/// for by-ref (<c>ref</c>/<c>out</c>/<c>in</c>) slots, pointers, or ref structs
/// (<c>Span&lt;T&gt;</c>/<c>ReadOnlySpan&lt;T&gt;</c>), and open generic parameters have no
/// concrete runtime type — those are the four unsupported categories. When the <c>dotnet:</c>
/// import resolver lands (#1195) it should call these same helpers.
/// </remarks>
public static class DotNetInteropClassifier
{
    /// <summary>Human-readable reason a slot is unsupported: a by-ref parameter/return.</summary>
    public const string ReasonByRef = "by-ref (ref/out/in) is not marshalable";

    /// <summary>Human-readable reason a slot is unsupported: a pointer type.</summary>
    public const string ReasonPointer = "pointer types are not marshalable";

    /// <summary>Human-readable reason a slot is unsupported: a ref struct (Span&lt;T&gt; etc.).</summary>
    public const string ReasonRefStruct = "ref struct (Span/ReadOnlySpan) cannot cross the interop boundary";

    /// <summary>Human-readable reason a slot is unsupported: an unbound generic parameter.</summary>
    public const string ReasonOpenGeneric = "open generic has no concrete runtime type";

    /// <summary>
    /// Returns null if a single type slot (a parameter or a method/property return type) can
    /// be marshaled across the interop boundary today, or a human-readable reason if it can't.
    /// </summary>
    public static string? UnsupportedSlotReason(Type type)
    {
        // by-ref (ref/out/in) — the marshaller has no path to write back to the caller's slot.
        if (type.IsByRef)
            return ReasonByRef;

        // Raw pointers can't be represented as a TS runtime value.
        if (type.IsPointer)
            return ReasonPointer;

        // Ref structs (Span<T>, ReadOnlySpan<T>, …) cannot be boxed, so they can never be
        // passed as an object across the interop boundary.
        if (type.IsByRefLike)
            return ReasonRefStruct;

        // An unbound generic parameter (T) or any type that still contains one has no
        // concrete runtime type to marshal to/from.
        if (type.IsGenericParameter || type.ContainsGenericParameters)
            return ReasonOpenGeneric;

        return null;
    }

    /// <summary>
    /// Returns null if the type itself can be imported/used as a whole, or a reason if it
    /// can't (an open generic type definition, a pointer, or a ref struct).
    /// </summary>
    public static string? UnsupportedTypeReason(Type type)
    {
        if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            return ReasonOpenGeneric;
        if (type.IsPointer)
            return ReasonPointer;
        if (type.IsByRefLike)
            return ReasonRefStruct;
        return null;
    }
}
