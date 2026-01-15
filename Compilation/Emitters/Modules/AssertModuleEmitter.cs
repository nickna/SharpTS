using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'assert' module.
/// </summary>
public sealed class AssertModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "assert";

    private static readonly string[] _exportedMembers =
    [
        "ok", "strictEqual", "notStrictEqual", "deepStrictEqual", "notDeepStrictEqual",
        "throws", "doesNotThrow", "fail", "equal", "notEqual"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "ok" => EmitOk(emitter, arguments),
            "strictEqual" => EmitStrictEqual(emitter, arguments),
            "notStrictEqual" => EmitNotStrictEqual(emitter, arguments),
            "deepStrictEqual" => EmitDeepStrictEqual(emitter, arguments),
            "notDeepStrictEqual" => EmitNotDeepStrictEqual(emitter, arguments),
            "throws" => EmitThrows(emitter, arguments),
            "doesNotThrow" => EmitDoesNotThrow(emitter, arguments),
            "fail" => EmitFail(emitter, arguments),
            "equal" => EmitEqual(emitter, arguments),
            "notEqual" => EmitNotEqual(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // assert has no properties
        return false;
    }

    private static bool EmitOk(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: AssertOk(object? value, object? message)
        if (arguments.Count > 0)
        {
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count > 1)
        {
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.AssertOk);
        return true;
    }

    private static bool EmitStrictEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: AssertStrictEqual(object? actual, object? expected, object? message)
        EmitThreeArgs(emitter, arguments);
        il.Emit(OpCodes.Call, ctx.Runtime!.AssertStrictEqual);
        return true;
    }

    private static bool EmitNotStrictEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitThreeArgs(emitter, arguments);
        il.Emit(OpCodes.Call, ctx.Runtime!.AssertNotStrictEqual);
        return true;
    }

    private static bool EmitDeepStrictEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitThreeArgs(emitter, arguments);
        il.Emit(OpCodes.Call, ctx.Runtime!.AssertDeepStrictEqual);
        return true;
    }

    private static bool EmitNotDeepStrictEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitThreeArgs(emitter, arguments);
        il.Emit(OpCodes.Call, ctx.Runtime!.AssertNotDeepStrictEqual);
        return true;
    }

    private static bool EmitThrows(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: AssertThrows(object? fn, object? message)
        if (arguments.Count > 0)
        {
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count > 1)
        {
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.AssertThrows);
        return true;
    }

    private static bool EmitDoesNotThrow(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count > 1)
        {
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.AssertDoesNotThrow);
        return true;
    }

    private static bool EmitFail(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: AssertFail(object? message)
        if (arguments.Count > 0)
        {
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.AssertFail);
        return true;
    }

    private static bool EmitEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitThreeArgs(emitter, arguments);
        il.Emit(OpCodes.Call, ctx.Runtime!.AssertEqual);
        return true;
    }

    private static bool EmitNotEqual(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        EmitThreeArgs(emitter, arguments);
        il.Emit(OpCodes.Call, ctx.Runtime!.AssertNotEqual);
        return true;
    }

    private static void EmitThreeArgs(IEmitterContext emitter, List<Expr> arguments)
    {
        var il = emitter.Context.IL;

        if (arguments.Count > 0)
        {
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count > 1)
        {
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (arguments.Count > 2)
        {
            emitter.EmitBoxIfNeeded(arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
    }
}
