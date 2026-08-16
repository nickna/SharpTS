using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'dgram' module.
/// </summary>
public sealed class DgramModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "dgram";

    private static readonly string[] _exportedMembers = ["createSocket", "Socket"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName == "createSocket")
        {
            var ctx = emitter.Context;
            var il = ctx.IL;

            // Emit type argument
            if (arguments.Count > 0)
            {
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            // Emit callback argument (optional)
            if (arguments.Count > 1)
            {
                emitter.EmitExpression(arguments[1]);
                emitter.EmitBoxIfNeeded(arguments[1]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }

            il.Emit(OpCodes.Call, ctx.Runtime!.DgramCreateSocket);
            emitter.SetStackUnknown();
            return true;
        }

        return false;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }

    public bool IsExportedProperty(string memberName) => false;
}
