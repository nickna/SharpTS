namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of JavaScript/TypeScript symbols.
/// </summary>
/// <remarks>
/// Symbols are unique identifiers. Each <c>Symbol()</c> call creates a distinct symbol,
/// even with the same description. Symbols can be used as property keys in objects.
/// </remarks>
public class SharpTSSymbol
{
    private static int _nextId = 0;
    private readonly int _id;
    private readonly string? _description;

    #region Global Symbol Registry
    /// <summary>
    /// Lock object for thread-safe access to the global symbol registry.
    /// </summary>
    private static readonly object _registryLock = new();

    /// <summary>
    /// Global symbol registry mapping keys to symbols (for Symbol.for()).
    /// </summary>
    private static readonly Dictionary<string, SharpTSSymbol> _globalRegistry = new();

    /// <summary>
    /// Reverse registry mapping symbols to their keys (for Symbol.keyFor()).
    /// </summary>
    private static readonly Dictionary<SharpTSSymbol, string> _reverseRegistry = new();

    /// <summary>
    /// Returns a symbol from the global symbol registry matching the given key.
    /// If no symbol exists, creates a new one and adds it to the registry.
    /// </summary>
    /// <param name="key">The key for the symbol in the global registry.</param>
    /// <returns>The symbol associated with the key.</returns>
    public static SharpTSSymbol For(string key)
    {
        lock (_registryLock)
        {
            if (_globalRegistry.TryGetValue(key, out var existing))
                return existing;

            var symbol = new SharpTSSymbol(key);
            _globalRegistry[key] = symbol;
            _reverseRegistry[symbol] = key;
            return symbol;
        }
    }

    /// <summary>
    /// Returns the key associated with a symbol in the global registry.
    /// Returns null for symbols not in the global registry (local symbols).
    /// </summary>
    /// <param name="symbol">The symbol to look up.</param>
    /// <returns>The key if the symbol is in the global registry, null otherwise.</returns>
    public static string? KeyFor(SharpTSSymbol symbol)
    {
        lock (_registryLock)
        {
            return _reverseRegistry.TryGetValue(symbol, out var key) ? key : null;
        }
    }

    /// <summary>
    /// Gets whether this symbol is registered in the global symbol registry.
    /// </summary>
    public bool IsInGlobalRegistry
    {
        get
        {
            lock (_registryLock)
            {
                return _reverseRegistry.ContainsKey(this);
            }
        }
    }
    #endregion

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
