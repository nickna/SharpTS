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
            // "fs" — migrated to stdlib/node/fs.ts which imports from primitive:fs.
            //   FsModuleInterpreter is reused by PrimitiveModuleValues; not routed here.
            // "fs/promises" — migrated to stdlib/node/fs/promises.ts which imports from primitive:fs/promises.
            //   FsPromisesModuleInterpreter is reused by PrimitiveModuleValues; not routed here.
            // "path" — migrated to stdlib/node/path.ts (pure-TS, uses primitive:process for cwd).
            // "os" — migrated to stdlib/node/os.ts which imports from primitive:os.
            //   OsModuleInterpreter is reused by PrimitiveModuleValues; not routed here.
            // "assert" — migrated to stdlib/node/assert.ts (pure-logic leaf).
            // "url" — migrated to stdlib/node/url.ts (full WHATWG state machine).
            // "util" — migrated to stdlib/node/util.ts (pure-TS port).
            // "process" — migrated to stdlib/node/process.ts which imports from primitive:process.
            //   ProcessModuleInterpreter is reused by PrimitiveModuleValues; not routed here.
            "crypto" => CryptoModuleInterpreter.GetExports(),
            // "readline" — migrated to stdlib/node/readline.ts which imports from primitive:readline.
            //   ReadlinePrimitiveInterpreter is reused by PrimitiveModuleValues; not routed here.
            "child_process" => ChildProcessModuleInterpreter.GetExports(),
            "buffer" => BufferModuleInterpreter.GetExports(),
            // "zlib" — migrated to stdlib/node/zlib.ts which imports from primitive:zlib.
            //   ZlibModuleInterpreter is reused by PrimitiveModuleValues; not routed here.
            // "events" — migrated to stdlib/node/events.ts (pure-TS EventEmitter).
            // "timers" / "timers/promises" — migrated to stdlib/node/timers{,/promises}.ts.
            //   TimersPrimitiveInterpreter is reused by PrimitiveModuleValues.
            // "string_decoder" — migrated to stdlib/node/string_decoder.ts (pure-TS over Buffer API).
            // "perf_hooks" — migrated to stdlib/node/perf_hooks.ts (pure-TS over primitive:perf).
            "stream" => StreamModuleInterpreter.GetExports(),
            "stream/promises" => StreamPromisesModuleInterpreter.GetExports(),
            "stream/web" => StreamWebModuleInterpreter.GetExports(),
            "http" => HttpModuleInterpreter.GetExports(),
            "worker_threads" => WorkerThreadsModuleInterpreter.GetExports(),
            "dns" => DnsModuleInterpreter.GetExports(),
            "dns/promises" => DnsModuleInterpreter.GetPromisesExports(),
            "net" => NetModuleInterpreter.GetExports(),
            "https" => HttpModuleInterpreter.GetHttpsExports(), // real TLS server (#1049) + client over TLS (#1050)
            "tls" => TlsModuleInterpreter.GetExports(),
            "dgram" => DgramModuleInterpreter.GetExports(),
            "cluster" => ClusterModuleInterpreter.GetExports(),
            "vm" => VmModuleInterpreter.GetExports(),
            "sharpts:execution" => SourceExecutionModuleInterpreter.GetExports(),
            // "async_hooks" — migrated to stdlib/node/async_hooks.ts (TS class over primitive:async_hooks).
            // "tty" — migrated to stdlib/node/tty.ts (pure-TS over primitive:tty).
            _ => throw new Exception($"Unknown built-in module: {moduleName}")
        };
    }

    /// <summary>
    /// Builds a custom namespace/default-export object for built-in modules whose
    /// namespace members need live accessor semantics instead of import-time value
    /// snapshots. Returns null for modules where a plain snapshot object is correct.
    /// </summary>
    /// <remarks>
    /// cluster (#1167/#1170): isPrimary/isWorker/worker/workers/settings resolve live,
    /// and `cluster.schedulingPolicy = ...` writes through to the singleton so the
    /// shared-listener dispatch honors it.
    /// </remarks>
    public static Types.SharpTSObject? TryCreateNamespaceOverride(string moduleName, Dictionary<string, object?> exports)
    {
        return moduleName == "cluster"
            ? ClusterModuleInterpreter.CreateNamespaceObject(exports)
            : null;
    }

    /// <summary>
    /// Checks if a module has interpreter support.
    /// </summary>
    public static bool HasInterpreterSupport(string moduleName)
    {
        // Single source of truth: the registry's module set (previously a third
        // hand-maintained copy of the same 16-entry list — 2026-07 cleanup).
        return BuiltInModuleRegistry.IsBuiltIn(moduleName);
    }
}
