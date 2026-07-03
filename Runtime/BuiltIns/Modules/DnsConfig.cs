namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Process-wide dns-module configuration shared by the interpreter and the
/// SharpTS.dll-side compiled dispatch (RuntimeTypes.Dns) (#1072).
/// The emitted standalone IL keeps its own mirror of the result order and
/// best-effort syncs to this class when SharpTS.dll is present.
/// </summary>
public static class DnsConfig
{
    /// <summary>Valid dns.setDefaultResultOrder values.</summary>
    public static readonly string[] ValidResultOrders = ["ipv4first", "ipv6first", "verbatim"];

    private static string _defaultResultOrder = "verbatim"; // Node default since v17

    /// <summary>The default result order applied by dns.lookup / dnsPromises.lookup.</summary>
    public static string DefaultResultOrder => _defaultResultOrder;

    /// <summary>
    /// Sets the default result order. Throws a coded error for invalid values
    /// (Node throws ERR_INVALID_ARG_VALUE).
    /// </summary>
    public static void SetDefaultResultOrder(string order)
    {
        if (!ValidResultOrders.Contains(order))
            throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'order' must be one of: 'ipv4first', 'ipv6first', 'verbatim'. Received '{order}'");
        _defaultResultOrder = order;
    }
}
