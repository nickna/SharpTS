# Runtime tree-shaking for compiled DLLs — implementation plan

**Goal:** shrink the per-test compiled DLL by gating emission of helper runtime
types on whether the user's source actually needs them. The same mechanism makes
production-shipped DLLs smaller.

**Status:** Phases 1–5a landed (see `archive/runtime-tree-shaking-phase1-results.md`,
`runtime-tree-shaking-phase5.md`, and `archive/runtime-tree-shaking-phase5-audit.md`;
cumulative DLL ~387KB → ~159.5KB). Phase 5b+ remains open.

## What's there now

- `RuntimeEmitter.EmitAll(moduleBuilder)` runs ~94 emit calls in a fixed order
  and produces **190 types** for every compiled assembly — even
  `console.log(1);`.
- That fixed emission costs **387 KB**, **~1500 methods**, and **~140 ms of
  `Assembly.Load`** per test. `Assembly.Load` is 73% of every compiled-mode
  test's wall time after warmup.
- A measured per-phase breakdown (50-iteration probe, post warm-up, on a 5-line
  test):

  | Phase | Avg ms | % of Total |
  |---|---:|---:|
  | Lex / Parse / TypeCheck / DeadCode | 0.33 | 0.2% |
  | Compile (IL emit) | 28.93 | 15.1% |
  | SaveToBytes | 15.48 | 8.1% |
  | **Assembly.Load** | **140.36** | **73.2%** |
  | InvokeMain | 6.71 | 3.5% |

- Per-type IL footprint is heavily skewed:

  | Type | Methods | IL bytes |
  |---|---:|---:|
  | `$Runtime` | 1122 | 130,949 |
  | `$Buffer` | 59 | 6,147 |
  | `$DataView` | 23 | 3,469 |
  | `$NetSocket` | 12 | 2,568 |
  | `$NetServer` | 9 | 2,403 |
  | `$DatagramSocket` | 24 | 2,402 |
  | … (top 30) | | |
  | Remaining ~160 types combined | | ~80 KB |

  `$Runtime` alone is **33% of the entire DLL**. Without touching it, the upper
  bound on size savings from gating other types is ~67%.

## Strategy

Stake-driven, measure-first rollout in four phases:

- **Phase 1**: gate the obviously rare top-level helper types (network, fs,
  crypto, streams, typed arrays beyond `Uint8Array`, etc.). Conservative: when
  in doubt, emit. Land detection visitor + emit-gate plumbing. Measure.
- **Phase 2**: gate moderately common types (`$Promise` + state machines,
  `$RegExp`, `$TSDate`, `$Map`/`$Set` bound-method types).
- **Phase 3**: tree-shake `$Runtime` itself — split the 1122 methods into
  feature-gated buckets (e.g., FS helpers, crypto helpers, JSON helpers, async
  helpers). This is where the dramatic size win comes from.
- **Phase 4**: optional — collectible AssemblyLoadContext for tests, so each
  test's loaded assembly unloads after the test instead of accumulating to ~2 GB.

This document covers **Phase 1** in detail. Phases 2–4 are sketched at the end
and become concrete once Phase 1 has measured results.

## Architecture

### Detection — `RuntimeFeatureSet`

A new `Compilation/RuntimeFeatureSet.cs` holds `bool` flags per feature:

```csharp
public sealed class RuntimeFeatureSet
{
    public bool UsesNet { get; set; }
    public bool UsesHttp { get; set; }
    public bool UsesTls { get; set; }
    public bool UsesDgram { get; set; }
    public bool UsesDns { get; set; }
    public bool UsesFs { get; set; }
    public bool UsesCrypto { get; set; }
    public bool UsesNodeStreams { get; set; }    // require('stream')
    public bool UsesWebStreams { get; set; }     // new ReadableStream()
    public bool UsesZlib { get; set; }
    public bool UsesCluster { get; set; }
    public bool UsesBroadcastChannel { get; set; }
    public bool UsesAsyncLocalStorage { get; set; }
    public bool UsesReadline { get; set; }
    public bool UsesUtilPromisify { get; set; }
    public bool UsesTextEncoding { get; set; }   // TextEncoder/TextDecoder
    public bool UsesFinalizationRegistry { get; set; }
    public bool UsesFetch { get; set; }
    public bool UsesIntervalAsync { get; set; }  // setIntervalAsync
    public bool UsesReflectMetadata { get; set; }
    public bool UsesCjsRequire { get; set; }     // require()/module.exports
    public TypedArrayKinds TypedArrays { get; set; }   // bit-flags

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
        ArrayBuffer = 1 << 11,    // bare new ArrayBuffer / DataView consumers
        SharedArrayBuffer = 1 << 12,
        DataView = 1 << 13,
    }
}
```

The set defaults to all-`true` ("emit everything") so the existing behavior is
preserved. The detection visitor flips flags to `false` only when it's *certain*
a feature isn't used. **Bias toward over-emitting.** A missed gate at worst
inflates the DLL; a false-negative on `false` would TypeLoadException at runtime.

### Detection visitor

`Compilation/RuntimeFeatureDetector.cs` runs after `TypeChecker.Check` and walks
the typed AST + the `TypeMap`. Triggers per feature:

| Feature | Triggers |
|---|---|
| `UsesNet` | `import 'net'`, `require('net')`, identifier `net` referenced as a value |
| `UsesHttp` | `import 'http'`/`'https'`, `fetch(` with mode that hits HttpServer (server-side fetch — keep conservative) |
| `UsesTls` | `import 'tls'`, identifier `TLSSocket` or `tls` |
| `UsesDgram` | `import 'dgram'`, identifier `dgram` |
| `UsesDns` | `import 'dns'`/`'dns/promises'`, identifier `dns` / `dnsP` |
| `UsesFs` | `import 'fs'`/`'fs/promises'`, identifier `fs` / `fsp` |
| `UsesCrypto` | `import 'crypto'`/`'crypto/promises'`, identifier `crypto` |
| `UsesNodeStreams` | `import 'stream'`/`'stream/promises'`, identifiers `Readable`/`Writable`/`Duplex`/`Transform`/`PassThrough` |
| `UsesWebStreams` | `new ReadableStream`/`WritableStream`/`TransformStream`, `pipeTo`/`pipeThrough` |
| `UsesZlib` | `import 'zlib'`, identifier `zlib` |
| `UsesCluster` | `import 'cluster'`, identifier `cluster` |
| `UsesBroadcastChannel` | `new BroadcastChannel(`, identifier `BroadcastChannel` |
| `UsesAsyncLocalStorage` | `new AsyncLocalStorage(`, `AsyncLocalStorage` identifier |
| `UsesReadline` | `import 'readline'`, identifier `readline` |
| `UsesUtilPromisify` | `import 'util'`, `util.promisify`/`util.callbackify`/`util.deprecate` |
| `UsesTextEncoding` | `new TextEncoder(`, `new TextDecoder(`, `TextEncoder`/`TextDecoder` identifier |
| `UsesFinalizationRegistry` | `new FinalizationRegistry(`, `FinalizationRegistry` identifier |
| `UsesFetch` | `fetch(` call site, identifier `Headers`/`Request`/`Response` |
| `UsesIntervalAsync` | `setInterval(` with `await` body indicator (heuristic — keep conservative) |
| `UsesReflectMetadata` | `Reflect.metadata`/`Reflect.defineMetadata`, decorator with metadata factory |
| `UsesCjsRequire` | `require(`, `module.exports`, `exports.X` |
| `TypedArrays.X` | `new Int8Array(`/etc, `Int8Array` identifier |

**Conservative heuristic for unknown identifiers**: when the type checker
reports `any` for a member access on a possibly-built-in name, treat as
"feature in use" (emit). Better to over-emit a rare type than to miss it.

**Module-import detection**: parse the import path string. `import "fs"` /
`require("fs")` are easy. Re-exports through stdlib aliases need to be tracked
through the embedded stdlib's import graph.

### Emit gating

`RuntimeEmitter.EmitAll` accepts a new `RuntimeFeatureSet features` parameter.
Each gateable emit call is guarded:

```csharp
if (features.UsesNet)
{
    EmitTSNetSocketPhase1(moduleBuilder, runtime);
    EmitTSNetServerPhase1(moduleBuilder, runtime);
}

if (features.UsesCrypto)
{
    EmitTSHashClass(moduleBuilder, runtime);
    EmitTSHmacClass(moduleBuilder, runtime);
    EmitTSCipherClass(moduleBuilder, runtime);
    EmitTSDecipherClass(moduleBuilder, runtime);
    EmitTSSignTypeDefinition(moduleBuilder, runtime);
    EmitTSVerifyTypeDefinition(moduleBuilder, runtime);
    EmitTSKeyObjectClass(moduleBuilder, runtime);
    EmitTSECDHTypeDefinition(moduleBuilder, runtime);
    EmitBoundECDHMethodTypeDefinition(moduleBuilder, runtime);
    EmitTSDHTypeDefinition(moduleBuilder, runtime);
    EmitBoundDHMethodTypeDefinition(moduleBuilder, runtime);
}

// ...etc
```

Phase-2 finalize calls (e.g. `EmitTSSignFinalize`) sit under the same flag.

`ILCompiler.Compile` becomes:

```csharp
public void Compile(List<Stmt> statements, TypeMap typeMap, DeadCodeInfo? deadCodeInfo = null)
{
    var features = new RuntimeFeatureDetector().Detect(statements, typeMap);
    // ... existing pipeline, threading `features` into RuntimeEmitter ...
}
```

For now `RuntimeFeatureSet` lives alongside `EmittedRuntime` on the
`CompilationContext` so emit sites that need to ask "should I emit a Newobj
for `$NetSocket`?" can check `Ctx.Features.UsesNet`.

### Cross-references — the dependency graph

A gateable type can declare its prerequisites:

```csharp
private static readonly Dictionary<string, string[]> _typeDeps = new()
{
    ["$BroadcastChannel"] = ["$EventEmitter", "$EventLoop"],
    ["$ReadableStream"]   = ["$Promise", "$WritableStream"],
    ["$WritableStream"]   = ["$Promise"],
    ["$TransformStream"]  = ["$ReadableStream", "$WritableStream", "$TransformSinkHolder"],
    ["$HttpServer"]       = ["$EventEmitter", "$NetServer"],
    ["$Headers"]          = [], // used by HttpRequest/Response and fetch
    ["$NetServer"]        = ["$EventEmitter", "$NetSocket"],
    ["$TlsServer"]        = ["$EventEmitter", "$NetServer"],
    ["$Int16Array"]       = ["$TypedArray", "$ArrayBuffer"],
    // ... etc
};
```

After detection produces a feature set, expand to a transitive type set:

```csharp
var toEmit = new HashSet<string>();
foreach (var f in features.RequiredTypes) AddWithDeps(f);

void AddWithDeps(string typeName)
{
    if (!toEmit.Add(typeName)) return;
    if (_typeDeps.TryGetValue(typeName, out var deps))
        foreach (var d in deps) AddWithDeps(d);
}
```

This is computed once at compile start. Emit sites then consult `toEmit`.

### Production vs test mode — same mechanism

Tree-shaking benefits both production and test builds; there is no separate
"test mode." The compiler simply always feeds the AST through the detector and
emits only what the AST needs. Production gets smaller DLLs as a bonus; tests
get faster `Assembly.Load`.

A `--no-tree-shake` CLI flag and an env var (`SHARPTS_TREE_SHAKE_OFF=1`) bypass
the detector and emit everything — useful for diagnosing whether a runtime
failure is a missed dependency vs a real bug.

## Phase 1 — concrete type list

I categorize the 190 emitted types into three tiers. Phase 1 gates Tier A only.

### Tier A — Phase 1 gateable (90+ types)

All emitted types listed below are skipped when the corresponding feature flag is `false`.

**`UsesCrypto = false`** (skip 11 types):
- `$Hash`, `$Hmac`, `$Cipher`, `$Decipher`, `$Sign`, `$Verify`,
  `$DiffieHellman`, `$ECDH`, `$TSKeyObject`, `$BoundDHMethod`, `$BoundECDHMethod`

**`UsesNet = false`** (skip 11 types):
- `$NetSocket`, `$NetServer`, `$TcpAcceptClosure`, `$IpcAcceptClosure`,
  `$IpcWriteClosure`, `$SocketConnectErrClosure`, `$SocketConnectOkClosure`,
  `$SocketReadDataClosure`, `$SocketReadEndClosure`,
  `$DatagramSocket`, `$DgramMessageClosure`

**`UsesHttp = false`** (skip 4 types):
- `$HttpServer`, `$HttpRequest`, `$HttpResponse`, `$HttpAcceptClosure`

**`UsesFetch = false`** (skip 5 types):
- `$Headers`, `$Request`, `$Response`, `$FetchResponse`, `$FetchDisplayClass`

**`UsesTls = false`** (skip 4 types):
- `$TlsServer`, `$TlsSocket`, `$TlsAcceptClosure`, `$TlsConnectClosure`

**`UsesDns = false`** (skip ~12 closures):
- `$DnsAsyncClosure_resolveCaa/Cname/Mx/Naptr/Ns/Ptr/Soa/Srv/Txt`,
  `$DnsDisplay1`, `$DnsDisplay2`

**`UsesFs = false`** (skip 10 types):
- `$FsReadStream`, `$FsWriteStream`, `$FsWatcher`, `$FsWatchChangeClosure`,
  `$StatWatcher`, `$StatWatchPollClosure`, `$Dir`, `$Dirent`,
  `$FileDescriptorTable`, `$Stats`

**`UsesNodeStreams = false`** (skip ~10 types):
- `$Readable`, `$Writable`, `$Duplex`, `$Transform`, `$PassThrough`,
  `$StreamUtils`, `$StreamFinishedCleanup`, `$WriteCallbackWrapper`,
  `$MapTransformCallback`, `$FilterTransformCallback`

**`UsesWebStreams = false`** (skip ~10 types):
- `$ReadableStream`, `$ReadableStreamDefaultController`,
  `$ReadableStreamDefaultReader`, `$WritableStream`,
  `$WritableStreamDefaultController`, `$WritableStreamDefaultWriter`,
  `$TransformStream`, `$TransformSinkHolder`, `$TransformDoneCallback`,
  `$CountQueuingStrategy`, `$ByteLengthQueuingStrategy`

**`UsesZlib = false`** (skip 1 type):
- `$ZlibTransform`

**`UsesCluster = false`** (skip 3 types):
- `$ClusterContext`, `$ClusterManager`, `$ClusterWorker`

**`UsesBroadcastChannel = false`** (skip 1 type):
- `$BroadcastChannel`

**`UsesAsyncLocalStorage = false`** (skip 1 type):
- `$AsyncLocalStorage`

**`UsesReadline = false`** (skip 1 type):
- `$ReadlineInterface`

**`UsesUtilPromisify = false`** (skip 4 types):
- `$DeprecatedFunction`, `$CallbackifiedFunction`, `$PromisifiedFunction`,
  `$PromisifyCallback`

**`UsesTextEncoding = false`** (skip 3 types):
- `$TextEncoder`, `$TextDecoder`, `$TextDecoderDecodeMethod`

**`UsesFinalizationRegistry = false`** (skip 1 type):
- `$FinRegEntry`

**`UsesReflectMetadata = false`** (skip 1 type):
- `$ReflectMetadataDecorator`

**`UsesCjsRequire = false`** (skip 1 type):
- `$CJSModule`

**`TypedArrays.X = 0` for individual kinds** (skip up to 14 types):
- 11 specific typed-array types: `$Int8Array`, `$Int16Array`, `$Int32Array`,
  `$Uint8Array`, `$Uint8ClampedArray`, `$Uint16Array`, `$Uint32Array`,
  `$Float32Array`, `$Float64Array`, `$BigInt64Array`, `$BigUint64Array`
- Plus the bases: `$TypedArray`, `$ArrayBuffer`, `$SharedArrayBuffer`, `$DataView`
- Each gated individually so a test using only `Float32Array` doesn't drag in
  `$BigInt64Array`.

**Estimated Phase 1 type-count cut**: 70–90 types out of 190, on a typical
"console.log a few values" test. Estimated DLL size cut: 50–80 KB
(13–20%) of the 387 KB baseline.

### Tier B — Phase 2 (deferred)

These need more careful handling because their features pull in `$Runtime`
methods too:

- `$Promise` + 7 promise state machines (`$Promise*_SM`)
- `$RegExp`
- `$TSDate`
- `$BoundMapMethod`, `$BoundSetMethod` (gate on `Map`/`Set` use)
- Iterator helper closures (`$MapIterator`, `$FilterIterator`,
  `$FlatMapIterator`, `$TakeIterator`, `$DropIterator`)
- `$Generator`/`$AsyncGenerator` interfaces + `$AsyncGeneratorAwaitContinue_StateMachine`
- `$Buffer` (used internally by crypto/fs; gateable when those are also gated)

### Tier C — always emit (~30 types)

- `$Runtime`, `$Program`, `$Undefined`
- `$TSFunction`, `$BoundTSFunction`, `$BoundAnyFunction`,
  `$FunctionBindWrapper`, `$FunctionCallWrapper`, `$FunctionApplyWrapper`
- `$Object`, `$IHasFields`, `$IUnionType`
- `$Error`, `$TypeError`, `$RangeError`, `$ReferenceError`, `$SyntaxError`,
  `$URIError`, `$EvalError`, `$AggregateError`
- `$Array`, `$ArrayHole`, `$BoundArrayMethod`
- `$TSSymbol`, `$TSNamespace`
- `$ReferenceEqualityComparer`, `$CallArgsPool`, `$ArgumentsContext`,
  `$Arguments`
- `$EventLoop`, `$VirtualTimer`, `$TSTimeout`, `$TimeoutClosure`,
  `$IntervalClosure`, `$AsyncIntervalClosure` (timers — emit if any async or
  setTimeout)
- `$EventEmitter` (used by lots of types as a base)
- `$PropertyDescriptorStore`, `$CompiledPropertyDescriptor`
- `$NodeError`
- `$IteratorWrapper`
- `$MethodCallable`
- `$TemplateStringsList`
- `$AnyState`, `$FrozenSealedState`, `$PrototypeInfo`, `$ListenerWrapper`

These ~30 are the "always emit" floor. Most have small IL footprints so the
forced minimum DLL is around 200 KB.

## Phase 1 implementation steps

1. **Add `RuntimeFeatureSet`** (`Compilation/RuntimeFeatureSet.cs`). Default
   constructor flips every flag to `true` so the existing emit behavior is
   preserved when `--no-tree-shake` is in effect or the detector hasn't run.

2. **Add `RuntimeFeatureDetector`** (`Compilation/RuntimeFeatureDetector.cs`).
   Visitor over typed AST. Returns a `RuntimeFeatureSet` with flags set
   according to the trigger table. Defaults each flag to `false` and flips to
   `true` on detection — i.e., "innocent until proven guilty" but only because
   the visitor itself is conservative.

3. **Wire into `ILCompiler.Compile`**: detect features, store the set on the
   `CompilationContext`, pass to `RuntimeEmitter.EmitAll`.

4. **Gate `EmitAll` calls**: each entry in the categorized list above gets a
   `if (features.X)` wrapper. Phase-2 finalize calls
   (`EmitTSSignFinalize`, etc.) gated under the same flag.

5. **Type-dependency closure**: optional for Phase 1 since each feature's
   gate already pulls all its types together. Add the explicit graph if the
   feature flags can't cleanly express prerequisites.

6. **Validation pass**: run the full SharpTS.Tests suite. Any
   `TypeLoadException` or `MissingMethodException` indicates a missed gate
   trigger — add the trigger and rerun.

7. **Standalone-DLL coverage**: `StandaloneDllTests` already exercises the
   "shipped DLL without SharpTS.dll" path. Verify those tests pass with tree-
   shaking on (i.e., the gates correctly skip emission AND nothing in the
   shipped DLL references a now-missing type).

8. **Test262 baseline**: regenerate the compiled-mode baseline. Tree-shaking
   should not change Pass/Fail counts. If any test flips, the gate is wrong.

9. **Measure**: rerun the per-phase probe with tree-shaking on. Target metrics:
   - `Assembly.Load` ms drops 25–40% on the trivial-test sample
   - DLL size drops 15–25% on the trivial-test sample
   - Full suite wall time drops 15–25%

10. **Add CLI flag + env var**: `--no-tree-shake` on `dotnet run --` for
    debugging, `SHARPTS_TREE_SHAKE_OFF=1` for env override. Defaults: on.

## Risks and mitigations

- **Missed dependency.** A type used at runtime via reflection or via a path
  the visitor missed produces `TypeLoadException` at first use.
  - *Mitigation:* fail loud (don't catch), keep gates conservative, run full
    suite + Test262 + standalone tests as the gate.
- **`globalThis`/`global` hijinks.** Tests that do
  `globalThis.Map = somethingElse` or read `globalThis.AsyncLocalStorage`
  could surface types that the visitor doesn't see.
  - *Mitigation:* any reference to `globalThis` or `global` (as identifier)
    forces the conservative path — emit `Tier B` (common runtime types).
- **Embedded stdlib re-exports.** SharpTS ships embedded stdlib modules; some
  re-export others. The visitor must trace through the import graph.
  - *Mitigation:* in Phase 1, treat any user-visible `import` of a stdlib
    module name as a feature trigger. Don't bother walking the stdlib's
    own imports — the stdlib is part of compilation input, the visitor
    sees its source directly.
- **Type identity in cross-assembly references.** Compiled DLLs with
  tree-shaking still need to interop with `SharpTS.Runtime.Types.*` for any
  reflection-based fallback path.
  - *Mitigation:* the existing reflection-emission pattern (`Type.GetType("..., SharpTS")`)
    already names types by string and falls through gracefully when the
    type isn't in the user assembly. Tree-shaking only changes which types
    are *emitted in the user assembly*; nothing changes about how the
    assembly references SharpTS.dll types.
- **Test262 / standalone DLL regression.** A Test262 test that uses
  `BroadcastChannel` and a Phase 1 gate that skips it would regress.
  - *Mitigation:* the gate trigger should include any AST mention. If
    Test262's source contains the identifier, the gate fires. Re-run Test262
    baseline as part of validation.

## Expected impact

For a trivial `console.log(1);` test:

| Metric | Before | Phase 1 | Phase 1+2 | Phase 1+2+3 |
|---|---:|---:|---:|---:|
| Types emitted | 190 | ~110 | ~70 | ~50 |
| DLL size | 387 KB | ~310 KB | ~250 KB | ~80 KB |
| `Assembly.Load` | 140 ms | ~100 ms | ~75 ms | ~30 ms |
| Per-test total | 192 ms | ~150 ms | ~120 ms | ~60 ms |

For the full suite (10,320 tests, currently 19m44s):

- Phase 1 alone: ~16–17 min (–15–20%). Modest. The integration-test floor
  (44 tests at 10–17s each, 14 min wall on 12 cores) limits how far a
  per-test optimization can go.
- Phase 1+2: ~13–14 min.
- Phase 1+2+3 ($Runtime tree-shaking): ~7–8 min. Big jump.
- All phases + integration-test triage (move I/O tests to a `slow`
  category): inner-loop runs in ~3–5 min.

**The biggest single win is `$Runtime` tree-shaking (Phase 3).** Phase 1 is
the foundation that proves the gating mechanism works on a low-risk scope
before applying it to the 1122-method behemoth.

## Phase 2/3/4 sketch

- **Phase 2** repeats the Phase 1 pattern for `$Promise` (gate on `async`/
  `await`/`Promise` use), `$RegExp` (gate on regex literal/`new RegExp`),
  `$TSDate` (gate on `Date` use), `$BoundMapMethod`/`$BoundSetMethod` (gate
  on `Map`/`Set` use), iterator helper closures, async-generator state machine.
- **Phase 3** is the `$Runtime` overhaul:
  - Either split `$Runtime` into multiple classes (`$RuntimeCore`,
    `$RuntimeFs`, `$RuntimeCrypto`, `$RuntimeJson`, …) with method bodies
    relocated according to feature.
  - Or keep `$Runtime` as one class but per-method-gate emission inside
    `EmitRuntimeClass`. Risk: the cctor + EmitAll has ~50 inter-method
    dependencies; need a closure tracker.
  - Pick after Phase 1 measurements determine whether the savings justify
    the disruption.
- **Phase 4** is `CollectibleAssemblyLoadContext` per-test. Independent of
  tree-shaking — it bounds memory growth instead of speeding individual
  tests. Useful especially if Phase 3 doesn't ship and the suite continues
  to load 5000+ assemblies into the testhost.

## Files to add / change for Phase 1

New:
- `Compilation/RuntimeFeatureSet.cs` (~80 lines)
- `Compilation/RuntimeFeatureDetector.cs` (~250 lines, visitor)

Modified:
- `Compilation/RuntimeEmitter.cs` — `EmitAll` takes `RuntimeFeatureSet`,
  ~30 emit calls become conditional
- `Compilation/ILCompiler.cs` — invoke detector, store result on context
- `Compilation/CompilationContext.cs` (or wherever `Ctx` is) — add
  `RuntimeFeatureSet Features` property
- `Cli/CompileCommand.cs` (or equivalent) — add `--no-tree-shake` flag
- A handful of emit-site files where construction of a now-gated type might
  be referenced from outside its emit guard (rare; should be self-contained
  per category)

Test changes: ideally none. Tree-shaking is meant to be invisible.
