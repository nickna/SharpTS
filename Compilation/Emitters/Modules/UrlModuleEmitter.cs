using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'url' module.
/// </summary>
public sealed class UrlModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "url";

    private static readonly string[] _exportedMembers =
    [
        "URL", "URLSearchParams", "parse", "format", "resolve"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "parse" => EmitParse(emitter, arguments),
            "format" => EmitFormat(emitter, arguments),
            "resolve" => EmitResolve(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // URL and URLSearchParams are handled as class constructors, not properties
        // They require special handling in the compiler
        return false;
    }

    private static bool EmitParse(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: UrlParse(object? url)
        if (arguments.Count > 0)
        {
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.UrlParse);
        return true;
    }

    private static bool EmitFormat(IEmitterContext emitter, List<Expr> arguments)
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

        il.Emit(OpCodes.Call, ctx.Runtime!.UrlFormat);
        return true;
    }

    private static bool EmitResolve(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call runtime helper: UrlResolve(object? from, object? to)
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

        il.Emit(OpCodes.Call, ctx.Runtime!.UrlResolve);
        return true;
    }
}
