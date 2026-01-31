using System.Net;
using System.Net.Sockets;
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
            // Methods
            ["lookup"] = new BuiltInMethod("lookup", 1, 3, Lookup),
            ["lookupService"] = new BuiltInMethod("lookupService", 2, 3, LookupService),

            // Constants
            ["ADDRCONFIG"] = (double)1,
            ["V4MAPPED"] = (double)2,
            ["ALL"] = (double)4
        };
    }

    /// <summary>
    /// dns.lookup(hostname[, options][, callback]) - Resolves a hostname to an IP address.
    /// </summary>
    /// <remarks>
    /// Options can be:
    /// - A number specifying the address family (4 for IPv4, 6 for IPv6, 0 for both)
    /// - An object with { family?: number, hints?: number, all?: boolean }
    ///
    /// Returns:
    /// - If all is false (default): { address: string, family: number }
    /// - If all is true: Array of { address: string, family: number }
    ///
    /// In Node.js this is async with callback, but SharpTS uses synchronous pattern.
    /// </remarks>
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

    private static bool IsTruthy(object? value)
    {
        return value switch
        {
            null => false,
            bool b => b,
            double d => d != 0 && !double.IsNaN(d),
            string s => s.Length > 0,
            _ => true
        };
    }

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
}
