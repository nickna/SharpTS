using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the stdlib-internal <c>primitive:net</c> seam.
/// </summary>
/// <remarks>
/// Provides TCP networking functionality:
/// - createServer() - creates a TCP server
/// - createConnection() - creates a TCP client socket
/// - createSocket() - creates an unconnected TCP socket
/// - createBlockList() - creates the native server-filtering handle
/// </remarks>
public sealed class NetModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:net";

    private static readonly string[] _exportedMembers =
    [
        "createServer",
        "createConnection",
        "createSocket",
        "createBlockList"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "createServer" => EmitCreateServer(emitter, arguments),
            "createConnection" => EmitCreateConnection(emitter, arguments),
            "createSocket" => EmitOneArgCall(emitter, arguments, emitter.Context.Runtime!.NetCreateSocket),
            "createBlockList" => EmitZeroArgCall(emitter, emitter.Context.Runtime!.NetCreateBlockList),
            _ => false
        };
    }

    private static bool EmitZeroArgCall(IEmitterContext emitter, System.Reflection.MethodInfo method)
    {
        emitter.Context.IL.Emit(OpCodes.Call, method);
        return true;
    }

    private static bool EmitOneArgCall(IEmitterContext emitter, List<Expr> arguments, System.Reflection.MethodInfo method)
    {
        var il = emitter.Context.IL;
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }
        il.Emit(OpCodes.Call, method);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        // Server and Socket are constructors, not simple properties
        return false;
    }

    private static bool EmitCreateServer(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Node signature: createServer([options][, connectionListener]).
        // Emit up to two positional args; missing ones are null.
        for (int i = 0; i < 2; i++)
        {
            if (arguments.Count > i)
            {
                emitter.EmitExpression(arguments[i]);
                emitter.EmitBoxIfNeeded(arguments[i]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        // Call $Runtime.NetCreateServer(optionsOrCallback, callback)
        il.Emit(OpCodes.Call, ctx.Runtime!.NetCreateServer);
        return true;
    }

    private static bool EmitCreateConnection(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Node signature: connect(options|port|path[, host][, connectListener]).
        // Emit up to three positional args; missing ones are null.
        for (int i = 0; i < 3; i++)
        {
            if (arguments.Count > i)
            {
                emitter.EmitExpression(arguments[i]);
                emitter.EmitBoxIfNeeded(arguments[i]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
        }

        // Call $Runtime.NetCreateConnection(options, hostOrCallback, callback)
        il.Emit(OpCodes.Call, ctx.Runtime!.NetCreateConnection);
        return true;
    }

    public bool IsExportedProperty(string memberName) => false;
}
