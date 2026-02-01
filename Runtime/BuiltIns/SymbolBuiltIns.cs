using SharpTS.Runtime.Types;

namespace SharpTS.Runtime.BuiltIns;

/// <summary>
/// Provides implementations for JavaScript Symbol static members.
/// </summary>
/// <remarks>
/// Contains well-known symbols (iterator, asyncIterator, toStringTag, etc.)
/// that back the <c>Symbol.x</c> syntax in TypeScript.
/// Called by <see cref="Execution.Interpreter"/> when resolving property access on Symbol.
/// All members are returned as BuiltInMethod for consistency with the registry pattern.
/// </remarks>
/// <seealso cref="SharpTSSymbol"/>
public static class SymbolBuiltIns
{
    /// <summary>
    /// Gets a static member from the Symbol namespace.
    /// All members are returned as BuiltInMethod for consistency with the registry.
    /// </summary>
    /// <param name="name">The member name (e.g., "iterator", "asyncIterator")</param>
    /// <returns>A BuiltInMethod wrapping the well-known symbol or method, or null if not found</returns>
    public static object? GetStaticMember(string name)
    {
        return name switch
        {
            // Well-known symbols - wrapped as zero-arity methods for registry consistency
            "iterator" => new BuiltInMethod("iterator", 0, 0, (_, _, _) => SharpTSSymbol.Iterator),
            "asyncIterator" => new BuiltInMethod("asyncIterator", 0, 0, (_, _, _) => SharpTSSymbol.AsyncIterator),
            "toStringTag" => new BuiltInMethod("toStringTag", 0, 0, (_, _, _) => SharpTSSymbol.ToStringTag),
            "hasInstance" => new BuiltInMethod("hasInstance", 0, 0, (_, _, _) => SharpTSSymbol.HasInstance),
            "isConcatSpreadable" => new BuiltInMethod("isConcatSpreadable", 0, 0, (_, _, _) => SharpTSSymbol.IsConcatSpreadable),
            "toPrimitive" => new BuiltInMethod("toPrimitive", 0, 0, (_, _, _) => SharpTSSymbol.ToPrimitive),
            "species" => new BuiltInMethod("species", 0, 0, (_, _, _) => SharpTSSymbol.Species),
            "unscopables" => new BuiltInMethod("unscopables", 0, 0, (_, _, _) => SharpTSSymbol.Unscopables),
            "dispose" => new BuiltInMethod("dispose", 0, 0, (_, _, _) => SharpTSSymbol.Dispose),
            "asyncDispose" => new BuiltInMethod("asyncDispose", 0, 0, (_, _, _) => SharpTSSymbol.AsyncDispose),

            // Symbol.for() - returns a shared symbol from the global symbol registry
            "for" => new BuiltInMethod("for", 1, (_, _, args) =>
            {
                var key = args.Count > 0 ? args[0]?.ToString() ?? "undefined" : "undefined";
                return SharpTSSymbol.For(key);
            }),

            // Symbol.keyFor() - returns the key for a symbol in the global registry, or undefined
            "keyFor" => new BuiltInMethod("keyFor", 1, (_, _, args) =>
            {
                if (args.Count > 0 && args[0] is SharpTSSymbol sym)
                {
                    var key = SharpTSSymbol.KeyFor(sym);
                    // Return undefined if symbol is not in global registry (matches JS behavior)
                    return key ?? (object?)SharpTSUndefined.Instance;
                }
                return SharpTSUndefined.Instance;
            }),

            _ => null
        };
    }
}
