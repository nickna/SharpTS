using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Symbol static property access and method calls.
/// Handles well-known symbols like Symbol.iterator, Symbol.asyncIterator, Symbol.toStringTag, etc.
/// Also handles Symbol.for() and Symbol.keyFor() methods for the global symbol registry.
/// </summary>
public sealed class SymbolStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Attempts to emit IL for Symbol static method calls (Symbol.for, Symbol.keyFor).
    /// Note: Symbol() constructor is handled separately as a special case.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "for":
                // Symbol.for(key) - returns symbol from global registry
                if (arguments.Count == 0)
                {
                    // No argument provided, use "undefined" as key
                    il.Emit(OpCodes.Ldstr, "undefined");
                }
                else
                {
                    // Emit the key argument
                    emitter.EmitExpression(arguments[0]);
                    // Convert to string if needed (call ToString on object)
                    il.Emit(OpCodes.Callvirt, ctx.Types.Object.GetMethod("ToString", Type.EmptyTypes)!);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.SymbolFor);
                return true;

            case "keyFor":
                // Symbol.keyFor(symbol) - returns key for symbol in global registry, or undefined
                if (arguments.Count == 0)
                {
                    throw new Exception("Symbol.keyFor requires a symbol argument");
                }
                // Emit the symbol argument
                emitter.EmitExpression(arguments[0]);
                // Cast to $TSSymbol type
                il.Emit(OpCodes.Castclass, ctx.Runtime!.TSSymbolType);
                il.Emit(OpCodes.Call, ctx.Runtime!.SymbolKeyFor);
                // Result is string or null. Convert null to undefined.
                // Stack has: string (or null)
                il.Emit(OpCodes.Dup);  // Stack: string, string
                var hasKey = il.DefineLabel();
                var doneKeyFor = il.DefineLabel();
                il.Emit(OpCodes.Brtrue, hasKey);  // If not null, jump to hasKey
                // Null case: pop null and load undefined
                il.Emit(OpCodes.Pop);
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.UndefinedInstance);
                il.Emit(OpCodes.Br, doneKeyFor);
                // Not null case: string is still on stack
                il.MarkLabel(hasKey);
                il.MarkLabel(doneKeyFor);
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Attempts to emit IL for a Symbol well-known symbol property get.
    /// </summary>
    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (propertyName)
        {
            case "iterator":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolIterator);
                return true;
            case "asyncIterator":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolAsyncIterator);
                return true;
            case "toStringTag":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolToStringTag);
                return true;
            case "hasInstance":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolHasInstance);
                return true;
            case "isConcatSpreadable":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolIsConcatSpreadable);
                return true;
            case "toPrimitive":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolToPrimitive);
                return true;
            case "species":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolSpecies);
                return true;
            case "unscopables":
                il.Emit(OpCodes.Ldsfld, ctx.Runtime!.SymbolUnscopables);
                return true;
            default:
                return false;
        }
    }
}
