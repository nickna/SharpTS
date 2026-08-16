using System.Net;
using System.Net.Sockets;

namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Shared dns.Resolver state and logic, used by both interpreter and compiled modes.
/// Holds a configurable list of DNS servers; resolve methods use the first configured
/// server instead of the system default when servers are set.
/// </summary>
public sealed class DnsResolverInstance
{
    private string[] _servers = [];
    private CancellationTokenSource _cts = new();
    private string? _localAddressV4;
    private string? _localAddressV6;

    public string[] GetServers() => (string[])_servers.Clone();

    public void SetServers(string[] servers)
    {
        // Validate all addresses
        foreach (var server in servers)
        {
            var addr = StripPort(server);
            if (!IPAddress.TryParse(addr, out _))
                throw new Exception($"Runtime Error: dns.setServers invalid address: {server}");
        }
        _servers = (string[])servers.Clone();
    }

    /// <summary>
    /// The token observed by outstanding queries; replaced on <see cref="Cancel"/>
    /// so subsequent queries run normally (Node: cancel() aborts all *outstanding*
    /// queries made by this resolver).
    /// </summary>
    public CancellationToken CancellationToken => Volatile.Read(ref _cts).Token;

    /// <summary>
    /// Cancels all outstanding queries (their callbacks/promises reject with
    /// ECANCELLED); later queries use a fresh token (#1072).
    /// </summary>
    public void Cancel()
    {
        var old = Interlocked.Exchange(ref _cts, new CancellationTokenSource());
        old.Cancel();
    }

    /// <summary>
    /// Sets the local address(es) the resolver binds outgoing queries to (#1072).
    /// </summary>
    public void SetLocalAddress(string? ipv4, string? ipv6)
    {
        if (ipv4 != null && (!IPAddress.TryParse(ipv4, out var a4) || a4.AddressFamily != AddressFamily.InterNetwork))
            throw new NodeError("ERR_INVALID_IP_ADDRESS", $"Invalid IP address: {ipv4}");
        if (ipv6 != null && (!IPAddress.TryParse(ipv6, out var a6) || a6.AddressFamily != AddressFamily.InterNetworkV6))
            throw new NodeError("ERR_INVALID_IP_ADDRESS", $"Invalid IP address: {ipv6}");
        _localAddressV4 = ipv4;
        _localAddressV6 = ipv6;
    }

    /// <summary>
    /// Picks the local bind address matching the target server's family.
    /// </summary>
    private string? LocalAddressFor(string server)
    {
        var addr = StripPort(server);
        var isV6 = IPAddress.TryParse(addr, out var ip) && ip.AddressFamily == AddressFamily.InterNetworkV6;
        return isV6 ? _localAddressV6 : _localAddressV4;
    }

    /// <summary>Returns the first configured server, or null to use system default.</summary>
    public string? GetPreferredServer() => _servers.Length > 0 ? _servers[0] : null;

    public object Resolve(string hostname, string rrtype)
    {
        var server = GetPreferredServer();
        return server != null
            ? DnsRecordResolver.Resolve(hostname, rrtype, server)
            : DnsRecordResolver.Resolve(hostname, rrtype);
    }

    public List<object?> Resolve4(string hostname)
    {
        // A records always use system DNS (Dns.GetHostEntry); wire protocol for custom server
        var server = GetPreferredServer();
        if (server != null)
            return (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeA, server, LocalAddressFor(server));
        return DnsRecordResolver.ResolveA(hostname);
    }

    public List<object?> Resolve6(string hostname)
    {
        var server = GetPreferredServer();
        if (server != null)
            return (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeAAAA, server, LocalAddressFor(server));
        return DnsRecordResolver.ResolveAaaa(hostname);
    }

    public List<object?> Reverse(string ip)
    {
        // Reverse DNS uses system DNS (Dns.GetHostEntry); wire protocol PTR for custom server
        var server = GetPreferredServer();
        if (server != null)
        {
            if (!IPAddress.TryParse(ip, out var addr))
                throw new Exception($"Runtime Error: dns.reverse invalid address {ip}");
            var arpaName = BuildArpaName(addr);
            return (List<object?>)DnsWireProtocol.Query(arpaName, DnsWireProtocol.TypePTR, server, LocalAddressFor(server));
        }

        if (!IPAddress.TryParse(ip, out var ipAddress))
            throw new Exception($"Runtime Error: dns.reverse invalid address {ip}");
        var hostEntry = Dns.GetHostEntry(ipAddress);
        return new List<object?> { hostEntry.HostName };
    }

    public object ResolveMx(string hostname) => DelegateResolve(hostname, "MX", DnsRecordResolver.ResolveMx);
    public object ResolveTxt(string hostname) => DelegateResolve(hostname, "TXT", DnsRecordResolver.ResolveTxt);
    public object ResolveSrv(string hostname) => DelegateResolve(hostname, "SRV", DnsRecordResolver.ResolveSrv);
    public object ResolveCname(string hostname) => DelegateResolve(hostname, "CNAME", DnsRecordResolver.ResolveCname);
    public object ResolveNs(string hostname) => DelegateResolve(hostname, "NS", DnsRecordResolver.ResolveNs);
    public object ResolveSoa(string hostname) => DelegateResolveSoa(hostname);
    public object ResolvePtr(string hostname) => DelegateResolve(hostname, "PTR", DnsRecordResolver.ResolvePtr);
    public object ResolveCaa(string hostname) => DelegateResolve(hostname, "CAA", DnsRecordResolver.ResolveCaa);
    public object ResolveNaptr(string hostname) => DelegateResolve(hostname, "NAPTR", DnsRecordResolver.ResolveNaptr);

    private object DelegateResolve(string hostname, string rrtype, Func<string, List<object?>> defaultResolver)
    {
        var server = GetPreferredServer();
        return server != null
            ? DnsWireProtocol.Query(hostname, GetQType(rrtype), server, LocalAddressFor(server))
            : defaultResolver(hostname);
    }

    private object DelegateResolveSoa(string hostname)
    {
        var server = GetPreferredServer();
        return server != null
            ? DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSOA, server, LocalAddressFor(server))
            : DnsRecordResolver.ResolveSoa(hostname);
    }

    private static int GetQType(string rrtype) => rrtype switch
    {
        "MX" => DnsWireProtocol.TypeMX,
        "TXT" => DnsWireProtocol.TypeTXT,
        "SRV" => DnsWireProtocol.TypeSRV,
        "CNAME" => DnsWireProtocol.TypeCNAME,
        "NS" => DnsWireProtocol.TypeNS,
        "SOA" => DnsWireProtocol.TypeSOA,
        "PTR" => DnsWireProtocol.TypePTR,
        "CAA" => DnsWireProtocol.TypeCAA,
        "NAPTR" => DnsWireProtocol.TypeNAPTR,
        _ => throw new Exception($"Runtime Error: dns.resolve unknown rrtype: {rrtype}")
    };

    private static string BuildArpaName(IPAddress addr)
    {
        if (addr.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = addr.GetAddressBytes();
            return $"{bytes[3]}.{bytes[2]}.{bytes[1]}.{bytes[0]}.in-addr.arpa";
        }
        // IPv6
        var hex = string.Concat(addr.GetAddressBytes().Select(b => b.ToString("x2")));
        return string.Join(".", hex.Reverse()) + ".ip6.arpa";
    }

    private static string StripPort(string server)
    {
        if (server.StartsWith('['))
        {
            var close = server.IndexOf(']');
            return close > 0 ? server[1..close] : server[1..];
        }
        var colon = server.IndexOf(':');
        if (colon >= 0 && server.IndexOf(':', colon + 1) < 0)
            return server[..colon];
        return server;
    }
}
