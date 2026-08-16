using SharpTS.Runtime.Types;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-mode implementation of the Node.js 'dgram' module (UDP sockets).
/// </summary>
public static class DgramModuleInterpreter
{
    public static Dictionary<string, object?> GetExports()
    {
        return new Dictionary<string, object?>
        {
            ["createSocket"] = BuiltInMethod.CreateV2("createSocket", 1, 2, CreateSocket),
            ["Socket"] = SharpTSDatagramSocketConstructor.Instance
        };
    }

    private static RuntimeValue CreateSocket(Interp interpreter, RuntimeValue receiver, ReadOnlySpan<RuntimeValue> args)
    {
        string type = "udp4";

        if (args.Length > 0)
        {
            if (args[0].IsString) type = args[0].AsStringUnsafe();
            else if (args[0].ToObject() is SharpTSObject options)
            {
                if (options.GetProperty("type") is string ot) type = ot;
            }
        }

        var socket = new SharpTSDatagramSocket(type);

        // If callback provided, attach as 'message' listener
        if (args.Length > 1 && args[1].ToObject() is ISharpTSCallable callback)
        {
            var onMethod = socket.GetMember("on") as BuiltInMethod;
            onMethod?.Bind(socket).Call(interpreter, new List<object?> { "message", callback });
        }

        return RuntimeValue.FromObject(socket);
    }

    public sealed class SharpTSDatagramSocketConstructor : ISharpTSCallable
    {
        public static readonly SharpTSDatagramSocketConstructor Instance = new();
        private SharpTSDatagramSocketConstructor() { }

        public int Arity() => 1;

        public object? Call(Interp interpreter, List<object?> args)
        {
            var rvArgs = new RuntimeValue[args.Count];
            for (int i = 0; i < args.Count; i++)
                rvArgs[i] = RuntimeValue.FromBoxed(args[i]);
            return CreateSocket(interpreter, RuntimeValue.Null, rvArgs).ToObject();
        }
    }
}
