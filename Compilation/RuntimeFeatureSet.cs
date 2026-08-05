namespace SharpTS.Compilation;

/// <summary>
/// Records which categories of runtime helper types the compiled program needs,
/// so <see cref="RuntimeEmitter.EmitAll"/> can skip emitting unused machinery.
///
/// Default constructor sets every flag to <c>true</c> — i.e., "emit everything,"
/// matching pre-tree-shaking behavior. Callers that have run
/// <see cref="RuntimeFeatureDetector"/> against the AST get a set with most flags
/// flipped to <c>false</c>, and only the actually-used categories <c>true</c>.
///
/// Phase 1 covers Tier A categories from <c>docs/plans/runtime-tree-shaking.md</c>:
/// network/HTTP/TLS/DNS/dgram/cluster/fs/streams/crypto/zlib/typed-arrays/etc.
/// Tier B (Promise, RegExp, Date, Map/Set, iterator helpers) is not gated yet —
/// those flags don't exist on this set.
/// </summary>
public sealed class RuntimeFeatureSet
{
    // ── Network family ────────────────────────────────────────────────────
    public bool UsesNet { get; set; } = true;       // 'net' module / NetServer / NetSocket
    public bool UsesHttp { get; set; } = true;      // 'http'/'https' module / HttpServer
    public bool UsesTls { get; set; } = true;       // 'tls' module / TLSSocket
    public bool UsesDgram { get; set; } = true;     // 'dgram' module
    public bool UsesDns { get; set; } = true;       // 'dns'/'dns/promises'
    public bool UsesFetch { get; set; } = true;     // fetch() / Headers / Request / Response

    // ── Storage / I/O family ──────────────────────────────────────────────
    public bool UsesFs { get; set; } = true;        // 'fs'/'fs/promises'
    public bool UsesCrypto { get; set; } = true;    // 'crypto'/'crypto/promises'
    public bool UsesZlib { get; set; } = true;      // 'zlib'

    // ── Stream APIs ───────────────────────────────────────────────────────
    public bool UsesNodeStreams { get; set; } = true;  // 'stream'/'stream/promises'
    public bool UsesWebStreams { get; set; } = true;   // ReadableStream / WritableStream / TransformStream

    // ── Worker / multi-process ────────────────────────────────────────────
    public bool UsesCluster { get; set; } = true;       // 'cluster'
    public bool UsesBroadcastChannel { get; set; } = true;
    public bool UsesAsyncLocalStorage { get; set; } = true;

    // ── Misc emitted-runtime types ────────────────────────────────────────
    public bool UsesReadline { get; set; } = true;          // 'readline'
    public bool UsesTextEncoding { get; set; } = true;      // TextEncoder / TextDecoder
    public bool UsesFinalizationRegistry { get; set; } = true;
    public bool UsesReflectMetadata { get; set; } = true;   // Reflect.metadata / Reflect.defineMetadata
    public bool UsesCjsRequire { get; set; } = true;        // require() / module.exports
    public bool UsesJSON { get; set; } = true;              // JSON.parse / JSON.stringify
    public bool UsesIntl { get; set; } = true;              // Intl.NumberFormat, DateTimeFormat, Collator
    public bool UsesReflect { get; set; } = true;           // Reflect.set/get/deleteProperty/has/etc.
    public bool UsesIteratorHelpers { get; set; } = true;   // Iterator.prototype.map/filter/flatMap/take/drop
    public bool UsesDate { get; set; } = true;              // new Date(), Date.now(), Date.X
    public bool UsesRegExp { get; set; } = true;            // /pattern/ or new RegExp()
    public bool UsesBuffer { get; set; } = true;            // Buffer.from(), new Buffer() — also implied by crypto/fs/zlib/http/fetch/dgram
    public bool UsesBigInt { get; set; } = true;            // BigInt(), 123n literal, BigInt arithmetic operators
    public bool UsesOs { get; set; } = true;                // 'os' module — os.freemem, os.loadavg, os.networkInterfaces
    public bool UsesChildProcess { get; set; } = true;      // 'child_process' module — spawn, exec, fork, execSync, etc.
    public bool UsesVm { get; set; } = true;                // 'vm' module — vm.runInNewContext, vm.compileFunction, etc.
    public bool UsesSourceExecution { get; set; } = true;   // 'sharpts:execution' trusted-host bridge
    public bool UsesTty { get; set; } = true;               // 'tty' module / process.stdout.isTTY — just isatty(fd)
    public bool UsesPerf { get; set; } = true;              // performance.now() / performance.timeOrigin (host-tied primitive)
    public bool UsesAbortController { get; set; } = true;   // AbortController / AbortSignal identifiers
    public bool UsesAbortSignalAny { get; set; } = true;    // AbortSignal.any([...]) — late-binds to SharpTS runtime, needs SharpTS.dll co-located
    public bool UsesProxy { get; set; } = true;             // `new Proxy(...)` / bare `Proxy` identifier
    public bool UsesDynamicImport { get; set; } = true;     // `import(specifier)` syntax (Expr.DynamicImport)
    public bool UsesAsyncGenerator { get; set; } = true;    // `async function*` / async generators
    public bool UsesForAwaitOf { get; set; } = true;        // `for await (... of ...)`
    public bool UsesWeakRef { get; set; } = true;           // `new WeakRef(target)` — bare or `new`
    public bool UsesWeakMap { get; set; } = true;           // `new WeakMap(...)` — bare or `new`
    public bool UsesWeakSet { get; set; } = true;           // `new WeakSet(...)` — bare or `new`
    public bool UsesMap { get; set; } = true;               // `new Map(...)` / `Map.groupBy` — bare or `new`
    public bool UsesSet { get; set; } = true;               // `new Set(...)` — bare or `new`

    // ── Typed arrays ──────────────────────────────────────────────────────
    /// <summary>
    /// Bitset of typed-array kinds the program references. A test using only
    /// <c>Float32Array</c> shouldn't drag in <c>$BigInt64Array</c>, etc.
    /// </summary>
    public TypedArrayKinds TypedArrays { get; set; } = TypedArrayKinds.All;

    [Flags]
    public enum TypedArrayKinds
    {
        None = 0,
        Int8 = 1 << 0,
        Uint8 = 1 << 1,
        Uint8Clamped = 1 << 2,
        Int16 = 1 << 3,
        Uint16 = 1 << 4,
        Int32 = 1 << 5,
        Uint32 = 1 << 6,
        Float32 = 1 << 7,
        Float64 = 1 << 8,
        BigInt64 = 1 << 9,
        BigUint64 = 1 << 10,
        ArrayBuffer = 1 << 11,
        SharedArrayBuffer = 1 << 12,
        DataView = 1 << 13,
        TypedArrayBase = 1 << 14, // emitted whenever any concrete typed-array type is

        All = Int8 | Uint8 | Uint8Clamped | Int16 | Uint16 | Int32 | Uint32
            | Float32 | Float64 | BigInt64 | BigUint64
            | ArrayBuffer | SharedArrayBuffer | DataView | TypedArrayBase,
    }

    /// <summary>
    /// True if any typed-array kind is referenced (gates the whole $ArrayBuffer /
    /// $SharedArrayBuffer / $DataView / $Int8Array / etc. cluster together).
    /// </summary>
    public bool HasAnyTypedArray => TypedArrays != TypedArrayKinds.None;

    /// <summary>
    /// Returns a <see cref="RuntimeFeatureSet"/> with every flag set to <c>true</c>.
    /// Equivalent to the default constructor; named for clarity at call sites that
    /// want to opt out of tree-shaking ("emit all helper types").
    /// </summary>
    public static RuntimeFeatureSet EmitEverything() => new();
}
