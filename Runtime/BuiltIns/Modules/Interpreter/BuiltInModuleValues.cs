namespace SharpTS.Runtime.BuiltIns.Modules.Interpreter;

/// <summary>
/// Provides runtime values for built-in modules in interpreter mode.
/// </summary>
/// <remarks>
/// Maps module names to their exported values (functions, constants, objects).
/// Used by the interpreter to populate module exports when loading built-in modules.
/// The compiler uses IL emitters instead; this is the interpreter-only equivalent.
/// </remarks>
public static class BuiltInModuleValues
{
    /// <summary>
    /// Gets the exported values for a built-in module.
    /// </summary>
    /// <param name="moduleName">The module name (e.g., "fs", "path", "os").</param>
    /// <returns>Dictionary of exported names to runtime values.</returns>
    /// <exception cref="Exception">If the module is not a known built-in module.</exception>
    public static Dictionary<string, object?> GetModuleExports(string moduleName)
    {
        return moduleName switch
        {
            "fs" => FsModuleInterpreter.GetExports(),
            "path" => PathModuleInterpreter.GetExports(),
            "os" => OsModuleInterpreter.GetExports(),
            "querystring" => QuerystringModuleInterpreter.GetExports(),
            "assert" => AssertModuleInterpreter.GetExports(),
            "url" => UrlModuleInterpreter.GetExports(),
            "process" => ProcessModuleInterpreter.GetExports(),
            "crypto" => CryptoModuleInterpreter.GetExports(),
            "util" => UtilModuleInterpreter.GetExports(),
            "readline" => ReadlineModuleInterpreter.GetExports(),
            "child_process" => ChildProcessModuleInterpreter.GetExports(),
            "buffer" => BufferModuleInterpreter.GetExports(),
            "zlib" => ZlibModuleInterpreter.GetExports(),
            "events" => EventsModuleInterpreter.GetExports(),
            "timers" => TimersModuleInterpreter.GetExports(),
            "string_decoder" => StringDecoderModuleInterpreter.GetExports(),
            "perf_hooks" => PerfHooksModuleInterpreter.GetExports(),
            "stream" => StreamModuleInterpreter.GetExports(),
            "http" => HttpModuleInterpreter.GetExports(),
            "worker_threads" => WorkerThreadsModuleInterpreter.GetExports(),
            "dns" => DnsModuleInterpreter.GetExports(),
            _ => throw new Exception($"Unknown built-in module: {moduleName}")
        };
    }

    /// <summary>
    /// Checks if a module has interpreter support.
    /// </summary>
    public static bool HasInterpreterSupport(string moduleName)
    {
        return moduleName is "fs" or "path" or "os" or "querystring" or "assert" or "url"
            or "process" or "crypto" or "util" or "readline" or "child_process" or "buffer"
            or "zlib" or "events" or "timers" or "string_decoder" or "perf_hooks" or "stream"
            or "http" or "worker_threads" or "dns";
    }
}
