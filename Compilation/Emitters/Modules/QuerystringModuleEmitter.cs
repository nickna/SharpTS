using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'querystring' module.
/// </summary>
public sealed class QuerystringModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "querystring";

    private static readonly string[] _exportedMembers =
    [
        "parse", "stringify", "escape", "unescape", "decode", "encode"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "parse" or "decode" => EmitParse(emitter, arguments),
            "stringify" or "encode" => EmitStringify(emitter, arguments),
            "escape" => EmitEscape(emitter, arguments),
            "unescape" => EmitUnescape(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // querystring has no properties
        return false;
    }

    private static bool EmitParse(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: QuerystringParse(string str, string? sep, string? eq)
        // Argument 0: str
        if (arguments.Count > 0)
        {
            EmitToString(emitter, arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }

        // Argument 1: sep (default "&")
        if (arguments.Count > 1)
        {
            EmitToString(emitter, arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "&");
        }

        // Argument 2: eq (default "=")
        if (arguments.Count > 2)
        {
            EmitToString(emitter, arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "=");
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.QuerystringParse);
        return true;
    }

    private static bool EmitStringify(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: QuerystringStringify(object? obj, string sep, string eq)
        // Argument 0: obj
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Argument 1: sep (default "&")
        if (arguments.Count > 1)
        {
            EmitToString(emitter, arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "&");
        }

        // Argument 2: eq (default "=")
        if (arguments.Count > 2)
        {
            EmitToString(emitter, arguments[2]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "=");
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.QuerystringStringify);
        return true;
    }

    private static bool EmitEscape(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Uri.EscapeDataString(str)
        if (arguments.Count > 0)
        {
            EmitToString(emitter, arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }

        il.Emit(OpCodes.Call, typeof(Uri).GetMethod("EscapeDataString", [typeof(string)])!);
        return true;
    }

    private static bool EmitUnescape(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Replace '+' with ' ' then Uri.UnescapeDataString(str)
        if (arguments.Count > 0)
        {
            EmitToString(emitter, arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldstr, "");
        }

        // Call string.Replace('+', ' ')
        il.Emit(OpCodes.Ldc_I4, '+');
        il.Emit(OpCodes.Ldc_I4, ' ');
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.String, "Replace", ctx.Types.Char, ctx.Types.Char));

        // Call Uri.UnescapeDataString
        il.Emit(OpCodes.Call, typeof(Uri).GetMethod("UnescapeDataString", [typeof(string)])!);
        return true;
    }

    private static void EmitToString(IEmitterContext emitter, Expr expr)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        emitter.EmitExpression(expr);
        emitter.EmitBoxIfNeeded(expr);
        il.Emit(OpCodes.Callvirt, ctx.Types.GetMethod(ctx.Types.Object, "ToString"));
    }
}
