using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL for the <c>primitive:tty</c> primitive module. Dispatches
/// <c>isatty(fd)</c> to <c>$Runtime.Tty_isatty</c>. The user-facing <c>tty</c>
/// module lives in <c>stdlib/node/tty.ts</c> and imports this primitive.
/// </summary>
public sealed class TtyPrimitiveEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:tty";

    private static readonly string[] _exportedMembers = ["isatty"];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        if (methodName != "isatty") return false;

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

        il.Emit(OpCodes.Call, ctx.Runtime!.TtyIsatty);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName) => false;

    public bool IsExportedProperty(string memberName) => false;
}
