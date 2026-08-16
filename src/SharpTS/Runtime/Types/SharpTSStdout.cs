using SharpTS.Runtime.BuiltIns;
using Interp = SharpTS.Execution.Interpreter;

namespace SharpTS.Runtime.Types;

/// <summary>
/// Singleton Writable stream for process.stdout.
/// Writes to the interpreter's Out writer (or Console.Out in compiled mode)
/// and emits Writable stream events.
/// </summary>
/// <remarks>
/// In Node.js, process.stdout is a special never-ending Writable stream.
/// end() and destroy() are no-ops to prevent corrupting the singleton state.
/// </remarks>
public class SharpTSStdout : SharpTSWritable
{
    public static readonly SharpTSStdout Instance = new();

    private SharpTSStdout()
    {
        SetWriteCallback(new StdoutWriteCallback());
    }

    /// <summary>
    /// Returns true if stdout is connected to a terminal (not redirected).
    /// </summary>
    public bool IsTTY => !Console.IsOutputRedirected;

    /// <summary>
    /// Gets a member by name, adding stdout-specific properties on top of Writable.
    /// Overrides end/destroy to be no-ops (process.stdout never ends in Node.js).
    /// </summary>
    /// <remarks>
    /// Now that <see cref="SharpTSEventEmitter.GetMember"/> is virtual (#1139) this is a
    /// normal override: the compiled-mode reflection fallback resolves the single virtual
    /// slot unambiguously and dispatches to this most-derived implementation.
    /// </remarks>
    public override object? GetMember(string name)
    {
        return name switch
        {
            "isTTY" => IsTTY,
            "columns" => IsTTY ? (double)Console.WindowWidth : null,
            "rows" => IsTTY ? (double)Console.WindowHeight : null,
            // Node exposes stdout's file descriptor as `fd === 1`.
            "fd" => 1.0,
            // process.stdout never ends or destroys — no-op to protect singleton state
            "end" => BuiltInMethod.CreateV2("end", 0, 3, (_, _, _) => RuntimeValue.FromObject(this)),
            "destroy" => BuiltInMethod.CreateV2("destroy", 0, 1, (_, _, _) => RuntimeValue.FromObject(this)),
            _ => base.GetMember(name)
        };
    }

    public override string ToString() => "[object stdout]";

    private sealed class StdoutWriteCallback : ISharpTSCallable
    {
        public int Arity() => 3;

        public object? Call(Interp interpreter, List<object?> arguments)
        {
            var chunk = arguments.Count > 0 ? arguments[0] : null;
            var callback = arguments.Count > 2 ? arguments[2] as ISharpTSCallable : null;

            var data = chunk?.ToString() ?? "";

            // Use the interpreter's Out writer if available (enables test output capture),
            // otherwise fall back to Console.Out (compiled mode).
            if (interpreter != null)
                interpreter.Out.Write(data);
            else
                Console.Write(data);

            callback?.Call(interpreter!, []);
            return null;
        }
    }
}
