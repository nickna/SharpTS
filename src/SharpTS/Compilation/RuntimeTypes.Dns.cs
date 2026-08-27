using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime;
using SharpTS.Runtime.BuiltIns.Modules;

namespace SharpTS.Compilation;

/// <summary>
/// RuntimeTypes helpers for dns/promises compiled support.
/// Called from emitted code via late-binding reflection.
/// Each method returns Task&lt;object?&gt; for wrapping in $Promise.
/// </summary>
public static partial class RuntimeTypes
{
    public static object DnsCreateResolverState() => new DnsResolverInstance();

    public static object? DnsResolverSetServers(object? state, object? servers)
    {
        RequireResolver(state).SetServers(ExtractStringArray(servers));
        return null;
    }

    public static object DnsResolverGetServers(object? state)
        => RequireResolver(state).GetServers().Select(server => (object?)server).ToList();

    public static object? DnsResolverCancel(object? state)
    {
        RequireResolver(state).Cancel();
        return null;
    }

    public static object DnsResolverGetGeneration(object? state)
        => (double)RequireResolver(state).CancelGeneration;

    public static object? DnsResolverSetLocalAddress(object? state, object? ipv4, object? ipv6)
    {
        RequireResolver(state).SetLocalAddress(ipv4 as string, ipv6 as string);
        return null;
    }

    /// <summary>
    /// Reflection target for the emitted resolver Promise primitive. The single
    /// request argument keeps the emitted async runner generic and standalone-safe.
    /// </summary>
    public static object? DnsResolverResolve(object? request)
    {
        var args = request switch
        {
            object?[] array => array,
            List<object?> list => list.ToArray(),
            _ => throw new NodeError("ERR_INVALID_ARG_TYPE", "Invalid DNS resolver request")
        };
        if (args.Length < 3)
            throw new NodeError("ERR_INVALID_ARG_VALUE", "Invalid DNS resolver request");

        var instance = RequireResolver(args[0]);
        var method = args[1]?.ToString() ?? "";
        var identifier = args[2]?.ToString() ?? "";
        var rrtype = args.Length > 3 ? args[3]?.ToString() : null;
        var expectedGeneration = args.Length > 4 && args[4] is not null
            ? Convert.ToInt64(args[4])
            : (long?)null;
        try
        {
            return instance.ResolveAsync(method, identifier, rrtype, expectedGeneration).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            var code = ex is NodeError nodeError ? nodeError.Code : ExtractDnsErrorCode(ex);
            return new Dictionary<string, object?>
            {
                ["__dnsError"] = true,
                ["name"] = "Error",
                ["message"] = ex.Message,
                ["code"] = code,
                ["hostname"] = identifier
            };
        }
    }

    private static DnsResolverInstance RequireResolver(object? state)
        => state as DnsResolverInstance
           ?? throw new NodeError("ERR_INVALID_ARG_TYPE", "Invalid DNS Resolver state");

    private static string ExtractDnsErrorCode(Exception ex)
    {
        var message = ex.Message;
        foreach (var code in new[] { "ECANCELLED", "ENOTFOUND", "ENODATA", "ETIMEOUT", "ECONNREFUSED" })
            if (message.Contains(code, StringComparison.Ordinal)) return code;
        return "EAI_FAIL";
    }

    public static Task<object?> DnsPromisesLookup(object? hostname, object? options)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() =>
        {
            var entry = Dns.GetHostEntry(h);
            AddressFamily? family = null;
            if (options is double d) family = (int)d == 6 ? AddressFamily.InterNetworkV6 : AddressFamily.InterNetwork;

            IPAddress? addr;
            if (family != null)
            {
                addr = entry.AddressList.FirstOrDefault(a => a.AddressFamily == family);
            }
            else
            {
                // Default result order (#1072): the emitted setter syncs
                // DnsConfig via late-binding, so this observes guest updates.
                addr = Runtime.BuiltIns.Modules.DnsConfig.DefaultResultOrder switch
                {
                    "ipv4first" => entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
                                   ?? entry.AddressList.FirstOrDefault(),
                    "ipv6first" => entry.AddressList.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetworkV6)
                                   ?? entry.AddressList.FirstOrDefault(),
                    _ => entry.AddressList.FirstOrDefault()
                };
            }

            if (addr == null) throw new SocketException((int)SocketError.HostNotFound);

            return (object?)new Dictionary<string, object?>
            {
                ["address"] = addr.ToString(),
                ["family"] = (double)(addr.AddressFamily == AddressFamily.InterNetworkV6 ? 6 : 4)
            };
        });
    }

    /// <summary>
    /// Late-binding bridge for the emitted dns.setDefaultResultOrder (#1072): keeps
    /// the SharpTS.dll-side DnsConfig in sync with the emitted static so the
    /// Resolver/promises paths (which run in SharpTS.dll) observe guest updates.
    /// </summary>
    public static object? DnsSetDefaultResultOrder(object? order)
    {
        if (order is string s && Runtime.BuiltIns.Modules.DnsConfig.ValidResultOrders.Contains(s))
            Runtime.BuiltIns.Modules.DnsConfig.SetDefaultResultOrder(s);
        return null;
    }

    /// <summary>Late-binding bridge mirror of dns.getDefaultResultOrder (#1072).</summary>
    public static object? DnsGetDefaultResultOrder()
        => Runtime.BuiltIns.Modules.DnsConfig.DefaultResultOrder;

    public static Task<object?> DnsPromisesResolve(object? hostname, object? rrtype)
    {
        var h = hostname?.ToString() ?? "";
        var rt = rrtype?.ToString() ?? "A";
        return Task.Run<object?>(() => DnsRecordResolver.Resolve(h, rt));
    }

    public static Task<object?> DnsPromisesResolve4(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveA(h));
    }

    public static Task<object?> DnsPromisesResolve6(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveAaaa(h));
    }

    public static Task<object?> DnsPromisesReverse(object? ip)
    {
        var ipStr = ip?.ToString() ?? "";
        return Task.Run<object?>(() =>
        {
            if (!IPAddress.TryParse(ipStr, out var addr))
                throw new Exception($"dns.reverse: invalid address {ipStr}");
            var entry = Dns.GetHostEntry(addr);
            return (object?)new List<object?> { entry.HostName };
        });
    }

    public static Task<object?> DnsPromisesResolveMx(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveMx(h));
    }

    public static Task<object?> DnsPromisesResolveTxt(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveTxt(h));
    }

    public static Task<object?> DnsPromisesResolveSrv(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveSrv(h));
    }

    public static Task<object?> DnsPromisesResolveCname(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveCname(h));
    }

    public static Task<object?> DnsPromisesResolveNs(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveNs(h));
    }

    public static Task<object?> DnsPromisesResolveSoa(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveSoa(h));
    }

    public static Task<object?> DnsPromisesResolvePtr(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolvePtr(h));
    }

    public static Task<object?> DnsPromisesResolveCaa(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveCaa(h));
    }

    public static Task<object?> DnsPromisesResolveNaptr(object? hostname)
    {
        var h = hostname?.ToString() ?? "";
        return Task.Run<object?>(() => (object?)DnsRecordResolver.ResolveNaptr(h));
    }

    /// <summary>
    /// Factory for dns.Resolver in compiled mode.
    /// Returns a Dictionary&lt;string,object?&gt; with all resolver methods bound to a shared DnsResolverInstance.
    /// </summary>
    public static Dictionary<string, object?> DnsCreateResolver()
    {
        var instance = new DnsResolverInstance();
        return new Dictionary<string, object?>
        {
            ["setServers"] = (Func<object?[], object?>)(args =>
            {
                var servers = ExtractStringArray(args[0]);
                instance.SetServers(servers);
                return null;
            }),
            ["getServers"] = (Func<object?[], object?>)(_ =>
            {
                var servers = instance.GetServers();
                return servers.Select(s => (object?)s).ToList();
            }),
            ["resolve"] = (Func<object?[], object?>)(args =>
            {
                var hostname = args[0]?.ToString() ?? "";
                string rrtype = "A";
                if (args.Length > 2 && args[1] is string rt) rrtype = rt;
                var callback = args[^1];
                DnsAsyncInvoke(callback, () => instance.Resolve(hostname, rrtype), hostname);
                return null;
            }),
            ["resolve4"] = (Func<object?[], object?>)(args =>
            {
                var hostname = args[0]?.ToString() ?? "";
                var callback = args[^1];
                DnsAsyncInvoke(callback, () => (object)instance.Resolve4(hostname), hostname);
                return null;
            }),
            ["resolve6"] = (Func<object?[], object?>)(args =>
            {
                var hostname = args[0]?.ToString() ?? "";
                var callback = args[^1];
                DnsAsyncInvoke(callback, () => (object)instance.Resolve6(hostname), hostname);
                return null;
            }),
            ["reverse"] = (Func<object?[], object?>)(args =>
            {
                var ip = args[0]?.ToString() ?? "";
                var callback = args[^1];
                DnsAsyncInvoke(callback, () => (object)instance.Reverse(ip), ip);
                return null;
            }),
            ["resolveMx"] = DnsRecordMethod(instance, h => instance.ResolveMx(h)),
            ["resolveTxt"] = DnsRecordMethod(instance, h => instance.ResolveTxt(h)),
            ["resolveSrv"] = DnsRecordMethod(instance, h => instance.ResolveSrv(h)),
            ["resolveCname"] = DnsRecordMethod(instance, h => instance.ResolveCname(h)),
            ["resolveNs"] = DnsRecordMethod(instance, h => instance.ResolveNs(h)),
            ["resolveSoa"] = DnsRecordMethod(instance, h => instance.ResolveSoa(h)),
            ["resolvePtr"] = DnsRecordMethod(instance, h => instance.ResolvePtr(h)),
            ["resolveCaa"] = DnsRecordMethod(instance, h => instance.ResolveCaa(h)),
            ["resolveNaptr"] = DnsRecordMethod(instance, h => instance.ResolveNaptr(h)),
            // cancel() aborts the instance's outstanding queries (#1072). Compiled-mode
            // resolver callbacks run inline-synchronously, so there is never a pending
            // query when guest code runs — prompt mid-flight rejection is
            // interpreter-only (documented deviation; cf. the tls/http compiled
            // deferral precedent).
            ["cancel"] = (Func<object?[], object?>)(_ =>
            {
                instance.Cancel();
                return null;
            }),
            ["setLocalAddress"] = (Func<object?[], object?>)(args =>
            {
                string? v4 = args.Length > 0 ? args[0] as string : null;
                string? v6 = args.Length > 1 ? args[1] as string : null;
                instance.SetLocalAddress(v4, v6);
                return null;
            })
        };
    }

    private static Func<object?[], object?> DnsRecordMethod(DnsResolverInstance instance, Func<string, object> resolve)
    {
        return args =>
        {
            var hostname = args[0]?.ToString() ?? "";
            var callback = args[^1];
            DnsAsyncInvoke(callback, () => resolve(hostname), hostname);
            return null;
        };
    }

    /// <summary>
    /// Invokes a DNS resolve callback synchronously (matching compiled mode pattern
    /// where callbacks are called inline after resolution).
    /// Handles both Delegate (Func&lt;&gt;) and emitted TSFunction types.
    /// </summary>
    private static void DnsAsyncInvoke(object? callback, Func<object> resolve, string identifier)
    {
        if (callback == null) return;
        try
        {
            var result = resolve();
            InvokeCallback(callback, new object?[] { null, result });
        }
        catch (Exception ex)
        {
            var code = ex.Message.Contains("ENOTFOUND") ? "ENOTFOUND" :
                       ex.Message.Contains("ETIMEOUT") ? "ETIMEOUT" :
                       ex.Message.Contains("ENODATA") ? "ENODATA" : "EAI_FAIL";
            var err = new Dictionary<string, object?>
            {
                ["code"] = code,
                ["hostname"] = identifier,
                ["message"] = $"query {code} {identifier}"
            };
            InvokeCallback(callback, new object?[] { err, null });
        }
    }

    /// <summary>
    /// Invokes a callback that may be a Delegate (interpreter) or an emitted TSFunction (compiled).
    /// TSFunction has an Invoke(object?[]) method found via reflection.
    /// </summary>
    private static void InvokeCallback(object callback, object?[] args)
    {
        if (RuntimeCallableDispatcher.IsCallable(callback))
            RuntimeCallableDispatcher.Invoke(null, callback, args);
    }

    private static string[] ExtractStringArray(object? value)
    {
        if (value is List<object?> list)
            return list.Select(e => e?.ToString() ?? "").ToArray();
        if (value is object?[] arr)
            return arr.Select(e => e?.ToString() ?? "").ToArray();
        throw new Exception("Runtime Error: dns.setServers requires an array of strings");
    }
}
