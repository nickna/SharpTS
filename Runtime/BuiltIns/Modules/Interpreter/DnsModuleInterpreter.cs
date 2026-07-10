using System.Net;
using System.Net.Sockets;
using SharpTS.Compilation;
using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'dns' module.
/// </summary>
/// <remarks>
/// Provides synchronous DNS resolution methods.
/// In Node.js, dns.lookup uses the OS resolver and is typically callback-based.
/// SharpTS implements synchronous versions that return results directly.
/// Record-type resolution (MX, TXT, SRV, etc.) delegates to <see cref="DnsRecordResolver"/>.
/// </remarks>
public static class DnsModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the dns module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            // Sync/callback methods
            ["lookup"] = BuiltInMethod.CreateV2("lookup", 1, 3, Lookup),
            ["lookupService"] = BuiltInMethod.CreateV2("lookupService", 2, 3, LookupService),

            // Async (callback-based) DNS resolution
            ["resolve"] = BuiltInMethod.CreateV2("resolve", 2, 3, ResolveAsync),
            ["resolve4"] = BuiltInMethod.CreateV2("resolve4", 2, Resolve4Async),
            ["resolve6"] = BuiltInMethod.CreateV2("resolve6", 2, Resolve6Async),
            ["reverse"] = BuiltInMethod.CreateV2("reverse", 2, ReverseAsync),
            ["resolveMx"] = BuiltInMethod.CreateV2("resolveMx", 2, ResolveMxAsync),
            ["resolveTxt"] = BuiltInMethod.CreateV2("resolveTxt", 2, ResolveTxtAsync),
            ["resolveSrv"] = BuiltInMethod.CreateV2("resolveSrv", 2, ResolveSrvAsync),
            ["resolveCname"] = BuiltInMethod.CreateV2("resolveCname", 2, ResolveCnameAsync),
            ["resolveNs"] = BuiltInMethod.CreateV2("resolveNs", 2, ResolveNsAsync),
            ["resolveSoa"] = BuiltInMethod.CreateV2("resolveSoa", 2, ResolveSoaAsync),
            ["resolvePtr"] = BuiltInMethod.CreateV2("resolvePtr", 2, ResolvePtrAsync),
            ["resolveCaa"] = BuiltInMethod.CreateV2("resolveCaa", 2, ResolveCaaAsync),
            ["resolveNaptr"] = BuiltInMethod.CreateV2("resolveNaptr", 2, ResolveNaptrAsync),

            // Promises API
            ["promises"] = new SharpTSObject(GetPromisesExports()),

            // Resolver class constructor
            ["Resolver"] = BuiltInMethod.CreateV2("Resolver", 0, 1, CreateResolver),

            // Default lookup result order (#1072)
            ["setDefaultResultOrder"] = BuiltInMethod.CreateV2("setDefaultResultOrder", 1, SetDefaultResultOrder),
            ["getDefaultResultOrder"] = BuiltInMethod.CreateV2("getDefaultResultOrder", 0, GetDefaultResultOrder),

            // Constants
            ["ADDRCONFIG"] = (double)1,
            ["V4MAPPED"] = (double)2,
            ["ALL"] = (double)4,
            ["NODATA"] = "ENODATA",
            ["FORMERR"] = "EFORMERR",
            ["SERVFAIL"] = "ESERVFAIL",
            ["NOTFOUND"] = "ENOTFOUND",
            ["NOTIMP"] = "ENOTIMP",
            ["REFUSED"] = "EREFUSED",
            ["BADQUERY"] = "EBADQUERY",
            ["BADNAME"] = "EBADNAME",
            ["BADFAMILY"] = "EBADFAMILY",
            ["BADRESP"] = "EBADRESP",
            ["CONNREFUSED"] = "ECONNREFUSED",
            ["TIMEOUT"] = "ETIMEOUT",
            ["EOF"] = "EEOF",
            ["FILE"] = "EFILE",
            ["NOMEM"] = "ENOMEM",
            ["DESTRUCTION"] = "EDESTRUCTION",
            ["BADSTR"] = "EBADSTR",
            ["BADFLAGS"] = "EBADFLAGS",
            ["NONAME"] = "ENONAME",
            ["BADHINTS"] = "EBADHINTS",
            ["NOTINITIALIZED"] = "ENOTINITIALIZED",
            ["LOADIPHLPAPI"] = "ELOADIPHLPAPI",
            ["ADDRGETNETWORKPARAMS"] = "EADDRGETNETWORKPARAMS",
            ["CANCELLED"] = "ECANCELLED"
        };
    }

    /// <summary>
    /// Gets exported values for dns.promises module.
    /// </summary>
    public static Dictionary<string, object?> GetPromisesExports()
    {
        return new Dictionary<string, object?>
        {
            ["lookup"] = new BuiltInAsyncMethod("lookup", 1, 2, LookupPromise),
            ["resolve"] = new BuiltInAsyncMethod("resolve", 1, 2, ResolvePromise),
            ["resolve4"] = new BuiltInAsyncMethod("resolve4", 1, 1, Resolve4Promise),
            ["resolve6"] = new BuiltInAsyncMethod("resolve6", 1, 1, Resolve6Promise),
            ["reverse"] = new BuiltInAsyncMethod("reverse", 1, 1, ReversePromise),
            ["resolveMx"] = new BuiltInAsyncMethod("resolveMx", 1, 1, ResolveMxPromise),
            ["resolveTxt"] = new BuiltInAsyncMethod("resolveTxt", 1, 1, ResolveTxtPromise),
            ["resolveSrv"] = new BuiltInAsyncMethod("resolveSrv", 1, 1, ResolveSrvPromise),
            ["resolveCname"] = new BuiltInAsyncMethod("resolveCname", 1, 1, ResolveCnamePromise),
            ["resolveNs"] = new BuiltInAsyncMethod("resolveNs", 1, 1, ResolveNsPromise),
            ["resolveSoa"] = new BuiltInAsyncMethod("resolveSoa", 1, 1, ResolveSoaPromise),
            ["resolvePtr"] = new BuiltInAsyncMethod("resolvePtr", 1, 1, ResolvePtrPromise),
            ["resolveCaa"] = new BuiltInAsyncMethod("resolveCaa", 1, 1, ResolveCaaPromise),
            ["resolveNaptr"] = new BuiltInAsyncMethod("resolveNaptr", 1, 1, ResolveNaptrPromise),
            // Shared with the callback API (#1072) — Node's dnsPromises mirrors these.
            ["setDefaultResultOrder"] = BuiltInMethod.CreateV2("setDefaultResultOrder", 1, SetDefaultResultOrder),
            ["getDefaultResultOrder"] = BuiltInMethod.CreateV2("getDefaultResultOrder", 0, GetDefaultResultOrder)
        };
    }

    /// <summary>
    /// dns.setDefaultResultOrder(order) — 'ipv4first' | 'ipv6first' | 'verbatim' (#1072).
    /// Affects the address selection of dns.lookup / dnsPromises.lookup.
    /// </summary>
    private static RuntimeValue SetDefaultResultOrder(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (!args[0].IsString)
            throw new NodeError("ERR_INVALID_ARG_VALUE", "The argument 'order' must be one of: 'ipv4first', 'ipv6first', 'verbatim'");
        DnsConfig.SetDefaultResultOrder(args[0].AsStringUnsafe());
        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// dns.getDefaultResultOrder() (#1072).
    /// </summary>
    private static RuntimeValue GetDefaultResultOrder(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromString(DnsConfig.DefaultResultOrder);
    }

    /// <summary>
    /// dns.lookup(hostname[, options][, callback]) - Resolves a hostname to an IP address.
    /// </summary>
    /// <remarks>
    /// With a callback (the Node contract), resolution runs off-thread and the
    /// callback is invoked asynchronously with (err, address, family) — or
    /// (err, addresses) when options.all is set — with Ref/Unref keeping the
    /// event loop alive (#206). Without a callback (no such form in Node) the
    /// legacy synchronous direct-return behavior is preserved.
    /// </remarks>
    private static RuntimeValue Lookup(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            throw new Exception("Runtime Error: dns.lookup requires a hostname string");
        var hostname = args[0].AsStringUnsafe();

        // Parse options
        int family = 0; // 0 = any, 4 = IPv4 only, 6 = IPv6 only
        bool all = false;
        string? order = null; // per-call result order (#1072); null = module default

        if (args.Length > 1 && !args[1].IsNull)
        {
            if (args[1].IsNumber)
            {
                family = (int)args[1].AsNumberUnsafe();
            }
            else if (args[1].ToObject() is SharpTSObject options)
            {
                if (options.Fields.TryGetValue("family", out var familyVal) && familyVal is double f)
                    family = (int)f;
                if (options.Fields.TryGetValue("all", out var allVal))
                    all = IsTruthy(allVal);
                if (options.Fields.TryGetValue("order", out var orderVal) && orderVal is string o)
                    order = o;
                else if (options.Fields.TryGetValue("verbatim", out var verbatimVal) && verbatimVal is bool v)
                    order = v ? "verbatim" : "ipv4first"; // legacy boolean form
            }
        }

        var callback = args[^1].ToObject() as ISharpTSCallable;
        if (callback == null)
            return RuntimeValue.FromBoxed(LookupCore(hostname, family, all, order));

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = LookupCore(hostname, family, all, order);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    if (result is SharpTSObject single)
                        interpreter.InvokeGuestCallback(callback,
                            [null, single.Fields["address"], single.Fields["family"]]);
                    else
                        interpreter.InvokeGuestCallback(callback, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback,
                        [CreateDnsError(ExtractErrorCode(ex), hostname), null, null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });
        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Shared dns.lookup resolution: returns a single {address, family} object,
    /// or an array of them when <paramref name="all"/> is set.
    /// </summary>
    private static object? LookupCore(string hostname, int family, bool all, string? order = null)
    {
        try
        {
            var hostEntry = Dns.GetHostEntry(hostname);
            var addresses = hostEntry.AddressList;

            // Filter by family if specified
            if (family == 4)
                addresses = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            else if (family == 6)
                addresses = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToArray();
            else
            {
                // Result order (#1072): per-call option, else the module default.
                // Stable partition so relative resolver order is preserved.
                var effectiveOrder = order ?? DnsConfig.DefaultResultOrder;
                if (effectiveOrder == "ipv4first")
                    addresses = [.. addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)];
                else if (effectiveOrder == "ipv6first")
                    addresses = [.. addresses.OrderBy(a => a.AddressFamily == AddressFamily.InterNetworkV6 ? 0 : 1)];
            }

            if (addresses.Length == 0)
            {
                throw new Exception($"Runtime Error: dns.lookup ENOTFOUND {hostname}");
            }

            if (all)
            {
                // Return array of all addresses
                var results = new List<object?>();
                foreach (var addr in addresses)
                {
                    var fields = new Dictionary<string, object?>
                    {
                        ["address"] = addr.ToString(),
                        ["family"] = addr.AddressFamily == AddressFamily.InterNetwork ? 4.0 : 6.0
                    };
                    results.Add(new SharpTSObject(fields));
                }
                return new SharpTSArray(results);
            }
            else
            {
                // Return first matching address
                var addr = addresses[0];
                var fields = new Dictionary<string, object?>
                {
                    ["address"] = addr.ToString(),
                    ["family"] = addr.AddressFamily == AddressFamily.InterNetwork ? 4.0 : 6.0
                };
                return new SharpTSObject(fields);
            }
        }
        catch (SocketException ex)
        {
            throw new Exception($"Runtime Error: dns.lookup {GetErrorCode(ex)} {hostname}");
        }
    }

    /// <summary>
    /// dns.lookupService(address, port[, callback]) - Resolves address and port to hostname and service.
    /// </summary>
    /// <remarks>
    /// With a callback (the Node contract), resolution runs off-thread and the
    /// callback receives (err, hostname, service) with Ref/Unref keeping the
    /// event loop alive (#206). Without a callback the legacy synchronous
    /// direct-return form is preserved.
    /// </remarks>
    private static RuntimeValue LookupService(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length < 2)
            throw new Exception("Runtime Error: dns.lookupService requires address and port");

        if (!args[0].IsString)
            throw new Exception("Runtime Error: dns.lookupService address must be a string");
        var address = args[0].AsStringUnsafe();

        if (!args[1].IsNumber)
            throw new Exception("Runtime Error: dns.lookupService port must be a number");
        var portNum = args[1].AsNumberUnsafe();

        int port = (int)portNum;

        var callback = args[^1].ToObject() as ISharpTSCallable;
        if (callback == null)
            return RuntimeValue.FromObject(LookupServiceCore(address, port));

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = LookupServiceCore(address, port);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback,
                        [null, result.Fields["hostname"], result.Fields["service"]]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback,
                        [CreateDnsError(ExtractErrorCode(ex), address), null, null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });
        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Shared dns.lookupService resolution: returns a {hostname, service} object.
    /// </summary>
    private static SharpTSObject LookupServiceCore(string address, int port)
    {
        try
        {
            // Parse the IP address
            if (!IPAddress.TryParse(address, out var ipAddress))
                throw new Exception($"Runtime Error: dns.lookupService invalid address {address}");

            // Reverse DNS lookup
            var hostEntry = Dns.GetHostEntry(ipAddress);

            var fields = new Dictionary<string, object?>
            {
                ["hostname"] = hostEntry.HostName,
                // Note: .NET doesn't have built-in service name lookup, so we just return the port
                ["service"] = port.ToString()
            };
            return new SharpTSObject(fields);
        }
        catch (SocketException ex)
        {
            throw new Exception($"Runtime Error: dns.lookupService {GetErrorCode(ex)} {address}");
        }
    }

    private static bool IsTruthy(object? value) => RuntimeTypes.IsTruthy(value);

    #region Resolver

    /// <summary>
    /// Creates a dns.Resolver instance — an object with resolve methods that use
    /// configurable DNS servers via setServers()/getServers().
    /// </summary>
    private static RuntimeValue CreateResolver(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var instance = new DnsResolverInstance();
        var fields = new Dictionary<string, object?>
        {
            ["setServers"] = BuiltInMethod.CreateV2("setServers", 1, 1, (interp, _, a) =>
            {
                var servers = ExtractStringArray(a[0].ToObject());
                instance.SetServers(servers);
                return RuntimeValue.Undefined;
            }),
            ["getServers"] = BuiltInMethod.CreateV2("getServers", 0, 0, (interp, _, _) =>
            {
                var servers = instance.GetServers();
                return RuntimeValue.FromObject(new SharpTSArray(servers.Select(s => (object?)s).ToList()));
            }),
            ["resolve"] = BuiltInMethod.CreateV2("resolve", 2, 3, (interp, _, a) =>
                ResolverResolveAsync(interp, instance, a)),
            ["resolve4"] = BuiltInMethod.CreateV2("resolve4", 2, 2, (interp, _, a) =>
                ResolverResolveByFamilyAsync(interp, instance, "resolve4", a, 4)),
            ["resolve6"] = BuiltInMethod.CreateV2("resolve6", 2, 2, (interp, _, a) =>
                ResolverResolveByFamilyAsync(interp, instance, "resolve6", a, 6)),
            ["reverse"] = BuiltInMethod.CreateV2("reverse", 2, 2, (interp, _, a) =>
                ResolverReverseAsync(interp, instance, a)),
            ["resolveMx"] = BuiltInMethod.CreateV2("resolveMx", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveMx", a, r => r.ResolveMx)),
            ["resolveTxt"] = BuiltInMethod.CreateV2("resolveTxt", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveTxt", a, r => r.ResolveTxt)),
            ["resolveSrv"] = BuiltInMethod.CreateV2("resolveSrv", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveSrv", a, r => r.ResolveSrv)),
            ["resolveCname"] = BuiltInMethod.CreateV2("resolveCname", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveCname", a, r => r.ResolveCname)),
            ["resolveNs"] = BuiltInMethod.CreateV2("resolveNs", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveNs", a, r => r.ResolveNs)),
            ["resolveSoa"] = BuiltInMethod.CreateV2("resolveSoa", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveSoa", a, r => r.ResolveSoa)),
            ["resolvePtr"] = BuiltInMethod.CreateV2("resolvePtr", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolvePtr", a, r => r.ResolvePtr)),
            ["resolveCaa"] = BuiltInMethod.CreateV2("resolveCaa", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveCaa", a, r => r.ResolveCaa)),
            ["resolveNaptr"] = BuiltInMethod.CreateV2("resolveNaptr", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveNaptr", a, r => r.ResolveNaptr)),
            // Real cancel (#1072): all outstanding queries reject with ECANCELLED.
            // Cooperative — the in-flight wire query itself runs to its timeout in
            // the background, but delivery is aborted promptly.
            ["cancel"] = BuiltInMethod.CreateV2("cancel", 0, 0, (_, _, _) =>
            {
                instance.Cancel();
                return RuntimeValue.Undefined;
            }),
            ["setLocalAddress"] = BuiltInMethod.CreateV2("setLocalAddress", 0, 2, (interp, _, a) =>
            {
                string? v4 = a.Length > 0 && a[0].IsString ? a[0].AsStringUnsafe() : null;
                string? v6 = a.Length > 1 && a[1].IsString ? a[1].AsStringUnsafe() : null;
                instance.SetLocalAddress(v4, v6);
                return RuntimeValue.Undefined;
            })
        };
        return RuntimeValue.FromObject(new SharpTSObject(fields));
    }

    /// <summary>
    /// Runs a resolver query off-thread with cooperative cancellation (#1072):
    /// the result/error is delivered via the callback on the event loop, and a
    /// Resolver.cancel() while the query is outstanding rejects it promptly with
    /// ECANCELLED (the orphaned wire query runs out its timeout in the background).
    /// </summary>
    private static RuntimeValue ResolverRunCancellable(Interp interpreter, DnsResolverInstance instance,
        string identifier, ISharpTSCallable callback, Func<object?> query, Func<object?, object?> wrap)
    {
        interpreter.Ref();
        var token = instance.CancellationToken;
        _ = Task.Run(async () =>
        {
            try
            {
                var queryTask = Task.Run(query);
                var cancelTask = Task.Delay(Timeout.Infinite, token);
                var completed = await Task.WhenAny(queryTask, cancelTask);
                if (completed != queryTask)
                    throw new OperationCanceledException(token);
                var raw = await queryTask;
                var result = wrap(raw);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (OperationCanceledException)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError("ECANCELLED", identifier), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError(ExtractErrorCode(ex), identifier), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });
        return RuntimeValue.Undefined;
    }

    private static string[] ExtractStringArray(object? value)
    {
        if (value is SharpTSArray arr)
            return arr.Select(e => e?.ToString() ?? "").ToArray();
        if (value is List<object?> list)
            return list.Select(e => e?.ToString() ?? "").ToArray();
        throw new Exception("Runtime Error: dns.setServers requires an array of strings");
    }

    private static RuntimeValue ResolverResolveAsync(Interp interpreter, DnsResolverInstance instance, ReadOnlySpan<RuntimeValue> args)
    {
        var hostname = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve callback is required");
        string rrtype = "A";
        if (args.Length > 2 && args[1].IsString) rrtype = args[1].AsStringUnsafe();

        return ResolverRunCancellable(interpreter, instance, hostname, callback,
            () => instance.Resolve(hostname, rrtype), WrapDnsResult);
    }

    private static RuntimeValue ResolverResolveByFamilyAsync(Interp interpreter, DnsResolverInstance instance,
        string methodName, ReadOnlySpan<RuntimeValue> args, int family)
    {
        var hostname = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception($"Runtime Error: dns.{methodName} callback is required");

        return ResolverRunCancellable(interpreter, instance, hostname, callback,
            () => family == 4 ? instance.Resolve4(hostname) : instance.Resolve6(hostname),
            raw => new SharpTSArray((List<object?>)raw!));
    }

    private static RuntimeValue ResolverReverseAsync(Interp interpreter, DnsResolverInstance instance, ReadOnlySpan<RuntimeValue> args)
    {
        var ip = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.reverse callback is required");

        return ResolverRunCancellable(interpreter, instance, ip, callback,
            () => instance.Reverse(ip),
            raw => new SharpTSArray((List<object?>)raw!));
    }

    private static RuntimeValue ResolverRecordAsync(Interp interpreter, DnsResolverInstance instance,
        string methodName, ReadOnlySpan<RuntimeValue> args, Func<DnsResolverInstance, Func<string, object>> resolveSelector)
    {
        var hostname = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception($"Runtime Error: dns.{methodName} callback is required");

        var resolveFunc = resolveSelector(instance);
        return ResolverRunCancellable(interpreter, instance, hostname, callback,
            () => resolveFunc(hostname), WrapDnsResult);
    }

    #endregion

    private static string GetErrorCode(SocketException ex)
    {
        return ex.SocketErrorCode switch
        {
            SocketError.HostNotFound => "ENOTFOUND",
            SocketError.NoData => "ENODATA",
            SocketError.TryAgain => "EAGAIN",
            SocketError.NoRecovery => "ESERVFAIL",
            SocketError.TimedOut => "ETIMEDOUT",
            SocketError.ConnectionRefused => "ECONNREFUSED",
            _ => "EAI_FAIL"
        };
    }

    private static SharpTSObject CreateDnsError(string code, string hostname)
    {
        return new SharpTSObject(new Dictionary<string, object?>
        {
            ["code"] = code,
            ["hostname"] = hostname,
            ["message"] = $"queryA {code} {hostname}"
        });
    }

    private static SharpTSArray ResolveAddresses(string hostname, AddressFamily? family)
    {
        // resolve4/resolve6 go through the DNS wire protocol (honors SHARPTS_DNS_SERVER,
        // Node/c-ares semantics — no hosts-file lookup). dns.lookup stays on
        // Dns.GetHostEntry. DnsWireProtocol.ParseResponse throws on NXDOMAIN/ENODATA.
        var queryType = family == AddressFamily.InterNetworkV6
            ? DnsWireProtocol.TypeAAAA
            : DnsWireProtocol.TypeA;
        return new SharpTSArray((List<object?>)DnsWireProtocol.Query(hostname, queryType));
    }

    /// <summary>
    /// Wraps raw DNS resolution results from DnsRecordResolver into SharpTS types.
    /// List&lt;object?&gt; of dicts → SharpTSArray of SharpTSObjects,
    /// List&lt;object?&gt; of lists (TXT) → SharpTSArray of SharpTSArrays,
    /// List&lt;object?&gt; of strings → SharpTSArray,
    /// Dictionary → SharpTSObject.
    /// </summary>
    private static object? WrapDnsResult(object? raw)
    {
        if (raw is Dictionary<string, object?> dict)
            return new SharpTSObject(dict);

        if (raw is List<object?> list)
        {
            var wrapped = list.Select<object?, object?>(item => item switch
            {
                Dictionary<string, object?> d => new SharpTSObject(d),
                List<object?> inner => new SharpTSArray(inner),
                _ => item
            }).ToList();
            return new SharpTSArray(wrapped);
        }

        return raw;
    }

    #region Async Callback-based DNS Resolution

    /// <summary>
    /// Generic helper for callback-based async DNS resolution using DnsRecordResolver.
    /// </summary>
    private static RuntimeValue ResolveRecordAsync(Interp interpreter, string methodName, ReadOnlySpan<RuntimeValue> args,
        Func<string, object> resolveFunc)
    {
        var hostname = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception($"Runtime Error: dns.{methodName} callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var raw = resolveFunc(hostname);
                var result = WrapDnsResult(raw);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// Extracts the DNS error code from an exception message.
    /// </summary>
    private static string ExtractErrorCode(Exception ex)
    {
        // Wire-protocol errors carry the code directly (#1073) — deterministic,
        // no message parsing needed.
        if (ex is NodeError nodeError)
            return nodeError.Code;

        var msg = ex.Message;
        // Legacy fallback: error messages follow "Runtime Error: dns.xxx ECODE hostname"
        if (msg.StartsWith("Runtime Error:"))
        {
            var parts = msg.Split(' ');
            if (parts.Length >= 4)
                return parts[3]; // The error code
        }
        if (ex is SocketException sex)
            return GetErrorCode(sex);
        return "EAI_FAIL";
    }

    /// <summary>
    /// dns.resolve(hostname[, rrtype], callback) - Resolve hostname using specified record type.
    /// </summary>
    private static RuntimeValue ResolveAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var hostname = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve callback is required");

        // Optional rrtype (default 'A')
        string rrtype = "A";
        if (args.Length > 2 && args[1].IsString)
            rrtype = args[1].AsStringUnsafe();

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var raw = DnsRecordResolver.Resolve(hostname, rrtype);
                var result = WrapDnsResult(raw);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// dns.resolve4(hostname, callback) - Resolve hostname to IPv4 addresses.
    /// </summary>
    private static RuntimeValue Resolve4Async(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var hostname = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve4 callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = ResolveAddresses(hostname, AddressFamily.InterNetwork);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            // Wire protocol throws Exception ("Runtime Error: dns.x ECODE host"), not
            // SocketException; ExtractErrorCode parses both shapes.
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// dns.resolve6(hostname, callback) - Resolve hostname to IPv6 addresses.
    /// </summary>
    private static RuntimeValue Resolve6Async(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var hostname = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve6 callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = ResolveAddresses(hostname, AddressFamily.InterNetworkV6);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            // Wire protocol throws Exception ("Runtime Error: dns.x ECODE host"), not
            // SocketException; ExtractErrorCode parses both shapes.
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return RuntimeValue.Undefined;
    }

    /// <summary>
    /// dns.reverse(ip, callback) - Reverse DNS lookup.
    /// </summary>
    private static RuntimeValue ReverseAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var ip = args[0].ToObject()?.ToString() ?? "";
        var callback = args[^1].ToObject() as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.reverse callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                if (!IPAddress.TryParse(ip, out var ipAddress))
                    throw new Exception($"invalid address {ip}");

                var hostEntry = Dns.GetHostEntry(ipAddress);
                var hostnames = new SharpTSArray(new List<object?> { hostEntry.HostName });
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [null, hostnames]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (SocketException ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError(GetErrorCode(ex), ip), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    interpreter.InvokeGuestCallback(callback, [CreateDnsError("EAI_FAIL", ip), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return RuntimeValue.Undefined;
    }

    private static RuntimeValue ResolveMxAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveMx", args, DnsRecordResolver.ResolveMx);

    private static RuntimeValue ResolveTxtAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveTxt", args, DnsRecordResolver.ResolveTxt);

    private static RuntimeValue ResolveSrvAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveSrv", args, DnsRecordResolver.ResolveSrv);

    private static RuntimeValue ResolveCnameAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveCname", args, DnsRecordResolver.ResolveCname);

    private static RuntimeValue ResolveNsAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveNs", args, DnsRecordResolver.ResolveNs);

    private static RuntimeValue ResolveSoaAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveSoa", args, hostname => DnsRecordResolver.ResolveSoa(hostname));

    private static RuntimeValue ResolvePtrAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolvePtr", args, DnsRecordResolver.ResolvePtr);

    private static RuntimeValue ResolveCaaAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveCaa", args, DnsRecordResolver.ResolveCaa);

    private static RuntimeValue ResolveNaptrAsync(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
        ResolveRecordAsync(interpreter, "resolveNaptr", args, DnsRecordResolver.ResolveNaptr);

    #endregion

    #region Promise-based DNS Resolution

    /// <summary>
    /// Runs blocking DNS work on the thread pool with the event loop Ref'd for
    /// the duration: bare thread-pool work holds no handle, so without the Ref
    /// a top-level promise wait sees a quiescent loop and exits mid-lookup.
    /// </summary>
    private static async Task<object?> RunRefed(Interp interpreter, Func<object?> work)
    {
        interpreter.Ref();
        try
        {
            return await Task.Run(work);
        }
        finally
        {
            interpreter.Unref();
        }
    }

    private static async Task<object?> LookupPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        int family = 0;
        if (args.Count > 1 && args[1] is double f) family = (int)f;
        else if (args.Count > 1 && args[1] is SharpTSObject opts)
        {
            if (opts.Fields.TryGetValue("family", out var fv) && fv is double fd) family = (int)fd;
        }

        return await RunRefed(interpreter, () =>
        {
            var hostEntry = Dns.GetHostEntry(hostname);
            var addresses = hostEntry.AddressList;
            if (family == 4) addresses = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            else if (family == 6) addresses = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToArray();

            if (addresses.Length == 0)
                throw new Exception($"dns.lookup ENOTFOUND {hostname}");

            var addr = addresses[0];
            return new SharpTSObject(new Dictionary<string, object?>
            {
                ["address"] = addr.ToString(),
                ["family"] = addr.AddressFamily == AddressFamily.InterNetwork ? 4.0 : 6.0
            });
        });
    }

    private static async Task<object?> ResolvePromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        string rrtype = "A";
        if (args.Count > 1 && args[1] is string rt) rrtype = rt;

        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.Resolve(hostname, rrtype)));
    }

    private static async Task<object?> Resolve4Promise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => ResolveAddresses(hostname, AddressFamily.InterNetwork));
    }

    private static async Task<object?> Resolve6Promise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => ResolveAddresses(hostname, AddressFamily.InterNetworkV6));
    }

    private static async Task<object?> ReversePromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var ip = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () =>
        {
            if (!IPAddress.TryParse(ip, out var ipAddress))
                throw new Exception($"dns.reverse: invalid address {ip}");
            var hostEntry = Dns.GetHostEntry(ipAddress);
            return new SharpTSArray(new List<object?> { hostEntry.HostName });
        });
    }

    private static async Task<object?> ResolveMxPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveMx(hostname)));
    }

    private static async Task<object?> ResolveTxtPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveTxt(hostname)));
    }

    private static async Task<object?> ResolveSrvPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveSrv(hostname)));
    }

    private static async Task<object?> ResolveCnamePromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveCname(hostname)));
    }

    private static async Task<object?> ResolveNsPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveNs(hostname)));
    }

    private static async Task<object?> ResolveSoaPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveSoa(hostname)));
    }

    private static async Task<object?> ResolvePtrPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolvePtr(hostname)));
    }

    private static async Task<object?> ResolveCaaPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveCaa(hostname)));
    }

    private static async Task<object?> ResolveNaptrPromise(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        return await RunRefed(interpreter, () => WrapDnsResult(DnsRecordResolver.ResolveNaptr(hostname)));
    }

    #endregion
}
