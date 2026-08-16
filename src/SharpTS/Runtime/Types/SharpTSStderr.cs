using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton Writable stream for process.stderr.
/// Writes to the interpreter's Error writer (or Console.Error in compiled mode)
/// and emits Writable stream events.
/// </summary>
/// <remarks>
/// In Node.js, process.stderr is a special never-ending Writable stream.
/// end() and destroy() are no-ops to prevent corrupting the singleton state.
/// </remarks>
public class SharpTSStderr : SharpTSWritable
{
    public static readonly SharpTSStderr Instance = new();

    private SharpTSStderr()
    {
        SetWriteCallback(new StderrWriteCallback());
    }

    /// <summary>
    /// Returns true if stderr is connected to a terminal (not redirected).
    /// </summary>
    public bool IsTTY => !Console.IsErrorRedirected;

    /// <summary>
    /// Gets a member by name, adding stderr-specific properties on top of Writable.
    /// Overrides end/destroy to be no-ops (process.stderr never ends in Node.js).
    /// </summary>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "isTTY" => IsTTY,
            // Node exposes stderr's file descriptor as `fd === 2`. Packages
            // like `debug` read it to test `tty.isatty(process.stderr.fd)`.
            "fd" => 2.0,
            // process.stderr never ends or destroys — no-op to protect singleton state
            "end" => BuiltInMethod.CreateV2("end", 0, 3, (_, _, _) => RuntimeValue.FromObject(this)),
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, (_, _, _) => RuntimeValue.FromObject(this)),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object stderr]";

    private sealed class StderrWriteCallback : ISharpTSCallable
    {
        public int Arity() => 3;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            var callback = arguments.Count > 2 ? arguments[2] as ISharpTSCallable : null;

            var data = chunk?.ToString() ?? "";

            if (interpreter != null)
                interpreter.Error.Write(data);
            else
                Console.Error.Write(data);

            callback?.Call(interpreter!, []);
            return null;
        }
    }
}
