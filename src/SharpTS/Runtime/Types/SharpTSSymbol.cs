using SharpTS.TypeSystem;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript symbols.
/// </summary>
/// <remarks>
/// Symbols are unique identifiers. Each <c>Symbol()</c> call creates a distinct symbol,
/// even with the same description. Symbols can be used as property keys in objects.
/// </remarks>
public class SharpTSSymbol : ITypeCategorized
{
    /// <inheritdoc />
    public TypeCategory RuntimeCategory => TypeCategory.Symbol;
    private static int _nextId = 0;
    private readonly int _id;
    private readonly string? _description;

    // The Symbol.for / Symbol.keyFor registry is per-realm, held on the
    // Interpreter (see Interpreter.SymbolFor / SymbolKeyFor), not as a static
    // here — each realm is its own ECMA-262 agent and the old static leaked
    // (and raced) across realms and worker threads. Well-known symbols below
    // are intentionally process-wide singletons and are not in that registry.

    #region Well-Known Symbols
    /// <summary>
    /// Symbol.iterator - Used by for...of to get an iterator from an object.
    /// </summary>
    public static readonly SharpTSSymbol Iterator = new("Symbol.iterator");

    /// <summary>
    /// Symbol.asyncIterator - Used by for await...of to get an async iterator.
    /// </summary>
    public static readonly SharpTSSymbol AsyncIterator = new("Symbol.asyncIterator");

    /// <summary>
    /// Symbol.toStringTag - Used by Object.prototype.toString() to get a custom type tag.
    /// </summary>
    public static readonly SharpTSSymbol ToStringTag = new("Symbol.toStringTag");

    /// <summary>
    /// Symbol.hasInstance - Used by instanceof to determine if an object is an instance.
    /// </summary>
    public static readonly SharpTSSymbol HasInstance = new("Symbol.hasInstance");

    /// <summary>
    /// Symbol.isConcatSpreadable - Used by Array.prototype.concat() to determine spreading.
    /// </summary>
    public static readonly SharpTSSymbol IsConcatSpreadable = new("Symbol.isConcatSpreadable");

    /// <summary>
    /// Symbol.toPrimitive - Used for type coercion to a primitive value.
    /// </summary>
    public static readonly SharpTSSymbol ToPrimitive = new("Symbol.toPrimitive");

    /// <summary>
    /// Symbol.species - Used to identify a constructor function for derived objects.
    /// </summary>
    public static readonly SharpTSSymbol Species = new("Symbol.species");

    /// <summary>
    /// Symbol.unscopables - Used to exclude properties from with statement bindings.
    /// </summary>
    public static readonly SharpTSSymbol Unscopables = new("Symbol.unscopables");

    /// <summary>
    /// Symbol.dispose - Used for synchronous resource disposal (explicit resource management).
    /// </summary>
    public static readonly SharpTSSymbol Dispose = new("Symbol.dispose");

    /// <summary>
    /// Symbol.asyncDispose - Used for asynchronous resource disposal (explicit resource management).
    /// </summary>
    public static readonly SharpTSSymbol AsyncDispose = new("Symbol.asyncDispose");

    /// <summary>
    /// Symbol.match - Used by String.prototype.match and matched against a regex.
    /// </summary>
    public static readonly SharpTSSymbol Match = new("Symbol.match");

    /// <summary>
    /// Symbol.matchAll - Used by String.prototype.matchAll.
    /// </summary>
    public static readonly SharpTSSymbol MatchAll = new("Symbol.matchAll");

    /// <summary>
    /// Symbol.replace - Used by String.prototype.replace and replaceAll.
    /// </summary>
    public static readonly SharpTSSymbol Replace = new("Symbol.replace");

    /// <summary>
    /// Symbol.search - Used by String.prototype.search.
    /// </summary>
    public static readonly SharpTSSymbol Search = new("Symbol.search");

    /// <summary>
    /// Symbol.split - Used by String.prototype.split.
    /// </summary>
    public static readonly SharpTSSymbol Split = new("Symbol.split");
    #endregion

    public SharpTSSymbol(string? description = null)
    {
        _id = Interlocked.Increment(ref _nextId);
        _description = description;
    }

    public string? Description => _description;

    public override bool Equals(object? obj) =>
        obj is SharpTSSymbol other && _id == other._id;

    public override int GetHashCode() => _id;

    public override string ToString() =>
        _description != null ? $"Symbol({_description})" : "Symbol()";
}
