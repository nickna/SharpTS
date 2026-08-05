using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits the trusted <c>sharpts:execution</c> host bridge through a late-bound
/// SharpTS runtime call, preserving the standalone-DLL constraint.
/// </summary>
public sealed class SourceExecutionModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "sharpts:execution";

    private static readonly string[] _exportedMembers =
        ["runSourceJson", "configureUntrustedProcess"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName == "configureUntrustedProcess")
        {
            var configureContext = emitter.Context;
            if (arguments.Count > 0)
            {
                emitter.EmitExpression(arguments[0]);
                emitter.EmitBoxIfNeeded(arguments[0]);
            }
            else
            {
                configureContext.IL.Emit(OpCodes.Ldnull);
            }
            configureContext.IL.Emit(
                OpCodes.Call,
                configureContext.Runtime!.SourceExecutionConfigureUntrustedProcess);
            return true;
        }

        if (methodName != "runSourceJson")
            return false;

        var context = emitter.Context;
        for (var index = 0; index < 3; index++)
        {
            if (arguments.Count > index)
            {
                emitter.EmitExpression(arguments[index]);
                emitter.EmitBoxIfNeeded(arguments[index]);
            }
            else
            {
                context.IL.Emit(OpCodes.Ldnull);
            }
        }

        context.IL.Emit(OpCodes.Call, context.Runtime!.SourceExecutionRunJson);
        emitter.SetStackUnknown();
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        if (propertyName is not ("runSourceJson" or "configureUntrustedProcess"))
            return false;
        emitter.Context.IL.Emit(OpCodes.Ldnull);
        return true;
    }

    public bool IsExportedProperty(string memberName) => false;
}
