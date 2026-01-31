using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the Node.js 'dns' module.
/// </summary>
/// <remarks>
/// Provides DNS resolution methods. The lookup method uses System.Net.Dns
/// to resolve hostnames to IP addresses.
/// </remarks>
public sealed class DnsModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "dns";

    private static readonly string[] _exportedMembers =
    [
        "lookup", "lookupService",
        "ADDRCONFIG", "V4MAPPED", "ALL"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "lookup" => EmitLookup(emitter, arguments),
            "lookupService" => EmitLookupService(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return propertyName switch
        {
            "lookup" => EmitLookupProperty(emitter),
            "lookupService" => EmitLookupServiceProperty(emitter),
            "ADDRCONFIG" => EmitConstant(il, 1.0),
            "V4MAPPED" => EmitConstant(il, 2.0),
            "ALL" => EmitConstant(il, 4.0),
            _ => false
        };
    }

    private static bool EmitLookupProperty(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsGetLookup);
        return true;
    }

    private static bool EmitLookupServiceProperty(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsGetLookupService);
        return true;
    }

    private static bool EmitConstant(ILGenerator il, double value)
    {
        il.Emit(OpCodes.Ldc_R8, value);
        il.Emit(OpCodes.Box, typeof(double));
        return true;
    }

    /// <summary>
    /// Emits: dns.lookup(hostname[, options])
    /// </summary>
    private static bool EmitLookup(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit hostname (first argument)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit options (second argument) - can be number or object
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.DnsLookup(hostname, options)
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsLookup);

        return true;
    }

    /// <summary>
    /// Emits: dns.lookupService(address, port)
    /// </summary>
    private static bool EmitLookupService(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // Emit address (first argument)
        if (arguments.Count > 0)
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Emit port (second argument)
        if (arguments.Count > 1)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        // Call $Runtime.DnsLookupService(address, port)
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsLookupService);

        return true;
    }
}
