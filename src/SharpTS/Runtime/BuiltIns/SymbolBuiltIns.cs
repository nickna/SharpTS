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
            // Well-known symbols are PROPERTY-style constants (access returns the symbol
            // value), not methods. Use CreateConstant so the IsConstant flag is set and
            // the interpreter's Get fast-path materializes them on access — otherwise
            // `Symbol.iterator` returns the wrapper method instead of the symbol itself
            // and `typeof Symbol.iterator === 'symbol'` fails.
            "iterator" => BuiltInMethod.CreateConstant("iterator", SharpTSSymbol.Iterator),
            "asyncIterator" => BuiltInMethod.CreateConstant("asyncIterator", SharpTSSymbol.AsyncIterator),
            "toStringTag" => BuiltInMethod.CreateConstant("toStringTag", SharpTSSymbol.ToStringTag),
            "hasInstance" => BuiltInMethod.CreateConstant("hasInstance", SharpTSSymbol.HasInstance),
            "isConcatSpreadable" => BuiltInMethod.CreateConstant("isConcatSpreadable", SharpTSSymbol.IsConcatSpreadable),
            "toPrimitive" => BuiltInMethod.CreateConstant("toPrimitive", SharpTSSymbol.ToPrimitive),
            "species" => BuiltInMethod.CreateConstant("species", SharpTSSymbol.Species),
            "unscopables" => BuiltInMethod.CreateConstant("unscopables", SharpTSSymbol.Unscopables),
            "dispose" => BuiltInMethod.CreateConstant("dispose", SharpTSSymbol.Dispose),
            "asyncDispose" => BuiltInMethod.CreateConstant("asyncDispose", SharpTSSymbol.AsyncDispose),
            "match" => BuiltInMethod.CreateConstant("match", SharpTSSymbol.Match),
            "matchAll" => BuiltInMethod.CreateConstant("matchAll", SharpTSSymbol.MatchAll),
            "replace" => BuiltInMethod.CreateConstant("replace", SharpTSSymbol.Replace),
            "search" => BuiltInMethod.CreateConstant("search", SharpTSSymbol.Search),
            "split" => BuiltInMethod.CreateConstant("split", SharpTSSymbol.Split),

            // Symbol.for() - returns a shared symbol from the realm's symbol registry
            "for" => BuiltInMethod.CreateV2("for", 1, static (interp, _, args) =>
            {
                var key = args.Length > 0 ? args[0].ToObject()?.ToString() ?? "undefined" : "undefined";
                return RuntimeValue.FromSymbol(interp.SymbolFor(key));
            }),

            // Symbol.keyFor() - returns the key for a symbol in the realm's registry, or undefined
            "keyFor" => BuiltInMethod.CreateV2("keyFor", 1, static (interp, _, args) =>
            {
                if (args.Length > 0 && args[0].IsSymbol)
                {
                    var key = interp.SymbolKeyFor(args[0].AsSymbol());
                    // Return undefined if symbol is not in the realm's registry (matches JS behavior)
                    return key is null ? RuntimeValue.Undefined : RuntimeValue.FromString(key);
                }
                return RuntimeValue.Undefined;
            }),

            _ => null
        };
    }

    /// <summary>
    /// Gets an instance member for a symbol value (Symbol.prototype surface).
    /// </summary>
    /// <param name="symbol">The receiver symbol</param>
    /// <param name="name">The member name (e.g., "description", "toString")</param>
    /// <returns>The member value or bound method, or null if not found</returns>
    public static object? GetInstanceMember(SharpTSSymbol symbol, string name)
    {
        return name switch
        {
            "description" => symbol.Description ?? (object)SharpTSUndefined.Instance,

            // Symbol.prototype.toString - returns SymbolDescriptiveString, e.g. "Symbol(desc)"
            "toString" => BuiltInMethod.CreateV2("toString", 0, (_, _, _) =>
                RuntimeValue.FromString(symbol.ToString())).AsNonConstructor(),

            // Symbol.prototype.valueOf - returns the symbol itself
            "valueOf" => BuiltInMethod.CreateV2("valueOf", 0, (_, _, _) =>
                RuntimeValue.FromSymbol(symbol)).AsNonConstructor(),

            _ => null
        };
    }
}
