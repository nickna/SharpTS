# Runtime tree-shaking — Phase 1 results

**Status:** infrastructure landed; six categories gated; full SharedTests pass.
**Parent plan:** [runtime-tree-shaking.md](runtime-tree-shaking.md)

## What landed

New files:
- `Compilation/RuntimeFeatureSet.cs` — feature flags + `TypedArrayKinds` bitset
- `Compilation/RuntimeFeatureDetector.cs` — typed-AST visitor that flips flags

Modified:
- `Compilation/ILCompiler.cs` — `_features` field, `SetRuntimeFeatures` API,
  `Compile`/`CompileModules` invoke the detector
- `Compilation/RuntimeEmitter.cs` — `EmitAll(ModuleBuilder, RuntimeFeatureSet)`
  overload; `_features` private field; gates on six categories
- `Compilation/RuntimeEmitter.RuntimeClass.cs` — `EmitClusterHelpers` gate
- `Compilation/RuntimeEmitter.FinalizationRegistry.cs` —
  `EmitFinalizationRegistryMethods` early-return when `!UsesFinalizationRegistry`
- `Compilation/RuntimeEmitter.ReadlineHelpers.cs` —
  `EmitReadlineMethods` early-return when `!UsesReadline`

## Six categories gated

| Feature flag | Type(s) gated | $Runtime methods gated |
|---|---|---|
| `UsesAsyncLocalStorage` | `$AsyncLocalStorage` | (none — methods stay on the type) |
| `UsesBroadcastChannel` | `$BroadcastChannel` | (none) |
| `UsesCluster` | `$ClusterContext`, `$ClusterManager`, `$ClusterWorker` | `EmitClusterHelpers` |
| `UsesFinalizationRegistry` | `$FinRegEntry` | `EmitFinalizationRegistryMethods` (Register/Unregister/CreateFinalizationRegistry) |
| `UsesReadline` | `$ReadlineInterface` | `EmitReadlineMethods` (QuestionSync/CreateInterface) |
| `UsesReflectMetadata` | `$ReflectMetadataDecorator` | (external call site has null-check fallback) |

## Measured impact (`/tmp/bench-perf-probe`, 50 iters of `console.log(1)`)

| Metric | Before | After Phase 1 | Δ |
|---|---:|---:|---:|
| Types emitted | 190 | 182 | −8 (−4.2%) |
| DLL size | 387 KB | 380 KB | −2.0% |
| `Assembly.Load` | 140 ms | 113 ms | **−19.4%** |
| Per-test total | 192 ms | 162 ms | **−15.4%** |

`Assembly.Load` dropping more than DLL size suggests the gated cctors
(`$BroadcastChannel` extends `$EventEmitter` and runs init code; the
`$Runtime.cctor` skips `_finRegPokeTable` setup when finalization-registry is
gated) carry more JIT-prep weight than their byte count alone.

## What's NOT in Phase 1 (and why)

The plan listed many more gates. Each one I tried beyond the six above hit a
blocker: a centralized $Runtime method emits `Isinst runtime.XType` or
`Newobj runtime.XCtor` unconditionally, and it lives outside the gated emit
file. Skipping the type leaves a null token, and IL emission throws
`ArgumentNullException` mid-compile.

Specifically:

- **`UsesUtilPromisify`** ($DeprecatedFunction / $CallbackifiedFunction /
  $PromisifiedFunction): referenced by `EmitInvokeValue` (5 dispatch arms in
  `RuntimeEmitter.Objects.Invocation.cs`) and `EmitTypeOf` (2 arms in
  `RuntimeEmitter.CoreUtilities.cs`).
- **`UsesTextEncoding`** ($TextEncoder / $TextDecoder / $TextDecoderDecodeMethod):
  $TextDecoderDecodeMethod is in `EmitInvokeValue`; $TextEncoder/$TextDecoder
  in `GlobalThisStaticEmitter` (which uses the runtime field
  unconditionally for `globalThis.TextEncoder`).
- **`UsesWebStreams`** ($ReadableStream / $WritableStream / $TransformStream):
  referenced from `EmitGlobalThisMethods` for `globalThis.ReadableStream` etc.
- **`UsesHttp` / `UsesFetch`** ($HttpServer / $Headers / fetch helpers):
  $Runtime emits the `fetch` getter unconditionally in `EmitGlobalThisMethods`,
  which references `runtime.Fetch` (the method built by `EmitHttpModuleMethods`).
- **`UsesNet` / `UsesTls` / `UsesDgram` / `UsesDns`**: Phase 1b/2 finalize
  steps unconditionally call back into the type builders.
- **`UsesCrypto`**: similar — $Runtime.crypto namespace getter references
  `$Hash`, `$Hmac`, etc.
- **`UsesZlib`** ($ZlibTransform): referenced from $Runtime's zlib module
  methods which are emitted unconditionally.
- **`UsesNodeStreams`** ($Readable / $Writable / $Duplex / $Transform):
  central `EmitInvokeValue` dispatch references `$TransformDoneCallback`,
  `$WriteCallbackWrapper`, etc.
- **`UsesFs`** ($Stats / $Dir / $Dirent / $FileDescriptorTable): $Stats is
  in the central GetFieldsProperty Isinst dispatch (for `fs.statSync(...).isFile()`
  duck typing).
- **`UsesCjsRequire`** ($CJSModule): central dispatch checks for
  `runtime.CjsModuleType` in three places in Properties.cs.
- **TypedArrays**: $ArrayBuffer / $SharedArrayBuffer / $DataView are in
  central GetFieldsProperty Isinst dispatch.

These are all addressable with **conditional dispatch emission**: where a
central $Runtime method does an Isinst check or unconditional call into a
gateable type, wrap that emission in `if (_features.UsesX) { ... }`. Ship as
Phase 2.

## Phase 2 sketch (next session)

For each remaining category in priority order (biggest size impact first):

1. **HTTP / Fetch** — biggest IL cluster outside crypto/fs. Steps:
   - Re-add `if (features.UsesHttp) EmitHttpTypes(...)` in `EmitAll`
   - Re-add `if (_features.UsesHttp) EmitHttpModuleMethods(...)` in `EmitRuntimeClass`
   - In `EmitGlobalThisMethods` (RuntimeEmitter.GlobalThis.cs), gate the
     `fetch` / `Headers` / `Request` / `Response` getter arms on `UsesFetch`
     and `UsesHttp`.
   - In `ExpressionEmitterBase.Constructors.cs`, the `case "Headers": ...`
     etc. branches won't fire when the user doesn't have `new Headers(...)` —
     no change needed.
2. **Node Streams** ($Readable etc.) — gate `EmitInvokeValue` and `EmitTypeOf`
   dispatch arms on `UsesNodeStreams`.
3. **Util.promisify family** — gate the same dispatch arms on `UsesUtilPromisify`.
4. **TextEncoding** — same pattern.
5. **Web Streams** — same.
6. **Crypto** — same; the $Runtime.crypto namespace getter is a single
   `EmitGlobalThisMethods` arm.
7. **Net / TLS / DNS / DGRAM / Cluster (already gated)** — net/tls extend the
   same dispatch story; gate alongside.
8. **Zlib** — small, independent module.
9. **FS + $Stats / $CJSModule / TypedArrays** — these need conditional Isinst
   in the GetFieldsProperty central dispatch. More invasive.

Most of these reach the central dispatch sites in just two or three files:
`RuntimeEmitter.GlobalThis.cs`, `RuntimeEmitter.Objects.Invocation.cs`,
`RuntimeEmitter.Objects.Properties.cs`, `RuntimeEmitter.CoreUtilities.cs`.
Phase 2's first task is to enumerate every `runtime.X` reference in those
files and tag each with the feature it belongs to.

Estimated Phase 2 impact (conservative): another 20–30% Assembly.Load
reduction on the trivial-test sample, taking per-test from 162ms → ~115ms.
Combined with Phase 1: 40% total reduction (192ms → 115ms).

## What this means for test perf

The 6 categories landed in Phase 1 don't intersect with most test code
(very few tests use BroadcastChannel, AsyncLocalStorage, cluster, etc.), so
the savings should reach almost every compiled-mode test. The
trivial-sample 19% Assembly.Load drop translates roughly to:

- Full suite (10,320 tests, currently 19m44s): saves ~3 min off compiled-test
  loading. Realistic full-suite time after Phase 1: ~16–17 min.
- The integration-test floor (~14 min wall on the 44 slow I/O tests) still
  dominates. Reducing that further is a separate problem — Phase 2 gates
  won't help there.

For inner-loop iteration on compiled-mode unit tests (filter to
`mode: Compiled`, exclude integration), Phase 1 should be visible on the
clock immediately.
