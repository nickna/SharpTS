using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'tls' module.
/// </summary>
/// <remarks>
/// Provides TLS/SSL networking functionality:
/// - createServer(options?, callback?) - creates a TLS server
/// - connect(port, host?, options?, callback?) - creates a TLS client connection
/// - createSecureContext(options?) - creates a secure context
/// - DEFAULT_MIN_VERSION, DEFAULT_MAX_VERSION - protocol version constants
/// </remarks>
public sealed class TlsModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "tls";

    private static readonly string[] _exportedMembers =
    [
        "createServer",
        "connect",
        "createSecureContext",
        "checkServerIdentity",
        "getCiphers",
        "Server",
        "TLSSocket",
        "DEFAULT_MIN_VERSION",
        "DEFAULT_MAX_VERSION",
        "rootCertificates"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "createServer" or "Server" => EmitCreateServer(emitter, arguments),
            "connect" => EmitConnect(emitter, arguments),
            "createSecureContext" => EmitCreateSecureContext(emitter, arguments),
            "checkServerIdentity" => EmitCheckServerIdentity(emitter, arguments),
            "getCiphers" => EmitNoArgCall(emitter, emitter.Context.Runtime!.TlsGetCiphers),
            "TLSSocket" => EmitCreateSocket(emitter),
            _ => false
        };
    }

    private static bool EmitCheckServerIdentity(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        for (int i = 0; i < 2; i++)
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
        var checkServerIdentity = ctx.Runtime!.GetBuiltInModuleMethod("tls", "checkServerIdentity")!;
        il.Emit(OpCodes.Call, checkServerIdentity);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitNoArgCall(IEmitterContext emitter, MethodBuilder method)
    {
        emitter.Context.IL.Emit(OpCodes.Call, method);
        emitter.SetStackUnknown();
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return propertyName switch
        {
            "DEFAULT_MIN_VERSION" => EmitConstantProperty(il, ctx.Runtime!.TlsGetDefaultMinVersion),
            "DEFAULT_MAX_VERSION" => EmitConstantProperty(il, ctx.Runtime!.TlsGetDefaultMaxVersion),
            "rootCertificates" => EmitConstantProperty(il, ctx.Runtime!.TlsRootCertificates),
            _ => false
        };
    }

    private static bool EmitConstantProperty(ILGenerator il, MethodBuilder method)
    {
        il.Emit(OpCodes.Call, method);
        return true;
    }

    private static bool EmitCreateServer(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit options argument (optional)
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

        // Call $Runtime.TlsCreateServer(options, callback)
        il.Emit(OpCodes.Call, ctx.Runtime!.TlsCreateServer);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitConnect(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit up to 4 arguments
        for (int i = 0; i < 4; i++)
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

        // Call $Runtime.TlsConnect(portOrOptions, hostOrCallback, optionsOrNull, callbackOrNull)
        il.Emit(OpCodes.Call, ctx.Runtime!.TlsConnect);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitCreateSecureContext(IEmitterContext emitter, List<Expr> arguments)
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

        il.Emit(OpCodes.Call, ctx.Runtime!.TlsCreateSecureContext);
        emitter.SetStackUnknown();
        return true;
    }

    private static bool EmitCreateSocket(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Call $Runtime.TlsCreateSocket() - creates a new $TlsSocket
        il.Emit(OpCodes.Call, ctx.Runtime!.TlsCreateSocket);
        emitter.SetStackUnknown();
        return true;
    }

    public bool IsExportedProperty(string memberName) =>
        memberName is "DEFAULT_MIN_VERSION" or "DEFAULT_MAX_VERSION" or "rootCertificates";
}
