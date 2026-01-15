using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'readline' module.
/// </summary>
public sealed class ReadlineModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "readline";

    private static readonly string[] _exportedMembers =
    [
        "questionSync", "createInterface"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "questionSync" => EmitQuestionSync(emitter, arguments),
            "createInterface" => EmitCreateInterface(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }

    private static bool EmitQuestionSync(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit query string
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
            il.Emit(OpCodes.Callvirt, ctx.Types.GetMethodNoParams(ctx.Types.Object, "ToString"));
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.ReadlineQuestionSync);
        return true;
    }

    private static bool EmitCreateInterface(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit options (or null)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call runtime helper
        il.Emit(OpCodes.Call, ctx.Runtime!.ReadlineCreateInterface);
        return true;
    }
}
