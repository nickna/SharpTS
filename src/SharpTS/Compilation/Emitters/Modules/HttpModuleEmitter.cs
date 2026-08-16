using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'http' module.
/// </summary>
/// <remarks>
/// Provides HTTP server and client functionality:
/// - createServer() - creates an HTTP server
/// - request() - makes an HTTP request
/// - get() - shorthand for GET requests
/// - METHODS - array of HTTP methods
/// - STATUS_CODES - map of status codes to messages
/// - globalAgent - default HTTP agent
/// </remarks>
public sealed class HttpModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "http";

    private static readonly string[] _exportedMembers =
    [
        "createServer",
        "request",
        "get",
        "METHODS",
        "STATUS_CODES",
        "globalAgent",
        "Agent",
        "validateHeaderName",
        "validateHeaderValue",
        "setMaxIdleHTTPParsers",
        "maxHeaderSize"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "createServer" => EmitCreateServer(emitter, arguments),
            "request" => EmitRequest(emitter, arguments),
            "get" => EmitGet(emitter, arguments),
            "validateHeaderName" => EmitUtilCall(emitter, arguments, emitter.Context.Runtime!.HttpValidateHeaderName, 1),
            "validateHeaderValue" => EmitUtilCall(emitter, arguments, emitter.Context.Runtime!.HttpValidateHeaderValue, 2),
            "setMaxIdleHTTPParsers" => EmitUtilCall(emitter, arguments, emitter.Context.Runtime!.HttpSetMaxIdleParsers, 1),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return propertyName switch
        {
            "METHODS" => EmitMethods(emitter),
            "STATUS_CODES" => EmitStatusCodes(emitter),
            "globalAgent" => EmitGlobalAgent(emitter),
            "Agent" => EmitAgentConstructor(emitter),
            "maxHeaderSize" => EmitMaxHeaderSize(emitter),
            _ => false
        };
    }

    /// <summary>
    /// Emits a call to a $Runtime header-utility helper, padding/truncating to argCount object args.
    /// </summary>
    private static bool EmitUtilCall(IEmitterContext emitter, List<Expr> arguments,
        System.Reflection.Emit.MethodBuilder target, int argCount)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        for (int i = 0; i < argCount; i++)
        {
            if (i < arguments.Count)
            {
                emitter.EmitExpression(arguments[i]);
                emitter.EmitBoxIfNeeded(arguments[i]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }
        il.Emit(OpCodes.Call, target);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitMaxHeaderSize(IEmitterContext emitter)
    {
        var il = emitter.Context.IL;
        il.Emit(OpCodes.Ldc_R8, 16384.0);
        il.Emit(OpCodes.Box, emitter.Context.Types.Double);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitCreateServer(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit callback - first argument (optional)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.HttpCreateServer(callback) - returns SharpTSHttpServer
        il.Emit(OpCodes.Call, ctx.Runtime!.HttpCreateServer);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitRequest(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit URL or options - first argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit options - second argument (optional)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.HttpRequest(urlOrOptions, options) - returns Promise
        il.Emit(OpCodes.Call, ctx.Runtime!.HttpRequest);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitGet(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit URL or options - first argument
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit options - second argument (optional)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.HttpGet(urlOrOptions, options) - returns Promise
        il.Emit(OpCodes.Call, ctx.Runtime!.HttpGet);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitMethods(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call $Runtime.HttpGetMethods() - returns SharpTSArray
        il.Emit(OpCodes.Call, ctx.Runtime!.HttpGetMethods);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitStatusCodes(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call $Runtime.HttpGetStatusCodes() - returns SharpTSObject
        il.Emit(OpCodes.Call, ctx.Runtime!.HttpGetStatusCodes);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitGlobalAgent(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call $Runtime.HttpGetGlobalAgent() - returns SharpTSObject
        il.Emit(OpCodes.Call, ctx.Runtime!.HttpGetGlobalAgent);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitAgentConstructor(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.HttpGetAgentConstructor);
        emitter.SetStackUnknown();
        return true;
    }

    public bool IsExportedProperty(string memberName) => memberName is
        "METHODS" or "STATUS_CODES" or "globalAgent" or "Agent" or "maxHeaderSize";
}
