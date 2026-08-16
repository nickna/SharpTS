using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for Boolean static property access.
/// Boolean has no static methods (its only "method" is the constructor),
/// but it has the standard <c>length</c>, <c>name</c>, and <c>prototype</c>
/// constructor metadata properties per ECMA-262 §20.3.
/// </summary>
public sealed class BooleanStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
        => false;

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Boolean.prototype — singleton dict populated lazily with toString,
        // valueOf wrappers (Stage 4z9 pattern, populated for Number/String/Array).
        if (propertyName == "prototype")
        {
            il.Emit(OpCodes.Call, ctx.Runtime!.BooleanPrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, ctx.Runtime!.BooleanPrototypeField);
            return true;
        }

        // Constructor metadata properties (ECMA-262 §20.3.2): Boolean.length is 1, name is "Boolean".
        if (propertyName == "length")
        {
            il.Emit(OpCodes.Ldc_R8, 1.0);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }
        if (propertyName == "name")
        {
            il.Emit(OpCodes.Ldstr, "Boolean");
            return true;
        }

        return false;
    }

    public bool HasStaticProperty(string memberName)
        => memberName is "length" or "name" or "prototype";
}
