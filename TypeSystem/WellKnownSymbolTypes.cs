using System.Collections.Frozen;

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
    public static readonly TypeInfo.UniqueSymbol Dispose =
        new("Symbol.dispose", "typeof Symbol.dispose");
    public static readonly TypeInfo.UniqueSymbol AsyncDispose =
        new("Symbol.asyncDispose", "typeof Symbol.asyncDispose");
    public static readonly TypeInfo.UniqueSymbol Match =
        new("Symbol.match", "typeof Symbol.match");
    public static readonly TypeInfo.UniqueSymbol MatchAll =
        new("Symbol.matchAll", "typeof Symbol.matchAll");
    public static readonly TypeInfo.UniqueSymbol Replace =
        new("Symbol.replace", "typeof Symbol.replace");
    public static readonly TypeInfo.UniqueSymbol Search =
        new("Symbol.search", "typeof Symbol.search");
    public static readonly TypeInfo.UniqueSymbol Split =
        new("Symbol.split", "typeof Symbol.split");

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
        "dispose" => Dispose,
        "asyncDispose" => AsyncDispose,
        "match" => Match,
        "matchAll" => MatchAll,
        "replace" => Replace,
        "search" => Search,
        "split" => Split,
        _ => null
    };

    /// <summary>
    /// The type of the bare `Symbol` value itself (tsc's `SymbolConstructor`). Used wherever
    /// `Symbol` is referenced WITHOUT being called — e.g. `var s = Symbol; obj[s]`. Deliberately
    /// NOT `any`: a bare-Symbol-derived value must fail non-permissive checks like a computed
    /// property name's "must be string/number/symbol/any" validation (TS2464), the way tsc's real
    /// SymbolConstructor object type does. `Symbol(...)` calls and `Symbol.iterator`-style
    /// well-known-symbol access are matched by earlier special cases (TryCheckBuiltinCall /
    /// CheckGet) before generic member lookup ever consults this type, so adding it here doesn't
    /// change those paths — only genuine "use Symbol as a plain value" sites.
    /// </summary>
    public static readonly TypeInfo.Interface SymbolConstructor = new(
        "SymbolConstructor",
        new Dictionary<string, TypeInfo>
        {
            ["iterator"] = Iterator,
            ["asyncIterator"] = AsyncIterator,
            ["toStringTag"] = ToStringTag,
            ["hasInstance"] = HasInstance,
            ["isConcatSpreadable"] = IsConcatSpreadable,
            ["toPrimitive"] = ToPrimitive,
            ["species"] = Species,
            ["unscopables"] = Unscopables,
            ["dispose"] = Dispose,
            ["asyncDispose"] = AsyncDispose,
            ["match"] = Match,
            ["matchAll"] = MatchAll,
            ["replace"] = Replace,
            ["search"] = Search,
            ["split"] = Split,
            ["for"] = new TypeInfo.Function([new TypeInfo.String()], new TypeInfo.Symbol(), 1, false, null, ["key"]),
            ["keyFor"] = new TypeInfo.Function([new TypeInfo.Symbol()], new TypeInfo.Union([new TypeInfo.String(), new TypeInfo.Undefined()]), 1, false, null, ["sym"]),
            ["prototype"] = new TypeInfo.Record(FrozenDictionary<string, TypeInfo>.Empty),
        }.ToFrozenDictionary(),
        FrozenSet<string>.Empty);
}
