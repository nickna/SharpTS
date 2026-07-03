namespace SharpTS.Runtime.BuiltIns.Modules;

/// <summary>
/// Registry of built-in Node.js-compatible modules.
/// These modules are intercepted during import resolution and handled specially
/// rather than being resolved from the filesystem.
/// </summary>
public static class BuiltInModuleRegistry
{
    private static readonly HashSet<string> _builtInModules =
    [
        // "fs" — migrated to stdlib/node/fs.ts (embedded stdlib, wraps primitive:fs + primitive:fs/promises).
        // "fs/promises" — migrated to stdlib/node/fs/promises.ts (embedded stdlib, wraps primitive:fs/promises).
        // "path" — migrated to stdlib/node/path.ts (embedded stdlib, uses primitive:process for cwd).
        // "os" — migrated to stdlib/node/os.ts (embedded stdlib, wraps primitive:os).
        // "querystring" — migrated to stdlib/node/querystring.ts (embedded stdlib).
        // "assert" — migrated to stdlib/node/assert.ts (embedded stdlib, pure-logic leaf).
        // "url" — migrated to stdlib/node/url.ts (embedded stdlib, full WHATWG state machine).
        //   URL/URLSearchParams classes fully implemented in TS; no System.Uri dependency.
        // The EmbeddedStdlibProvider claims these specifiers via the chain.
        // "process" — migrated to stdlib/node/process.ts (embedded stdlib, wraps primitive:process).
        //   Global `process` binding (bare `process.cwd()`) is unaffected — registered separately
        //   in BuiltInRegistry as a singleton namespace.
        "crypto",
        // "util" — migrated to stdlib/node/util.ts (embedded stdlib, pure-TS port).
        // "readline" — migrated to stdlib/node/readline.ts (TS wrapper over primitive:readline,
        //   class-instance-via-primitive pattern like async_hooks).
        "child_process",
        "buffer",
        // "zlib" — migrated to stdlib/node/zlib.ts (TS facade over primitive:zlib;
        //   compression cores + Transform streams stay C#/IL behind the primitive).
        // "events" — migrated to stdlib/node/events.ts (pure-TS EventEmitter).
        // "timers" / "timers/promises" — migrated to stdlib/node/timers{,/promises}.ts
        //   (TS facades over primitive:timers and primitive:timers/promises).
        // "string_decoder" — migrated to stdlib/node/string_decoder.ts (uses Buffer JS API).
        // "perf_hooks" — migrated to stdlib/node/perf_hooks.ts (pure-TS over primitive:perf).
        "stream",
        "stream/promises",
        "stream/web",
        "http",
        "worker_threads",
        "dns",
        "dns/promises",
        "net",
        "https",
        "tls",
        "dgram",
        "cluster",
        "vm",
        // "async_hooks" — migrated to stdlib/node/async_hooks.ts (TS class over primitive:async_hooks).
        // "tty" — migrated to stdlib/node/tty.ts (pure-TS over primitive:tty).
    ];

    /// <summary>
    /// Checks if a module specifier refers to a built-in module.
    /// </summary>
    /// <param name="specifier">The import specifier (e.g., "fs", "path").</param>
    /// <returns>True if this is a built-in module.</returns>
    public static bool IsBuiltIn(string specifier) => _builtInModules.Contains(specifier);

    /// <summary>
    /// Gets all registered built-in module names.
    /// </summary>
    public static IReadOnlySet<string> GetBuiltInModules() => _builtInModules;

    /// <summary>
    /// The sentinel prefix used for built-in module paths.
    /// </summary>
    public const string BuiltInPrefix = "builtin:";

    /// <summary>
    /// Creates the sentinel path for a built-in module.
    /// </summary>
    public static string GetBuiltInPath(string moduleName) => $"{BuiltInPrefix}{moduleName}";

    /// <summary>
    /// Extracts the module name from a built-in sentinel path.
    /// </summary>
    public static string? GetModuleName(string path)
    {
        if (path.StartsWith(BuiltInPrefix))
            return path[BuiltInPrefix.Length..];
        return null;
    }
}
