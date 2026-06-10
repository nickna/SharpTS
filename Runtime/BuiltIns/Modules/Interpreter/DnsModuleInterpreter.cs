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
            ["lookup"] = new BuiltInMethod("lookup", 1, 3, Lookup),
            ["lookupService"] = new BuiltInMethod("lookupService", 2, 3, LookupService),

            // Async (callback-based) DNS resolution
            ["resolve"] = new BuiltInMethod("resolve", 2, 3, ResolveAsync),
            ["resolve4"] = new BuiltInMethod("resolve4", 2, Resolve4Async),
            ["resolve6"] = new BuiltInMethod("resolve6", 2, Resolve6Async),
            ["reverse"] = new BuiltInMethod("reverse", 2, ReverseAsync),
            ["resolveMx"] = new BuiltInMethod("resolveMx", 2, ResolveMxAsync),
            ["resolveTxt"] = new BuiltInMethod("resolveTxt", 2, ResolveTxtAsync),
            ["resolveSrv"] = new BuiltInMethod("resolveSrv", 2, ResolveSrvAsync),
            ["resolveCname"] = new BuiltInMethod("resolveCname", 2, ResolveCnameAsync),
            ["resolveNs"] = new BuiltInMethod("resolveNs", 2, ResolveNsAsync),
            ["resolveSoa"] = new BuiltInMethod("resolveSoa", 2, ResolveSoaAsync),
            ["resolvePtr"] = new BuiltInMethod("resolvePtr", 2, ResolvePtrAsync),
            ["resolveCaa"] = new BuiltInMethod("resolveCaa", 2, ResolveCaaAsync),
            ["resolveNaptr"] = new BuiltInMethod("resolveNaptr", 2, ResolveNaptrAsync),

            // Promises API
            ["promises"] = new SharpTSObject(GetPromisesExports()),

            // Resolver class constructor
            ["Resolver"] = new BuiltInMethod("Resolver", 0, 1, CreateResolver),

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
            ["resolveNaptr"] = new BuiltInAsyncMethod("resolveNaptr", 1, 1, ResolveNaptrPromise)
        };
    }

    /// <summary>
    /// dns.lookup(hostname[, options][, callback]) - Resolves a hostname to an IP address.
    /// </summary>
    private static object? Lookup(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count == 0 || args[0] is not string hostname)
            throw new Exception("Runtime Error: dns.lookup requires a hostname string");

        // Parse options
        int family = 0; // 0 = any, 4 = IPv4 only, 6 = IPv6 only
        bool all = false;

        if (args.Count > 1 && args[1] != null)
        {
            if (args[1] is double familyNum)
            {
                family = (int)familyNum;
            }
            else if (args[1] is SharpTSObject options)
            {
                if (options.Fields.TryGetValue("family", out var familyVal) && familyVal is double f)
                    family = (int)f;
                if (options.Fields.TryGetValue("all", out var allVal))
                    all = IsTruthy(allVal);
            }
        }

        try
        {
            var hostEntry = Dns.GetHostEntry(hostname);
            var addresses = hostEntry.AddressList;

            // Filter by family if specified
            if (family == 4)
                addresses = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetwork).ToArray();
            else if (family == 6)
                addresses = addresses.Where(a => a.AddressFamily == AddressFamily.InterNetworkV6).ToArray();

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
    private static object? LookupService(Interp interpreter, object? receiver, List<object?> args)
    {
        if (args.Count < 2)
            throw new Exception("Runtime Error: dns.lookupService requires address and port");

        if (args[0] is not string address)
            throw new Exception("Runtime Error: dns.lookupService address must be a string");

        if (args[1] is not double portNum)
            throw new Exception("Runtime Error: dns.lookupService port must be a number");

        int port = (int)portNum;

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
    private static object? CreateResolver(Interp interpreter, object? receiver, List<object?> args)
    {
        var instance = new DnsResolverInstance();
        var fields = new Dictionary<string, object?>
        {
            ["setServers"] = new BuiltInMethod("setServers", 1, 1, (interp, _, a) =>
            {
                var servers = ExtractStringArray(a[0]);
                instance.SetServers(servers);
                return SharpTSUndefined.Instance;
            }),
            ["getServers"] = new BuiltInMethod("getServers", 0, 0, (interp, _, _) =>
            {
                var servers = instance.GetServers();
                return new SharpTSArray(servers.Select(s => (object?)s).ToList());
            }),
            ["resolve"] = new BuiltInMethod("resolve", 2, 3, (interp, _, a) =>
                ResolverResolveAsync(interp, instance, a)),
            ["resolve4"] = new BuiltInMethod("resolve4", 2, 2, (interp, _, a) =>
                ResolverResolveByFamilyAsync(interp, instance, "resolve4", a, 4)),
            ["resolve6"] = new BuiltInMethod("resolve6", 2, 2, (interp, _, a) =>
                ResolverResolveByFamilyAsync(interp, instance, "resolve6", a, 6)),
            ["reverse"] = new BuiltInMethod("reverse", 2, 2, (interp, _, a) =>
                ResolverReverseAsync(interp, instance, a)),
            ["resolveMx"] = new BuiltInMethod("resolveMx", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveMx", a, r => r.ResolveMx)),
            ["resolveTxt"] = new BuiltInMethod("resolveTxt", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveTxt", a, r => r.ResolveTxt)),
            ["resolveSrv"] = new BuiltInMethod("resolveSrv", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveSrv", a, r => r.ResolveSrv)),
            ["resolveCname"] = new BuiltInMethod("resolveCname", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveCname", a, r => r.ResolveCname)),
            ["resolveNs"] = new BuiltInMethod("resolveNs", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveNs", a, r => r.ResolveNs)),
            ["resolveSoa"] = new BuiltInMethod("resolveSoa", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveSoa", a, r => r.ResolveSoa)),
            ["resolvePtr"] = new BuiltInMethod("resolvePtr", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolvePtr", a, r => r.ResolvePtr)),
            ["resolveCaa"] = new BuiltInMethod("resolveCaa", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveCaa", a, r => r.ResolveCaa)),
            ["resolveNaptr"] = new BuiltInMethod("resolveNaptr", 2, 2, (interp, _, a) =>
                ResolverRecordAsync(interp, instance, "resolveNaptr", a, r => r.ResolveNaptr)),
            ["cancel"] = new BuiltInMethod("cancel", 0, 0, (_, _, _) => SharpTSUndefined.Instance)
        };
        return new SharpTSObject(fields);
    }

    private static string[] ExtractStringArray(object? value)
    {
        if (value is SharpTSArray arr)
            return arr.Select(e => e?.ToString() ?? "").ToArray();
        if (value is List<object?> list)
            return list.Select(e => e?.ToString() ?? "").ToArray();
        throw new Exception("Runtime Error: dns.setServers requires an array of strings");
    }

    private static object? ResolverResolveAsync(Interp interpreter, DnsResolverInstance instance, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve callback is required");
        string rrtype = "A";
        if (args.Count > 2 && args[1] is string rt) rrtype = rt;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var raw = instance.Resolve(hostname, rrtype);
                var result = WrapDnsResult(raw);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });
        return SharpTSUndefined.Instance;
    }

    private static object? ResolverResolveByFamilyAsync(Interp interpreter, DnsResolverInstance instance,
        string methodName, List<object?> args, int family)
    {
        var hostname = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception($"Runtime Error: dns.{methodName} callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = family == 4 ? instance.Resolve4(hostname) : instance.Resolve6(hostname);
                var wrapped = new SharpTSArray(result);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, wrapped]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });
        return SharpTSUndefined.Instance;
    }

    private static object? ResolverReverseAsync(Interp interpreter, DnsResolverInstance instance, List<object?> args)
    {
        var ip = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.reverse callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = instance.Reverse(ip);
                var wrapped = new SharpTSArray(result);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, wrapped]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(ExtractErrorCode(ex), ip), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });
        return SharpTSUndefined.Instance;
    }

    private static object? ResolverRecordAsync(Interp interpreter, DnsResolverInstance instance,
        string methodName, List<object?> args, Func<DnsResolverInstance, Func<string, object>> resolveSelector)
    {
        var hostname = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception($"Runtime Error: dns.{methodName} callback is required");

        var resolveFunc = resolveSelector(instance);
        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var raw = resolveFunc(hostname);
                var result = WrapDnsResult(raw);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });
        return SharpTSUndefined.Instance;
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
        var hostEntry = Dns.GetHostEntry(hostname);
        var addresses = hostEntry.AddressList;
        if (family != null)
            addresses = addresses.Where(a => a.AddressFamily == family).ToArray();
        if (addresses.Length == 0)
            throw new SocketException((int)SocketError.HostNotFound);
        return new SharpTSArray(addresses.Select(a => (object?)a.ToString()).ToList());
    }

    /// <summary>
    /// Wraps raw DNS resolution results from DnsRecordResolver into SharpTS types.
    /// List&lt;object?&gt; of dicts → SharpTSArray of SharpTSObjects,
    /// List&lt;object?&gt; of lists (TXT) → SharpTSArray of SharpTSArrays,
    /// List&lt;object?&gt; of strings → SharpTSArray,
    /// Dictionary → SharpTSObject.
    /// </summary>
    private static object WrapDnsResult(object raw)
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
    private static object? ResolveRecordAsync(Interp interpreter, string methodName, List<object?> args,
        Func<string, object> resolveFunc)
    {
        var hostname = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
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
                    callback.Call(interpreter, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// Extracts the DNS error code from an exception message.
    /// </summary>
    private static string ExtractErrorCode(Exception ex)
    {
        var msg = ex.Message;
        // Error messages follow pattern "Runtime Error: dns.xxx ECODE hostname"
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
    private static object? ResolveAsync(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve callback is required");

        // Optional rrtype (default 'A')
        string rrtype = "A";
        if (args.Count > 2 && args[1] is string rt)
            rrtype = rt;

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var raw = DnsRecordResolver.Resolve(hostname, rrtype);
                var result = WrapDnsResult(raw);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(ExtractErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// dns.resolve4(hostname, callback) - Resolve hostname to IPv4 addresses.
    /// </summary>
    private static object? Resolve4Async(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve4 callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = ResolveAddresses(hostname, AddressFamily.InterNetwork);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (SocketException ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(GetErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// dns.resolve6(hostname, callback) - Resolve hostname to IPv6 addresses.
    /// </summary>
    private static object? Resolve6Async(Interp interpreter, object? receiver, List<object?> args)
    {
        var hostname = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
            ?? throw new Exception("Runtime Error: dns.resolve6 callback is required");

        interpreter.Ref();
        _ = Task.Run(() =>
        {
            try
            {
                var result = ResolveAddresses(hostname, AddressFamily.InterNetworkV6);
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [null, result]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (SocketException ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(GetErrorCode(ex), hostname), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return SharpTSUndefined.Instance;
    }

    /// <summary>
    /// dns.reverse(ip, callback) - Reverse DNS lookup.
    /// </summary>
    private static object? ReverseAsync(Interp interpreter, object? receiver, List<object?> args)
    {
        var ip = args[0]?.ToString() ?? "";
        var callback = args[^1] as ISharpTSCallable
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
                    callback.Call(interpreter, [null, hostnames]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (SocketException ex)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError(GetErrorCode(ex), ip), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
            catch (Exception)
            {
                interpreter.ScheduleTimer(0, 0, () =>
                {
                    callback.Call(interpreter, [CreateDnsError("EAI_FAIL", ip), null]);
                    interpreter.Unref();
                }, isInterval: false);
            }
        });

        return SharpTSUndefined.Instance;
    }

    private static object? ResolveMxAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolveMx", args, DnsRecordResolver.ResolveMx);

    private static object? ResolveTxtAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolveTxt", args, DnsRecordResolver.ResolveTxt);

    private static object? ResolveSrvAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolveSrv", args, DnsRecordResolver.ResolveSrv);

    private static object? ResolveCnameAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolveCname", args, DnsRecordResolver.ResolveCname);

    private static object? ResolveNsAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolveNs", args, DnsRecordResolver.ResolveNs);

    private static object? ResolveSoaAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolveSoa", args, hostname => DnsRecordResolver.ResolveSoa(hostname));

    private static object? ResolvePtrAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolvePtr", args, DnsRecordResolver.ResolvePtr);

    private static object? ResolveCaaAsync(Interp interpreter, object? receiver, List<object?> args) =>
        ResolveRecordAsync(interpreter, "resolveCaa", args, DnsRecordResolver.ResolveCaa);

    private static object? ResolveNaptrAsync(Interp interpreter, object? receiver, List<object?> args) =>
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
