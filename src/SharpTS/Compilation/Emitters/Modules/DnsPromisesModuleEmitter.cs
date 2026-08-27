using System.Reflection.Emit;
using SharpTS.Parsing;

namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Emits IL code for the <c>primitive:dns/promises</c> host seam.
/// The user-facing module lives in <c>stdlib/node/dns/promises.ts</c>.
/// </summary>
public sealed class DnsPromisesModuleEmitter : IBuiltInModuleEmitter
{
    public string ModuleName => "primitive:dns/promises";

    private static readonly string[] _exportedMembers =
    [
        "lookup", "lookupService", "resolve", "resolve4", "resolve6", "reverse",
        "resolveMx", "resolveTxt", "resolveSrv", "resolveCname",
        "resolveNs", "resolveSoa", "resolvePtr", "resolveCaa", "resolveNaptr",
        "setDefaultResultOrder", "getDefaultResultOrder", "resolverResolve"
    ];

    public IReadOnlyList<string> GetExportedMembers() => _exportedMembers;

    public bool TryEmitMethodCall(IEmitterContext emitter, string methodName, List<Expr> arguments)
    {
        return methodName switch
        {
            "lookup" => EmitLookup(emitter, arguments),
            "lookupService" => EmitLookupService(emitter, arguments),
            "resolve" => EmitResolve(emitter, arguments),
            "resolve4" => EmitSingleArg(emitter, arguments, "DnsPromisesResolve4"),
            "resolve6" => EmitSingleArg(emitter, arguments, "DnsPromisesResolve6"),
            "reverse" => EmitSingleArg(emitter, arguments, "DnsPromisesReverse"),
            "resolveMx" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveMx"),
            "resolveTxt" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveTxt"),
            "resolveSrv" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveSrv"),
            "resolveCname" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveCname"),
            "resolveNs" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveNs"),
            "resolveSoa" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveSoa"),
            "resolvePtr" => EmitSingleArg(emitter, arguments, "DnsPromisesResolvePtr"),
            "resolveCaa" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveCaa"),
            "resolveNaptr" => EmitSingleArg(emitter, arguments, "DnsPromisesResolveNaptr"),
            "setDefaultResultOrder" => EmitSetDefaultResultOrder(emitter, arguments),
            "getDefaultResultOrder" => EmitGetDefaultResultOrder(emitter),
            "resolverResolve" => EmitResolverResolve(emitter, arguments),
            _ => false
        };
    }

    public bool TryEmitPropertyGet(IEmitterContext emitter, string propertyName)
    {
        return false;
    }

    /// <summary>Emits: dnsPromises.setDefaultResultOrder(order) — shared emitted state with dns (#1072)</summary>
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

    /// <summary>Emits: dnsPromises.getDefaultResultOrder() (#1072)</summary>
    private static bool EmitGetDefaultResultOrder(IEmitterContext emitter)
    {
        var ctx = emitter.Context;
        ctx.IL.Emit(OpCodes.Call, ctx.Runtime!.DnsGetDefaultResultOrder);
        return true;
    }

    /// <summary>Emits a call to the pure-IL $Runtime DNS promise wrapper.</summary>
    private static bool EmitSingleArg(IEmitterContext emitter, List<Expr> arguments, string runtimeMethod)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        if (arguments.Count == 0)
            il.Emit(OpCodes.Ldnull);
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }

        // Wrappers already return $Promise (they call WrapTaskAsPromise internally)
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsPromisesWrapperMethods[runtimeMethod]);
        return true;
    }

    /// <summary>Emits a call to the pure-IL $Runtime lookup promise wrapper.</summary>
    private static bool EmitLookup(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // hostname
        if (arguments.Count == 0)
            il.Emit(OpCodes.Ldnull);
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }

        // options
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
            il.Emit(OpCodes.Ldnull);

        // Wrapper already calls WrapTaskAsPromise internally
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsPromisesWrapperMethods["DnsPromisesLookup"]);
        return true;
    }

    private static bool EmitLookupService(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

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

        il.Emit(OpCodes.Call, ctx.Runtime!.DnsPromisesWrapperMethods["DnsPromisesLookupService"]);
        return true;
    }

    private static bool EmitResolverResolve(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        il.Emit(OpCodes.Ldc_I4_5);
        il.Emit(OpCodes.Newarr, ctx.Types.Object);
        for (int i = 0; i < 5; i++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, i);
            if (arguments.Count > i)
            {
                emitter.EmitExpression(arguments[i]);
                emitter.EmitBoxIfNeeded(arguments[i]);
            }
            else
            {
                il.Emit(OpCodes.Ldnull);
            }
            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Call, ctx.Runtime!.DnsPromisesWrapperMethods["DnsResolverResolveAsync"]);
        return true;
    }

    /// <summary>Emits a call to the pure-IL $Runtime resolve promise wrapper.</summary>
    private static bool EmitResolve(IEmitterContext emitter, List<Expr> arguments)
    {
        var ctx = emitter.Context;
        var il = ctx.IL;

        // hostname
        if (arguments.Count == 0)
            il.Emit(OpCodes.Ldnull);
        else
        {
            emitter.EmitExpression(arguments[0]);
            emitter.EmitBoxIfNeeded(arguments[0]);
        }

        // rrtype
        if (arguments.Count >= 2)
        {
            emitter.EmitExpression(arguments[1]);
            emitter.EmitBoxIfNeeded(arguments[1]);
        }
        else
            il.Emit(OpCodes.Ldnull);

        // Wrapper already calls WrapTaskAsPromise internally
        il.Emit(OpCodes.Call, ctx.Runtime!.DnsPromisesWrapperMethods["DnsPromisesResolve"]);
        return true;
    }
}
