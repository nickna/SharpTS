namespace SharpTS.TypeSystem;

/// <summary>
/// Predefined unique symbol types for well-known symbols.
/// These correspond to TypeScript's built-in symbols like Symbol.iterator.
/// </summary>
public static class WellKnownSymbolTypes
{
    public static readonly TypeInfo.UniqueSymbol Iterator =
        new("Symbol.iterator", "typeof Symbol.iterator");
    public static readonly TypeInfo.UniqueSymbol AsyncIterator =
        new("Symbol.asyncIterator", "typeof Symbol.asyncIterator");
    public static readonly TypeInfo.UniqueSymbol ToStringTag =
        new("Symbol.toStringTag", "typeof Symbol.toStringTag");
    public static readonly TypeInfo.UniqueSymbol HasInstance =
        new("Symbol.hasInstance", "typeof Symbol.hasInstance");
    public static readonly TypeInfo.UniqueSymbol IsConcatSpreadable =
        new("Symbol.isConcatSpreadable", "typeof Symbol.isConcatSpreadable");
    public static readonly TypeInfo.UniqueSymbol ToPrimitive =
        new("Symbol.toPrimitive", "typeof Symbol.toPrimitive");
    public static readonly TypeInfo.UniqueSymbol Species =
        new("Symbol.species", "typeof Symbol.species");
    public static readonly TypeInfo.UniqueSymbol Unscopables =
        new("Symbol.unscopables", "typeof Symbol.unscopables");

    /// <summary>
    /// Tries to get a well-known symbol type by its property name.
    /// </summary>
    /// <param name="name">The property name (e.g., "iterator" for Symbol.iterator)</param>
    /// <returns>The unique symbol type, or null if not a well-known symbol</returns>
    public static TypeInfo.UniqueSymbol? TryGet(string name) => name switch
    {
        "iterator" => Iterator,
        "asyncIterator" => AsyncIterator,
        "toStringTag" => ToStringTag,
        "hasInstance" => HasInstance,
        "isConcatSpreadable" => IsConcatSpreadable,
        "toPrimitive" => ToPrimitive,
        "species" => Species,
        "unscopables" => Unscopables,
        _ => null
    };
}
