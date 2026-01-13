using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Symbol static property access.
/// Handles well-known symbols like Symbol.iterator, Symbol.asyncIterator, Symbol.toStringTag, etc.
/// </summary>
public sealed class SymbolStaticEmitter : IStaticTypeEmitterStrategy
{
    /// <summary>
    /// Symbol has no static methods to emit.
    /// Note: Symbol() constructor is handled separately as a special case.
    /// </summary>
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return false;
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
