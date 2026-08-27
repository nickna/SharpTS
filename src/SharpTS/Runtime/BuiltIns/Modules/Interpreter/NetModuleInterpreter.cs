using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the stdlib-internal primitive:net seam.
/// </summary>
/// <remarks>
/// Provides TCP networking functionality:
/// - createServer(options?, connectionListener?) - create a TCP server
/// - createConnection(options, connectListener?) - create a TCP client
/// - createSocket(options?) - create an unconnected native socket
/// - createBlockList() - create the native server-filtering handle
/// </remarks>
public static class NetModuleInterpreter
{
    /// <summary>
    /// Gets all exported values for the net module.
    /// </summary>
    public static Dictionary<string, object?> GetPrimitiveExports()
    {
        return new Dictionary<string, object?>
        {
            ["createServer"] = BuiltInMethod.CreateV2("createServer", 0, 2, CreateServer),
            ["createConnection"] = BuiltInMethod.CreateV2("createConnection", 1, 3, CreateConnection),
            ["createSocket"] = BuiltInMethod.CreateV2("createSocket", 0, 1, CreateSocket),
            ["createBlockList"] = BuiltInMethod.CreateV2("createBlockList", 0, 0, CreateBlockList),
        };
    }

    /// <summary>
    /// Creates the opaque native handle owned by the TypeScript BlockList facade.
    /// </summary>
    private static RuntimeValue CreateBlockList(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        return RuntimeValue.FromObject(new SharpTSBlockList());
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

}
