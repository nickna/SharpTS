using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Static-member strategy for the <c>Date</c> constructor. Supplies the property reads that
/// aren't calls — <c>Date.prototype</c>, <c>Date.length</c>, <c>Date.name</c>. Static *calls*
/// (<c>Date.now()</c>, <c>Date.parse(…)</c>, <c>Date.UTC(…)</c>) keep going through
/// <c>DateStaticHandler</c>, so this returns false for them.
/// </summary>
/// <remarks>
/// Before this existed <c>Date.prototype</c> read as <c>undefined</c>, so every reflective use
/// of it threw — <c>Object.getOwnPropertyDescriptor(Date.prototype, …)</c> reported
/// "called on null or undefined".
/// </remarks>
public sealed class DateStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
        => false;

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var runtime = ctx.Runtime;
        if (runtime is null) return false;

        var il = ctx.IL;

        if (propertyName == "prototype")
        {
            il.Emit(OpCodes.Call, runtime.DatePrototypePopulateMethod);
            il.Emit(OpCodes.Ldsfld, runtime.DatePrototypeField);
            return true;
        }

        // ECMA-262 §21.4.3: Date.length is 7, name is "Date".
        if (propertyName == "length")
        {
            il.Emit(OpCodes.Ldc_R8, 7.0);
            il.Emit(OpCodes.Box, ctx.Types.Double);
            return true;
        }
        if (propertyName == "name")
        {
            il.Emit(OpCodes.Ldstr, "Date");
            return true;
        }

        return false;
    }

    public bool HasStaticProperty(string memberName)
        => memberName is "length" or "name" or "prototype";
}
