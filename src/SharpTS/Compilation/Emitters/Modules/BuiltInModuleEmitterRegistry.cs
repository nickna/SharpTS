namespace SharpTS.Compilation.Emitters.Modules;

/// <summary>
/// Compiler-owned dispatch index with checked registration and an explicit completion boundary.
/// </summary>
public sealed class BuiltInModuleEmitterRegistry
{
    private readonly Dictionary<string, IBuiltInModuleEmitter> _emitters = new(StringComparer.Ordinal);

    /// <summary>Whether registration is complete and the dispatch index is frozen.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Creates the complete strategy index owned by one compiler.</summary>
    internal static BuiltInModuleEmitterRegistry CreateDefault()
    {
        var registry = new BuiltInModuleEmitterRegistry();
        // Built-in module emitters
        registry.Register(new OsModuleEmitter());
        registry.Register(new FsModuleEmitter());
        // "path"         — migrated to stdlib/node/path.ts (pure-TS, uses primitive:process for cwd).
        // "querystring"  — migrated to stdlib/node/querystring.ts.
        // "assert"       — migrated to stdlib/node/assert.ts (pure-logic leaf).
        // "url"          — migrated to stdlib/node/url.ts (full WHATWG state machine).
        // "process"      — migrated to stdlib/node/process.ts which imports from primitive:process.
        //   ProcessModuleEmitter remains, registered only under the primitive specifier.
        var processEmitter = new ProcessModuleEmitter();
        registry.RegisterAlias("primitive:process", processEmitter);
        registry.Register(new CryptoModuleEmitter());
        // "util" — migrated to stdlib/node/util.ts (pure-TS port).
        // "readline" — migrated to stdlib/node/readline.ts; emitter registered under primitive:readline only.
        registry.Register(new ReadlinePrimitiveEmitter());
        registry.Register(new ModulePrimitiveEmitter());
        registry.Register(new StreamConsumersPrimitiveEmitter());
        registry.Register(new ChildProcessModuleEmitter());
        registry.Register(new BufferModuleEmitter());
        // "zlib" — migrated to stdlib/node/zlib.ts; emitter registered under primitive:zlib only.
        registry.Register(new ZlibModuleEmitter());
        // "events" — migrated to stdlib/node/events.ts (pure-TS EventEmitter).
        // "timers" and "timers/promises" migrated to stdlib/node/timers{,/promises}.ts
        //   (TS facades over primitive:timers and primitive:timers/promises respectively).
        registry.Register(new TimersPrimitiveEmitter());
        registry.Register(new TimersPromisesPrimitiveEmitter());
        // "string_decoder" — migrated to stdlib/node/string_decoder.ts.
        // "perf_hooks" — migrated to stdlib/node/perf_hooks.ts (pure-TS over primitive:perf).
        //   Only the narrow now() method needs host access; mark/measure/observer are TS.
        registry.Register(new PerfPrimitiveEmitter());
        registry.Register(new StreamModuleEmitter());
        registry.Register(new StreamPromisesModuleEmitter());
        registry.Register(new StreamWebModuleEmitter());
        registry.Register(new HttpModuleEmitter());
        registry.Register(new WorkerThreadsModuleEmitter());
        // "dns" / "dns/promises" migrated to stdlib TS facades. These emitters
        // now serve only the stdlib-internal primitive:dns{,/promises} seams.
        registry.Register(new DnsModuleEmitter());
        registry.Register(new DnsPromisesModuleEmitter());
        registry.Register(new FsPromisesModuleEmitter());
        // "net" migrated to stdlib/node/net.ts; emitter serves primitive:net.
        registry.Register(new NetModuleEmitter());
        registry.Register(new TlsModuleEmitter());
        registry.Register(new DgramModuleEmitter());
        registry.Register(new ClusterModuleEmitter());
        registry.Register(new VmModuleEmitter());
        registry.Register(new SourceExecutionModuleEmitter());
        // "async_hooks" migrated to stdlib/node/async_hooks.ts (TS class over primitive:async_hooks).
        registry.Register(new AsyncHooksPrimitiveEmitter());
        // "tty" migrated to stdlib/node/tty.ts (pure-TS over primitive:tty).
        registry.Register(new TtyPrimitiveEmitter());

        // https delegates to http emitter
        registry.Register(new HttpsModuleEmitterProxy());
        registry.CompleteRegistration();
        return registry;
    }

    /// <summary>Freezes registration; existing declarations remain available for lookup.</summary>
    public void CompleteRegistration()
    {
        EnsureRegistering();
        IsComplete = true;
    }

    private void EnsureRegistering()
    {
        if (IsComplete)
            throw new InvalidOperationException("Built-in module emitter registration is complete.");
    }

    private void Add(string name, IBuiltInModuleEmitter emitter)
    {
        EnsureRegistering();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(emitter);
        if (!_emitters.TryAdd(name, emitter))
            throw new InvalidOperationException($"Built-in module emitter '{name}' is already registered.");
    }

    /// <summary>
    /// Registers an emitter for a built-in module. Duplicate keys and writes after completion are rejected.
    /// </summary>
    /// <param name="emitter">The emitter to register.</param>
    public void Register(IBuiltInModuleEmitter emitter)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        Add(emitter.ModuleName, emitter);
    }

    /// <summary>
    /// Registers an emitter under an additional key (alias). Used when a single
    /// emitter serves multiple specifiers, or is intentionally exposed only under a
    /// primitive specifier such as <c>primitive:process</c>. The canonical name
    /// need not be registered; aliases retain the exact supplied strategy instance.
    /// </summary>
    public void RegisterAlias(string alias, IBuiltInModuleEmitter emitter)
    {
        Add(alias, emitter);
    }

    /// <summary>
    /// Gets the emitter for a built-in module.
    /// </summary>
    /// <param name="moduleName">The module name (e.g., "fs", "path").</param>
    /// <returns>The emitter, or null if not found.</returns>
    public IBuiltInModuleEmitter? GetEmitter(string moduleName)
    {
        return _emitters.GetValueOrDefault(moduleName);
    }
}
