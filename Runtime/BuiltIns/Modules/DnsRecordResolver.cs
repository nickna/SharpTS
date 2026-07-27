namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// DNS record resolver using DnsWireProtocol for MX, TXT, SRV, CNAME, NS, SOA, PTR, CAA, NAPTR records.
/// Used by the interpreter (via direct calls) and compiled mode (via emitted wire protocol IL).
/// </summary>
public static class DnsRecordResolver
{
    /// <summary>
    /// Resolves DNS records by type. Returns results as List&lt;object?&gt; suitable for
    /// creating SharpTS arrays/objects.
    /// </summary>
    /// <param name="hostname">The hostname to resolve.</param>
    /// <param name="rrtype">The record type (MX, TXT, SRV, CNAME, NS, SOA, PTR, CAA, NAPTR, A, AAAA).</param>
    /// <returns>
    /// For most types: List&lt;object?&gt; of results (each element is a Dictionary or string).
    /// For SOA: a single Dictionary&lt;string, object?&gt;.
    /// </returns>
    public static object Resolve(string hostname, string rrtype)
    {
        return rrtype.ToUpperInvariant() switch
        {
            "MX" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeMX),
            "TXT" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeTXT),
            "SRV" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSRV),
            "CNAME" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCNAME),
            "NS" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNS),
            "SOA" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSOA),
            "PTR" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypePTR),
            "CAA" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCAA),
            "NAPTR" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNAPTR),
            "A" => ResolveA(hostname),
            "AAAA" => ResolveAaaa(hostname),
            _ => throw new Exception($"Runtime Error: dns.resolve unknown rrtype: {rrtype}")
        };
    }

    public static List<object?> ResolveMx(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeMX);

    public static List<object?> ResolveTxt(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeTXT);

    public static List<object?> ResolveSrv(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSRV);

    public static List<object?> ResolveCname(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCNAME);

    public static List<object?> ResolveNs(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNS);

    public static Dictionary<string, object?> ResolveSoa(string hostname) =>
        (Dictionary<string, object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSOA);

    public static List<object?> ResolvePtr(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypePTR);

    public static List<object?> ResolveCaa(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCAA);

    public static List<object?> ResolveNaptr(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNAPTR);

    // resolve4/resolve6 (A/AAAA) use the DNS wire protocol — like Node's c-ares
    // resolver, and unlike dns.lookup — so they honor SHARPTS_DNS_SERVER and do NOT
    // consult the hosts file. dns.lookup (DnsModuleInterpreter.LookupCore) stays on
    // the OS resolver for hosts-file resolution.
    public static List<object?> ResolveA(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeA);

    public static List<object?> ResolveAaaa(string hostname) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeAAAA);

    #region Overloads with custom DNS server

    /// <summary>
    /// Resolves DNS records using a specific DNS server. Used by dns.Resolver instances.
    /// </summary>
    public static object Resolve(string hostname, string rrtype, string server) =>
        rrtype.ToUpperInvariant() switch
        {
            "MX" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeMX, server),
            "TXT" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeTXT, server),
            "SRV" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSRV, server),
            "CNAME" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCNAME, server),
            "NS" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNS, server),
            "SOA" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSOA, server),
            "PTR" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypePTR, server),
            "CAA" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCAA, server),
            "NAPTR" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNAPTR, server),
            "A" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeA, server),
            "AAAA" => DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeAAAA, server),
            _ => throw new Exception($"Runtime Error: dns.resolve unknown rrtype: {rrtype}")
        };

    public static List<object?> ResolveMx(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeMX, server);

    public static List<object?> ResolveTxt(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeTXT, server);

    public static List<object?> ResolveSrv(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSRV, server);

    public static List<object?> ResolveCname(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCNAME, server);

    public static List<object?> ResolveNs(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNS, server);

    public static Dictionary<string, object?> ResolveSoa(string hostname, string server) =>
        (Dictionary<string, object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeSOA, server);

    public static List<object?> ResolvePtr(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypePTR, server);

    public static List<object?> ResolveCaa(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeCAA, server);

    public static List<object?> ResolveNaptr(string hostname, string server) =>
        (List<object?>)DnsWireProtocol.Query(hostname, DnsWireProtocol.TypeNAPTR, server);

    #endregion
}
