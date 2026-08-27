using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the <c>primitive:dns</c> host seam.
/// </summary>
/// <remarks>
/// Provides DNS resolution methods. The lookup method uses System.Net.Dns
/// to resolve hostnames to IP addresses.
/// </remarks>
public sealed class DnsModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:dns";

    private static readonly string[] _exportedMembers =
    [
        "lookup", "lookupService",
        "resolve", "resolve4", "resolve6", "reverse",
        "resolveMx", "resolveTxt", "resolveSrv", "resolveCname", "resolveNs",
        "resolveSoa", "resolvePtr", "resolveCaa", "resolveNaptr",
        "promises", "Resolver", "createResolver",
        "resolverSetServers", "resolverGetServers", "resolverCancel", "resolverGetGeneration", "resolverSetLocalAddress",
        "setDefaultResultOrder", "getDefaultResultOrder",
        "ADDRCONFIG", "V4MAPPED", "ALL",
        "NODATA", "FORMERR", "SERVFAIL", "NOTFOUND", "NOTIMP", "REFUSED",
        "BADQUERY", "BADNAME", "BADFAMILY", "BADRESP", "CONNREFUSED", "TIMEOUT",
        "EOF", "FILE", "NOMEM", "DESTRUCTION", "BADSTR", "BADFLAGS",
        "NONAME", "BADHINTS", "NOTINITIALIZED", "LOADIPHLPAPI", "ADDRGETNETWORKPARAMS", "CANCELLED"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "lookup" => EmitLookup(emitter, arguments),
            "lookupService" => EmitLookupService(emitter, arguments),
            "setDefaultResultOrder" => EmitSetDefaultResultOrder(emitter, arguments),
            "getDefaultResultOrder" => EmitGetDefaultResultOrder(emitter),
            "createResolver" => EmitCreateResolver(emitter),
            "resolverSetServers" => EmitPrimitiveCall(emitter, arguments, emitter.Context.Runtime!.DnsResolverSetServers, 2),
            "resolverGetServers" => EmitPrimitiveCall(emitter, arguments, emitter.Context.Runtime!.DnsResolverGetServers, 1),
            "resolverCancel" => EmitPrimitiveCall(emitter, arguments, emitter.Context.Runtime!.DnsResolverCancel, 1),
            "resolverGetGeneration" => EmitPrimitiveCall(emitter, arguments, emitter.Context.Runtime!.DnsResolverGetGeneration, 1),
            "resolverSetLocalAddress" => EmitPrimitiveCall(emitter, arguments, emitter.Context.Runtime!.DnsResolverSetLocalAddress, 3),
            _ => false
        };
    }

    private static bool EmitCreateResolver(IEmitterContext emitter)
    {
        emitter.Context.IL.Emit(OpCodes.Call, emitter.Context.Runtime!.DnsResolverFactory);
        return true;
    }

    private static bool EmitPrimitiveCall(
        IEmitterContext emitter,
        List<Expr> arguments,
        System.Reflection.MethodInfo method,
        int arity)
    {
        var il = emitter.Context.IL;
        for (int i = 0; i < arity; i++)
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
        il.Emit(OpCodes.Call, method);
        return true;
    }

    /// <summary>Emits: dns.setDefaultResultOrder(order) (#1072)</summary>
    private static bool EmitSetDefaultResultOrder(IEmitterContext emitter, List<Expr> arguments)
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
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsSetDefaultResultOrder);
        return true;
    }

    /// <summary>Emits: dns.getDefaultResultOrder() (#1072)</summary>
    private static bool EmitGetDefaultResultOrder(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime!.DnsGetDefaultResultOrder);
        return true;
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        return propertyName switch
        {
            "lookup" => EmitLookupProperty(emitter),
            "lookupService" => EmitLookupServiceProperty(emitter),
            "promises" => EmitPromisesProperty(emitter),
            "ADDRCONFIG" => EmitConstant(il, 1.0),
            "V4MAPPED" => EmitConstant(il, 2.0),
            "ALL" => EmitConstant(il, 4.0),
            "NODATA" => EmitStringConstant(il, "ENODATA"),
            "FORMERR" => EmitStringConstant(il, "EFORMERR"),
            "SERVFAIL" => EmitStringConstant(il, "ESERVFAIL"),
            "NOTFOUND" => EmitStringConstant(il, "ENOTFOUND"),
            "NOTIMP" => EmitStringConstant(il, "ENOTIMP"),
            "REFUSED" => EmitStringConstant(il, "EREFUSED"),
            "BADQUERY" => EmitStringConstant(il, "EBADQUERY"),
            "BADNAME" => EmitStringConstant(il, "EBADNAME"),
            "BADFAMILY" => EmitStringConstant(il, "EBADFAMILY"),
            "BADRESP" => EmitStringConstant(il, "EBADRESP"),
            "CONNREFUSED" => EmitStringConstant(il, "ECONNREFUSED"),
            "TIMEOUT" => EmitStringConstant(il, "ETIMEOUT"),
            "EOF" => EmitStringConstant(il, "EEOF"),
            "FILE" => EmitStringConstant(il, "EFILE"),
            "NOMEM" => EmitStringConstant(il, "ENOMEM"),
            "DESTRUCTION" => EmitStringConstant(il, "EDESTRUCTION"),
            "BADSTR" => EmitStringConstant(il, "EBADSTR"),
            "BADFLAGS" => EmitStringConstant(il, "EBADFLAGS"),
            "NONAME" => EmitStringConstant(il, "ENONAME"),
            "BADHINTS" => EmitStringConstant(il, "EBADHINTS"),
            "NOTINITIALIZED" => EmitStringConstant(il, "ENOTINITIALIZED"),
            "LOADIPHLPAPI" => EmitStringConstant(il, "ELOADIPHLPAPI"),
            "ADDRGETNETWORKPARAMS" => EmitStringConstant(il, "EADDRGETNETWORKPARAMS"),
            "CANCELLED" => EmitStringConstant(il, "ECANCELLED"),
            "Resolver" => EmitResolverProperty(il),
            _ => false
        };
    }

    private static bool EmitPromisesProperty(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsGetPromisesNamespace);
        return true;
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

    private static bool EmitStringConstant(ILGenerator il, string value)
    {
        il.Emit(OpCodes.Ldstr, value);
        return true;
    }

    /// <summary>
    /// Emits a placeholder for dns.Resolver — actual instantiation via 'new dns.Resolver()'
    /// is handled by TryEmitModuleQualifiedConstructor → DnsResolverFactory.
    /// </summary>
    private static bool EmitResolverProperty(ILGenerator il)
    {
        il.Emit(OpCodes.Ldstr, "[DnsResolver]");
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
