using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Node.js net.BlockList (#1069): a rule set of blocked
/// addresses, ranges, and subnets, checkable per-address and enforceable by
/// net.Server connection filtering (a blocked peer is closed silently — Node
/// emits no event for BlockList rejections).
/// NOTE: Must stay in sync with the compiled $BlockList
/// (Compilation/RuntimeEmitter.TSNetBlockList.cs).
/// </summary>
public class SharpTSBlockList
{
    /// <summary>One block rule: an inclusive canonical byte range within a family.</summary>
    private sealed record Rule(bool IsV6, byte[] Start, byte[] End, string Display);

    private readonly List<Rule> _rules = [];

    /// <summary>
    /// Gets a member by name for interpreter dispatch.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "addAddress" => BuiltInMethod.CreateV2("addAddress", 1, 2, AddAddress),
            "addRange" => BuiltInMethod.CreateV2("addRange", 2, 3, AddRange),
            "addSubnet" => BuiltInMethod.CreateV2("addSubnet", 2, 3, AddSubnet),
            "check" => BuiltInMethod.CreateV2("check", 1, 2, Check),
            "rules" => GetRules(),
            _ => null
        };
    }

    private SharpTSArray GetRules()
    {
        var items = new List<object?>(_rules.Count);
        foreach (var rule in _rules) items.Add(rule.Display);
        return new SharpTSArray(items);
    }

    private RuntimeValue AddAddress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var (addr, isV6) = ResolveAddressArg(args[0].ToObject(), args.Length > 1 ? args[1].ToObject() : null);
        var bytes = ParseCanonical(addr, isV6)
            ?? throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'address' is invalid. Received '{addr}'");
        _rules.Add(new Rule(isV6, bytes, bytes, $"Address: {FamilyDisplay(isV6)} {Canonical(addr)}"));
        return RuntimeValue.Undefined;
    }

    private RuntimeValue AddRange(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var familyArg = args.Length > 2 ? args[2].ToObject() : null;
        var (startAddr, isV6) = ResolveAddressArg(args[0].ToObject(), familyArg);
        var (endAddr, _) = ResolveAddressArg(args[1].ToObject(), familyArg);
        var start = ParseCanonical(startAddr, isV6)
            ?? throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'start' is invalid. Received '{startAddr}'");
        var end = ParseCanonical(endAddr, isV6)
            ?? throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'end' is invalid. Received '{endAddr}'");
        if (CompareBytes(start, end) > 0)
            throw new NodeError("ERR_INVALID_ARG_VALUE", "The argument 'start' must come before 'end'");
        _rules.Add(new Rule(isV6, start, end, $"Range: {FamilyDisplay(isV6)} {Canonical(startAddr)}-{Canonical(endAddr)}"));
        return RuntimeValue.Undefined;
    }

    private RuntimeValue AddSubnet(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var familyArg = args.Length > 2 ? args[2].ToObject() : null;
        var (addr, isV6) = ResolveAddressArg(args[0].ToObject(), familyArg);
        if (args[1].ToObject() is not double prefixNum)
            throw new NodeError("ERR_INVALID_ARG_TYPE", "The 'prefix' argument must be of type number");
        int prefix = (int)prefixNum;
        int maxPrefix = isV6 ? 128 : 32;
        if (prefix < 0 || prefix > maxPrefix)
            throw new NodeError("ERR_OUT_OF_RANGE", $"The value of 'prefix' is out of range. It must be >= 0 and <= {maxPrefix}. Received {prefix}");
        var bytes = ParseCanonical(addr, isV6)
            ?? throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'network' is invalid. Received '{addr}'");
        var (start, end) = SubnetBounds(bytes, prefix);
        _rules.Add(new Rule(isV6, start, end, $"Subnet: {FamilyDisplay(isV6)} {Canonical(addr)}/{prefix}"));
        return RuntimeValue.Undefined;
    }

    private RuntimeValue Check(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string addr;
        bool isV6;
        try
        {
            (addr, isV6) = ResolveAddressArg(args[0].ToObject(), args.Length > 1 ? args[1].ToObject() : null);
        }
        catch (NodeError)
        {
            return RuntimeValue.False; // check() never throws — unparseable input is simply not blocked
        }

        if (isV6)
        {
            // A v6-family query checks v6 rules; a v4-mapped address additionally
            // checks the embedded IPv4 against the v4 rules (Node semantics).
            var v6 = ParseCanonical(addr, isV6: true);
            if (v6 != null && Matches(v6, isV6: true)) return RuntimeValue.True;
            var mapped = ParseCanonical(addr, isV6: false);
            return RuntimeValue.FromBoolean(mapped != null && Matches(mapped, isV6: false));
        }

        var v4 = ParseCanonical(addr, isV6: false);
        return RuntimeValue.FromBoolean(v4 != null && Matches(v4, isV6: false));
    }

    /// <summary>
    /// Host-side check used by net.Server connection filtering: true if the peer
    /// address is blocked. IPv4-mapped IPv6 peers are checked as IPv4.
    /// </summary>
    public bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var isV6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        return Matches(address.GetAddressBytes(), isV6);
    }

    private bool Matches(byte[] bytes, bool isV6)
    {
        foreach (var rule in _rules)
        {
            if (rule.IsV6 != isV6) continue;
            if (CompareBytes(rule.Start, bytes) <= 0 && CompareBytes(bytes, rule.End) <= 0)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves an (address, family) argument pair: the address may be a string
    /// (family from the second arg, default "ipv4") or a SocketAddress (family
    /// carried by the instance).
    /// </summary>
    private static (string Address, bool IsV6) ResolveAddressArg(object? addressArg, object? familyArg)
    {
        if (addressArg is SharpTSSocketAddress sa)
            return (sa.Address, sa.Family == "ipv6");

        if (addressArg is not string addr)
            throw new NodeError("ERR_INVALID_ARG_TYPE", "The 'address' argument must be of type string or SocketAddress");

        var family = familyArg is string f ? f.ToLowerInvariant() : "ipv4";
        if (family is not ("ipv4" or "ipv6"))
            throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'family' is invalid. Received '{family}'");
        return (addr, family == "ipv6");
    }

    /// <summary>
    /// Parses an address into canonical family bytes: 4 bytes for ipv4 (accepting
    /// IPv4-mapped IPv6 input), 16 bytes for ipv6. Returns null when the address
    /// doesn't parse in the requested family.
    /// </summary>
    private static byte[]? ParseCanonical(string address, bool isV6)
    {
        if (!IPAddress.TryParse(address, out var parsed)) return null;
        if (isV6)
            return parsed.AddressFamily == AddressFamily.InterNetworkV6 ? parsed.GetAddressBytes() : null;
        if (parsed.AddressFamily == AddressFamily.InterNetwork) return parsed.GetAddressBytes();
        if (parsed.AddressFamily == AddressFamily.InterNetworkV6 && parsed.IsIPv4MappedToIPv6)
            return parsed.MapToIPv4().GetAddressBytes();
        return null;
    }

    private static int CompareBytes(byte[] a, byte[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return a[i] < b[i] ? -1 : 1;
        }
        return 0;
    }

    /// <summary>Computes the inclusive [network, broadcast] bounds of an address/prefix subnet.</summary>
    private static (byte[] Start, byte[] End) SubnetBounds(byte[] addr, int prefix)
    {
        var start = new byte[addr.Length];
        var end = new byte[addr.Length];
        for (int i = 0; i < addr.Length; i++)
        {
            int bits = prefix - i * 8;
            int mask = bits >= 8 ? 0xFF : bits <= 0 ? 0 : (0xFF << (8 - bits)) & 0xFF;
            start[i] = (byte)(addr[i] & mask);
            end[i] = (byte)(addr[i] | ~mask & 0xFF);
        }
        return (start, end);
    }

    private static string FamilyDisplay(bool isV6) => isV6 ? "IPv6" : "IPv4";

    private static string Canonical(string address) =>
        IPAddress.TryParse(address, out var parsed) ? parsed.ToString() : address;

    public override string ToString() => $"BlockList {{ rules: [{_rules.Count}] }}";
}
