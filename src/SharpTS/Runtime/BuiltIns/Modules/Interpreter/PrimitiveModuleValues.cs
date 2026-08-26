namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Interpreter-side dispatch for <c>primitive:*</c> modules — the narrow C# interop
/// surface that stdlib TypeScript modules rely on. Analogous to
/// <see cref="BuiltInModuleValues"/> but limited to stdlib-only primitives.
/// </summary>
/// <remarks>
/// Primitive modules share implementation with their matching user-facing built-in
/// (e.g. <c>primitive:os</c> returns the same exports as <c>os</c>), deliberately
/// reusing the existing C# code. The distinction is architectural: stdlib TS
/// modules target the primitive layer; user code targets the Node-compatible
/// module surface. When a leaf migrates to TS (stdlib/node/os.ts), the user-facing
/// name flips to TS while the primitive retains the C# implementation.
/// </remarks>
public static class PrimitiveModuleValues
{
    /// <summary>
    /// Gets the exported values for a primitive module (without the "primitive:" prefix).
    /// </summary>
    public static Dictionary<string, object?> GetPrimitiveExports(string primitiveName)
    {
        return primitiveName switch
        {
            "os" => OsModuleInterpreter.GetExports(),
            "process" => ProcessModuleInterpreter.GetExports(),
            "perf" => PerfPrimitiveInterpreter.GetExports(),
            "tty" => TtyPrimitiveInterpreter.GetExports(),
            "async_hooks" => AsyncHooksPrimitiveInterpreter.GetExports(),
            "timers" => TimersPrimitiveInterpreter.GetExports(),
            "timers/promises" => TimersPrimitiveInterpreter.GetPromisesExports(),
            "readline" => ReadlinePrimitiveInterpreter.GetExports(),
            "module" => ModulePrimitiveInterpreter.GetExports(),
            "stream/consumers" => StreamConsumersPrimitiveInterpreter.GetExports(),
            "fs" => FsModuleInterpreter.GetExports(),
            "fs/promises" => FsPromisesModuleInterpreter.GetExports(),
            "zlib" => ZlibModuleInterpreter.GetExports(),
            "dns" => DnsModuleInterpreter.GetPrimitiveExports(),
            "dns/promises" => DnsModuleInterpreter.GetPromisesExports(),
            _ => throw new Exception($"Unknown primitive module: primitive:{primitiveName}")
        };
    }

    /// <summary>
    /// Whether a given primitive name has an interpreter-mode implementation.
    /// </summary>
    public static bool HasInterpreterSupport(string primitiveName)
    {
        return primitiveName is "os" or "process" or "perf" or "tty" or "async_hooks"
            or "timers" or "timers/promises" or "readline" or "module" or "stream/consumers"
            or "fs" or "fs/promises" or "zlib" or "dns" or "dns/promises";
    }
}
