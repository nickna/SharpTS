using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL for <c>primitive:readline</c>. Exposes <c>questionSync(query)</c>
/// and <c>createInterface(options)</c> via the existing <c>$Runtime</c> helpers
/// and the <c>$ReadlineInterface</c> emitted class. The user-facing readline
/// module is implemented in <c>stdlib/node/readline.ts</c>, which wraps the
/// returned Interface instance and forwards method calls dynamically.
/// </summary>
public sealed class ReadlinePrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:readline";

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

        il.Emit(OpCodes.Call, ctx.Runtime!.ReadlineQuestionSync);
        return true;
    }

    private static bool EmitCreateInterface(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.ReadlineCreateInterface);
        return true;
    }
}
