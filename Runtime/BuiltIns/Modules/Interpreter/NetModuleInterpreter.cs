using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'net' module.
/// </summary>
/// <remarks>
/// Provides TCP networking functionality:
/// - createServer(options?, connectionListener?) - create a TCP server
/// - createConnection(options, connectListener?) / connect() - create a TCP client
/// - Server and Socket constructors
/// - isIP(), isIPv4(), isIPv6() utility functions
/// </remarks>
public static class NetModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the net module.
    /// </summary>
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["createServer"] = BuiltInMethod.CreateV2("createServer", 0, 2, CreateServer),
            // Node signature: connect(options|port|path[, host][, connectListener])
            // — three positional args; the socket's own connect does the parsing.
            ["createConnection"] = BuiltInMethod.CreateV2("createConnection", 1, 3, CreateConnection),
            ["connect"] = BuiltInMethod.CreateV2("connect", 1, 3, CreateConnection),
            ["isIP"] = BuiltInMethod.CreateV2("isIP", 1, IsIP),
            ["isIPv4"] = BuiltInMethod.CreateV2("isIPv4", 1, IsIPv4),
            ["isIPv6"] = BuiltInMethod.CreateV2("isIPv6", 1, IsIPv6),
            ["Server"] = BuiltInMethod.CreateV2("Server", 0, 2, CreateServer),
            ["Socket"] = BuiltInMethod.CreateV2("Socket", 0, 1, CreateSocket)
        };
    }

    /// <summary>
    /// Creates a new TCP server.
    /// </summary>
    private static RuntimeValue CreateServer(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        ISharpTSCallable? connectionListener = null;
        SharpTSObject? options = null;

        if (args.Length > 0)
        {
            if (args[0].ToObject() is ISharpTSCallable cb)
            {
                connectionListener = cb;
            }
            else if (args[0].ToObject() is SharpTSObject opts)
            {
                options = opts;
                if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable cb2)
                    connectionListener = cb2;
            }
        }

        var server = new SharpTSNetServer(connectionListener);
        if (options != null)
            server.ConfigureFromOptions(options);
        return RuntimeValue.FromObject(server);
    }

    /// <summary>
    /// Creates a new TCP connection (client socket).
    /// </summary>
    private static RuntimeValue CreateConnection(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var socket = new SharpTSSocket();

        // Delegate to socket.connect()
        var connectMethod = socket.GetMember("connect") as BuiltInMethod;
        var connectArgs = new List<object?>(args.Length);
        for (int i = 0; i < args.Length; i++)
            connectArgs.Add(args[i].ToObject());
        connectMethod?.Bind(socket).Call(interpreter, connectArgs);

        return RuntimeValue.FromObject(socket);
    }

    /// <summary>
    /// Creates a new unconnected Socket.
    /// </summary>
    private static RuntimeValue CreateSocket(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        var socket = new SharpTSSocket();
        if (args.Length > 0 && args[0].ToObject() is SharpTSObject options)
            socket.ConfigureFromOptions(options);
        return RuntimeValue.FromObject(socket);
    }

    /// <summary>
    /// Tests if input is an IP address. Returns 4 for IPv4, 6 for IPv6, 0 for invalid.
    /// </summary>
    private static RuntimeValue IsIP(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            return RuntimeValue.FromNumber(0.0);

        if (System.Net.IPAddress.TryParse(args[0].AsStringUnsafe(), out var addr))
        {
            return RuntimeValue.FromNumber(
                addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? 6.0 : 4.0);
        }
        return RuntimeValue.FromNumber(0.0);
    }

    /// <summary>
    /// Tests if input is an IPv4 address.
    /// </summary>
    private static RuntimeValue IsIPv4(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            return RuntimeValue.False;

        return RuntimeValue.FromBoolean(System.Net.IPAddress.TryParse(args[0].AsStringUnsafe(), out var addr)
            && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
    }

    /// <summary>
    /// Tests if input is an IPv6 address.
    /// </summary>
    private static RuntimeValue IsIPv6(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        if (args.Length == 0 || !args[0].IsString)
            return RuntimeValue.False;

        return RuntimeValue.FromBoolean(System.Net.IPAddress.TryParse(args[0].AsStringUnsafe(), out var addr)
            && addr.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6);
    }
}
