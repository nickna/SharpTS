using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters;

/// <summary>
/// Emitter strategy for ReadableStream static method calls (#269). Currently
/// only <c>ReadableStream.from(iterable)</c>, which eagerly drains a guest
/// iterable into a closed readable stream via $ReadableStream.From. The
/// interpreter's equivalent lives in SharpTSWebStreamsConstructors.
/// </summary>
public sealed class ReadableStreamStaticEmitter : IStaticTypeEmitterStrategy
{
    public bool TryEmitStaticCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        switch (methodName)
        {
            case "from":
                // ReadableStream.from(iterable) → $ReadableStream.From(iterable)
                if (arguments.Count > 0)
                {
                    emitter.EmitExpression(arguments[0]);
                    emitter.EmitBoxIfNeeded(arguments[0]);
                }
                else
                {
                    il.Emit(OpCodes.Ldnull);
                }
                il.Emit(OpCodes.Call, ctx.Runtime!.ReadableStreamFrom);
                return true;

            default:
                return false;
        }
    }

    public bool TryEmitStaticPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }
}
