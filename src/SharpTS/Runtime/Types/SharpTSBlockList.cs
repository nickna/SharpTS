using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Opaque native rule store used by the TypeScript net.BlockList facade. Public
/// validation, rule display, and check semantics live in stdlib/node/net.ts; this
/// type exists only so net.Server can reject a peer on its accept thread.
/// </summary>
public sealed class SharpTSBlockList
{
    private sealed record Rule(bool IsV6, byte[] Start, byte[] End);

    private readonly List<Rule> _rules = [];

    public object? GetMember(string name) => name switch
    {
        "addAddress" => BuiltInMethod.CreateV2("addAddress", 1, 2, AddAddress),
        "addRange" => BuiltInMethod.CreateV2("addRange", 2, 3, AddRange),
        "addSubnet" => BuiltInMethod.CreateV2("addSubnet", 2, 3, AddSubnet),
        _ => null
    };

    private RuntimeValue AddAddress(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var isV6 = IsV6(args, 1);
        var bytes = Parse(args[0].ToObject(), isV6);
        _rules.Add(new Rule(isV6, bytes, bytes));
        return RuntimeValue.Undefined;
    }

    private RuntimeValue AddRange(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var isV6 = IsV6(args, 2);
        var start = Parse(args[0].ToObject(), isV6);
        var end = Parse(args[1].ToObject(), isV6);
        if (Compare(start, end) > 0)
            throw new InvalidOperationException("Invalid native BlockList range");
        _rules.Add(new Rule(isV6, start, end));
        return RuntimeValue.Undefined;
    }

    private RuntimeValue AddSubnet(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var isV6 = IsV6(args, 2);
        var address = Parse(args[0].ToObject(), isV6);
        if (args[1].ToObject() is not double prefixValue || prefixValue != Math.Truncate(prefixValue))
            throw new InvalidOperationException("Invalid native BlockList prefix");
        var prefix = checked((int)prefixValue);
        if (prefix < 0 || prefix > (isV6 ? 128 : 32))
            throw new InvalidOperationException("Invalid native BlockList prefix");
        var (start, end) = SubnetBounds(address, prefix);
        _rules.Add(new Rule(isV6, start, end));
        return RuntimeValue.Undefined;
    }

    /// <summary>Called by the server accept loop; safe from guest execution.</summary>
    public bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        var isV6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        var bytes = address.GetAddressBytes();
        foreach (var rule in _rules)
        {
            if (rule.IsV6 == isV6 && Compare(rule.Start, bytes) <= 0 && Compare(bytes, rule.End) <= 0)
                return true;
        }
        return false;
    }

    private static bool IsV6(ReadOnlySpan<RuntimeValue> args, int familyIndex) =>
        args.Length > familyIndex
        && args[familyIndex].ToObject() is string family
        && string.Equals(family, "ipv6", StringComparison.OrdinalIgnoreCase);

    private static byte[] Parse(object? value, bool isV6)
    {
        if (value is not string text || !IPAddress.TryParse(text, out var address))
            throw new InvalidOperationException("Invalid native BlockList address");
        if (isV6 && address.AddressFamily == AddressFamily.InterNetworkV6)
            return address.GetAddressBytes();
        if (!isV6 && address.AddressFamily == AddressFamily.InterNetwork)
            return address.GetAddressBytes();
        if (!isV6 && address.IsIPv4MappedToIPv6)
            return address.MapToIPv4().GetAddressBytes();
        throw new InvalidOperationException("Invalid native BlockList address family");
    }

    private static int Compare(byte[] left, byte[] right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i]) return left[i] < right[i] ? -1 : 1;
        }
        return 0;
    }

    private static (byte[] Start, byte[] End) SubnetBounds(byte[] address, int prefix)
    {
        var start = new byte[address.Length];
        var end = new byte[address.Length];
        for (var i = 0; i < address.Length; i++)
        {
            var bits = prefix - i * 8;
            var mask = bits >= 8 ? 0xff : bits <= 0 ? 0 : (0xff << (8 - bits)) & 0xff;
            start[i] = (byte)(address[i] & mask);
            end[i] = (byte)(address[i] | ~mask & 0xff);
        }
        return (start, end);
    }
}
