using SharpTS.Parsing;

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
/// Later phases also gate Promise/async, RegExp, Date, Map/Set, and iterator
/// helper families.
/// </summary>
public sealed class RuntimeFeatureSet
{
    /// <summary>
    /// Closed record shapes reachable from a direct, one-argument
    /// <c>JSON.stringify</c> call. Object-literal emission uses this allow-list
    /// to keep the compact JSON carrier out of unrelated object graphs.
    /// </summary>
    internal HashSet<string> JsonScalarRecordShapeFingerprints { get; } = [];

    /// <summary>
    /// Closed JSON record shapes keyed by their structural fingerprint.  The
    /// runtime emitter uses these to give primitive slots their native CLR
    /// types while retaining the scalar-record materialization fallback.
    /// </summary>
    internal Dictionary<string, JsonSerializationShape.Record> JsonScalarRecordShapes { get; } = [];

    /// <summary>
    /// Shapes of small plain-object literals, grouped by slot count. When an
    /// arity has a single shape, the JSON scalar carrier's CLR type is itself
    /// an exact shape guard and typed reads need not compare the lazy descriptor.
    /// Ordinary stable records use the per-fingerprint types below.
    /// </summary>
    internal Dictionary<int, HashSet<string>> CompactObjectRecordShapeFingerprints { get; } = [];

    internal Dictionary<string, JsonSerializationShape.Record> CompactObjectRecordShapes { get; } = [];

    /// <summary>
    /// Object literals stored by the same guarded, discarded-result array-push
    /// intrinsic used by the IL emitter. These literals may use their compact
    /// carrier even though the conservative call-escape analysis keeps the
    /// shape's materialization guard enabled.
    /// </summary>
    internal HashSet<Expr.ObjectLiteral> CompactObjectRecordStablePushLiterals { get; } =
        new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// Compact-record shapes used by at least one stable discarded array push.
    /// Only these shapes specialize scalar slots to native CLR field types.
    /// </summary>
    internal HashSet<string> CompactObjectRecordStablePushShapes { get; } =
        new(StringComparer.Ordinal);
    internal HashSet<string> CompactObjectRecordStableIteratorShapes { get; } =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Generic-looking compact-record slots whose literal initializers prove that the
    /// value is either nullish or another instance of the same recursive shape.
    /// These slots can carry the exact generated CLR record type instead of object.
    /// </summary>
    internal HashSet<(string Fingerprint, int Index)> CompactObjectRecordSelfFields { get; } = [];

    internal HashSet<string> PotentiallyMaterializedCompactObjectRecordShapes { get; } = [];
    internal bool PotentiallyMaterializesUnknownCompactObjectRecordShape { get; set; } = true;

    internal bool HasUniqueCompactObjectRecordShape(int arity, string fingerprint) =>
        CompactObjectRecordShapeFingerprints.TryGetValue(arity, out var shapes) &&
        shapes.Count == 1 && shapes.Contains(fingerprint);

    internal bool CanAssumeCompactObjectRecordIsUnmaterialized(string fingerprint) =>
        !PotentiallyMaterializesUnknownCompactObjectRecordShape &&
        !PotentiallyMaterializedCompactObjectRecordShapes.Contains(fingerprint);

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
    public bool UsesCompactObjectRecords { get; set; } = true; // compact slot-backed ordinary object literals
    public bool UsesIntl { get; set; } = true;              // Intl.NumberFormat, DateTimeFormat, Collator
    public bool UsesReflect { get; set; } = true;           // Reflect.set/get/deleteProperty/has/etc.
    public bool UsesIteratorHelpers { get; set; } = true;   // Iterator.prototype.map/filter/flatMap/take/drop
    public bool UsesPromise { get; set; } = true;           // Promise references, async/await, and Promise-returning host/module surfaces
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

    // ── Semantic optimization guards ────────────────────────────────────────────────
    // Object/Reflect descriptor APIs can install indexed accessors on arrays.
    // Such programs must not use backing-list-only indexed-read fast paths.
    public bool UsesDynamicPropertyDescriptors { get; set; } = true;
    // Object.freeze/seal/preventExtensions (or an opaque route to those intrinsics)
    // can populate the emitted runtime's object-integrity tables. Direct typed
    // class setters may omit that table probe only while this is false.
    public bool UsesObjectIntegrityMutation { get; set; } = true;
    // Any observable access to a class prototype (or an opaque Object/eval/
    // Function route checked alongside this flag) can replace a method binding.
    // Exact-instance typed companion calls require this to remain false.
    public bool UsesClassPrototypeMutation { get; set; } = true;
    // Date method binding mutation (prototype aliases, own overrides/accessors,
    // prototype-chain mutation, or opaque evaluation) makes statically typed Date
    // calls observable, so the direct DateEmitter fast path is unsafe. The historical
    // property name is retained because it is part of the emitted-runtime feature API.
    public bool UsesDatePrototypeMutation { get; set; } = true;
    // Promise method/constructor/species mutation makes direct then lowering
    // observable through ordinary property lookup. Such programs must retain
    // value dispatch for then/catch/finally calls.
    public bool UsesPromisePrototypeMutation { get; set; } = true;
    // Any observable access to Array.prototype, push-binding mutation,
    // prototype-chain mutation API, or __proto__ access makes a backing-list-only
    // array append unsafe. The discarded-result push fast path requires this false.
    public bool UsesArrayPrototypeMutation { get; set; } = true;
    // Number prototype mutation makes typed number instance methods observable
    // through ordinary property lookup. Direct formatting requires this false.
    public bool UsesNumberPrototypeMutation { get; set; } = true;
    // String prototype/constructor mutation makes primitive string method
    // bindings observable through ordinary property lookup. Fixed-arity typed
    // intrinsics require this to remain false.
    public bool UsesStringPrototypeMutation { get; set; } = true;
    // Replacing the global Math object or one of its function properties makes
    // direct Math.min/max interception observable. Typed folds require this false.
    public bool UsesMathMutation { get; set; } = true;
    // Replacing Number or mutating its static properties requires live lookup of
    // Number.parseInt and the other constructor-owned built-ins.
    public bool UsesNumberConstructorMutation { get; set; } = true;
    // Any observable access to RegExp.prototype, replacement of the global
    // constructor, dynamic evaluation, or opaque descriptor/prototype mutation
    // can replace the intrinsic test/exec bindings. The allocation-free literal
    // test path requires this to remain false.
    public bool UsesRegExpPrototypeMutation { get; set; } = true;
    // A write to the global parseInt binding (or opaque eval) requires live
    // globalThis lookup instead of the direct typed parsing intrinsic.
    public bool UsesGlobalParseIntMutation { get; set; } = true;

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
