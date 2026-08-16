using System.Net;
using System.Net.Sockets;
using SharpTS.Runtime.BuiltIns;
using SharpTS.Runtime.BuiltIns.Modules;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Runtime representation of Node.js net.SocketAddress — an immutable value type
/// describing an IP endpoint: { address, family, port, flowlabel } (#1069).
/// NOTE: Must stay in sync with the compiled $SocketAddress
/// (Compilation/RuntimeEmitter.TSNetBlockList.cs).
/// </summary>
public class SharpTSSocketAddress
{
    /// <summary>The IP address string (normalized).</summary>
    public string Address { get; }

    /// <summary>The address family: "ipv4" or "ipv6".</summary>
    public string Family { get; }

    /// <summary>The port number (0 if unset).</summary>
    public double Port { get; }

    /// <summary>The IPv6 flow label (0 if unset or IPv4).</summary>
    public double FlowLabel { get; }

    /// <summary>
    /// Creates a SocketAddress from a Node options object. Defaults mirror Node:
    /// family "ipv4", address "127.0.0.1" (v4) / "::" (v6), port 0, flowlabel 0.
    /// </summary>
    public SharpTSSocketAddress(SharpTSObject? options)
    {
        string family = "ipv4";
        if (options?.GetProperty("family") is string f)
        {
            family = f.ToLowerInvariant();
            if (family is not ("ipv4" or "ipv6"))
                throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'options.family' is invalid. Received '{f}'");
        }
        Family = family;

        string address = family == "ipv6" ? "::" : "127.0.0.1";
        if (options?.GetProperty("address") is string a)
        {
            if (!IPAddress.TryParse(a, out var parsed)
                || (family == "ipv4" && parsed.AddressFamily != AddressFamily.InterNetwork)
                || (family == "ipv6" && parsed.AddressFamily != AddressFamily.InterNetworkV6))
            {
                throw new NodeError("ERR_INVALID_ARG_VALUE", $"The argument 'options.address' is invalid. Received '{a}'");
            }
            address = parsed.ToString();
        }
        Address = address;

        Port = options?.GetProperty("port") is double p ? p : 0;
        FlowLabel = options?.GetProperty("flowlabel") is double fl ? fl : 0;
    }

    internal SharpTSSocketAddress(IPAddress address, int port)
    {
        var isV6 = address.AddressFamily == AddressFamily.InterNetworkV6;
        Address = address.ToString();
        Family = isV6 ? "ipv6" : "ipv4";
        Port = port;
        FlowLabel = 0;
    }

    /// <summary>
    /// Gets a member by name for interpreter dispatch.
    /// </summary>
    public object? GetMember(string name)
    {
        return name switch
        {
            "address" => Address,
            "family" => Family,
            "port" => Port,
            "flowlabel" => FlowLabel,
            "toJSON" => BuiltInMethod.CreateV2("toJSON", 0, (Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args) =>
                RuntimeValue.FromObject(new SharpTSObject(new Dictionary<string, object?>
                {
                    ["address"] = Address,
                    ["family"] = Family,
                    ["port"] = Port,
                    ["flowlabel"] = FlowLabel
                }))),
            _ => null
        };
    }

    public override string ToString() => $"SocketAddress {{ address: '{Address}', family: '{Family}', port: {Port} }}";
}
