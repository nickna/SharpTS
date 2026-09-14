# SharpTS architecture

SharpTS is one TypeScript front end with two execution backends: a tree-walking interpreter and a
.NET IL compiler. This document records stable subsystem boundaries, data flow, and invariants.
Per-file catalogs and current feature coverage belong in source, tests, and [STATUS.md](STATUS.md).

## Repository layout

- `src/` contains shipping projects and the bounded `src/SharpTS/` core source tree.
- `tests/` contains the normal unit suite, opt-in external-corpus conformance harnesses,
  GUI conformance infrastructure, and packaging fixtures.
- `benchmarks/cross-runtime/` and `benchmarks/micro/` remain separate because they measure
  cross-engine workloads and BenchmarkDotNet microbenchmarks respectively.
- `samples/` contains runnable applications and scripts; `extensions/` contains editor integrations.
- Repository infrastructure stays at the root under `.github/`, `docs/`, `scripts/`, `eng/`,
  `distribution/`, and `external/`.

Shipping projects are grouped by role rather than flattened at the repository root. `src/SharpTS/`
owns the core front end, both execution engines, runtime, and `sharpts` CLI. `SharpTS.Hosting*`
owns hosted ABI and Native AOT integration; `SharpTS.Gui*` owns the desktop bridge, host, generator,
and SDK; `SharpTS.Sdk*` owns MSBuild integration. `SharpTS.LanguageServer` (`sharpts-lsp`) and
`SharpTS.DebugAdapter` (`sharpts-dap`) are separate tools that consume the core project without
making their protocols part of the core CLI.

## System data flow

```text
source/config/references
          |
          v
  lexer -> parser -> checked AST/program graph
                         |             |
                         v             v
                  interpreter      IL compiler
                         |             |
                         v             v
                  runtime values   PE/PDB + emitted runtime
                         \             /
                          observable JS behavior
```

Configuration and `sharpts.json` reference discovery happen before the graph is checked. Both
backends consume the same parsed nodes, `TypeInfo` model, module graph, and resolved external
assemblies. Project checking and declaration emission stop or branch from the shared front end;
they are not interpreter features.

## Stable subsystem boundaries

| Subsystem | Responsibility | Must not own |
| --- | --- | --- |
| `src/SharpTS/Parsing/` | Tokens, source locations, AST records, TypeScript/TSX grammar, syntactic lowering | Runtime values or CLR emission |
| `TypeSystem/` | `TypeInfo`, environments, checking, compatibility, narrowing, built-in module type surfaces | Executing guest effects |
| `Configuration/`, `Projects/` | `tsconfig` discovery/merge, roots, references, build/watch/incremental state | Backend semantics |
| `Modules/` | Module/package resolution, declarations, embedded stdlib provider chain, module graph | User-visible execution policy |
| `References/`, `Declaration/` | Managed reference resolution and .NET declaration/discovery surfaces | JavaScript object behavior |
| `Execution/` | Tree-walking statements/expressions, scopes, async/generator execution | Persisted IL metadata |
| `Runtime/` | Shared interpreter values, built-ins, event loop, host bridges, interop adapters | Front-end type authority |
| `Compilation/` | Typed AST analysis, IL/type emission, emitted runtime, PDBs, bundling requirements | Calling the interpreter as the compiled backend |
| `Cli/`, `Repl/`, `Packaging/` | User command orchestration, presentation, REPL, NuGet packaging | Core phase logic or process exits from library APIs |
| `Hosting/`, `SharpTS.Hosting*` | Versioned hosted ABI, Native AOT catalogs, host lifecycle | Undeclared open-world native reflection |
| `SharpTS.Gui*` | GUI bridge, generated descriptor contract, host, SDK, tests | A public raw-Avalonia/custom-provider API |
| `SharpTS.LanguageServer`, `SharpTS.DebugAdapter` | Standalone LSP and DAP protocol hosts over core services | Front-end or interpreter semantics |

Large subsystems use partial classes grouped by concern. A partial file is an organizational unit,
not a new architectural layer.

## Front-end invariants

### AST and traversal

AST nodes are immutable records. The supported node universe is explicitly cataloged for
reflection-free dispatch, which is required by Native AOT. Parser, checker, interpreter, and IL
emitter traversal use ordered type switches. Adding a node requires updating the catalog and every
applicable dispatch family; registry tests re-derive the node set and catch omissions.

The parser may perform syntax-directed lowering, but it must preserve source locations and enough
node identity for checking, debug information, feature detection, and diagnostics.

### Compile-time and runtime environments

`TypeEnvironment` contains static names and `TypeInfo`; `RuntimeEnvironment` contains executed
values and scope chains. They are intentionally separate. Runtime behavior must not depend on a
mutable checker environment, and the checker must not execute user code to learn a type.

Class assignment compatibility is structural like TypeScript except where private/protected
branding requires a nominal relationship. Inheritance lookup is nominal. The compatibility logic,
not CLR assignability, is authoritative for TypeScript checking.

### Diagnostics

Core phases return or collect structured diagnostics with source locations, SharpTS codes, and a
canonical TypeScript code where one applies. Libraries and embedding services do not write to the
console or terminate the process. CLI layers format diagnostics and choose exit codes.

## Module and standard-library architecture

The resolver builds a graph from source modules, `package.json` metadata, `tsconfig` paths, ambient
declarations, and external references. A provider chain resolves:

1. source files and npm packages;
2. embedded npm fallbacks such as the JSX runtime;
3. embedded TypeScript implementations under `src/SharpTS/stdlib/`;
4. internal `primitive:` host seams used only by the embedded standard library; and
5. C#/IL-backed built-in modules.

User code imports only public specifiers. `primitive:` modules are private implementation seams.
The user-facing declaration, interpreter export, and compiled emitter for a built-in must describe
the same surface. See [`src/SharpTS/stdlib/CONTRIBUTING.md`](src/SharpTS/stdlib/CONTRIBUTING.md).

## Interpreter architecture

The interpreter evaluates expressions to `RuntimeValue` and executes statements against a
`RuntimeEnvironment`. `RuntimeValue` is the primary discriminated value representation; conversion
at legacy/object and host boundaries must preserve JavaScript distinctions such as `undefined`,
`null`, number, string, symbol, bigint, and object identity.

### Abrupt completion and exceptions

There is no single "exception-based control flow" rule. The boundary is explicit:

- Statement execution returns `ExecutionResult` for normal completion, `return`, `break`,
  `continue`, and guest/translated `throw`. Blocks and loops propagate or consume that struct.
- `ThrowException` adapts a guest throw across ordinary .NET call, callback, built-in, promise, and
  interop boundaries that cannot return an `ExecutionResult`. Catch sites convert it back while
  preserving the guest value and origin.
- `YieldException` and `GeneratorReturnException` are deliberate suspension/unwind mechanisms for
  generator machinery where a simple statement result cannot cross the iterator boundary.
- Host failures are translated at defined seams. A host exception must not be mislabeled as a
  guest-thrown string or swallowed as normal completion.

This resolves the apparent contradiction: `ExecutionResult` is the normal statement protocol;
exceptions remain boundary/suspension adapters, not the representation of every return or loop
branch.

### Async and event-loop behavior

Promises, timers, I/O callbacks, and microtasks converge on the event-loop/runtime scheduling
contracts. Async state must preserve runtime context and unhandled-rejection lifecycle. Hosts can
provide dispatch/lifetime services, but backend-specific scheduling differences require an
explicit tested deviation.

### Interpreter debugging

Interpreter debugging is an optional cooperative control surface, not a third execution backend.
Core hooks in `Execution/Debugging/` observe TypeScript statement/callback boundaries, retain AST
and lexical-environment identity, and coordinate safe-point suspension across the main interpreter
and workers. With no controller attached, they must not change guest behavior.

`SharpTS.DebugAdapter` owns Debug Adapter Protocol framing, launch/session lifetime, thread and
handle mapping, read-only inspection, and client-facing errors. It drives the interpreter in
process because paused runtime objects must retain identity, but DAP transport types and protocol
state do not belong in the interpreter. Suspension is cooperative and all-stop: a managed/native
call can run until the next safe point, while protocol I/O, termination, and disconnect remain
responsive. See [Debugging interpreted TypeScript](docs/debugging-interpreter.md).

## IL compiler architecture

The compiler analyzes the checked program, defines the required CLR types/members, emits bodies,
finalizes metadata, and serializes PE/PDB output. Definition and body phases are separate because
closures, recursion, inheritance, modules, async state machines, and runtime helpers need stable
metadata handles before all bodies exist.

Key compiler analyses include module bindings, closure/capture shape, runtime feature detection,
typed/local representation opportunities, and hosted output. Optimizations may use static type
facts but must preserve JavaScript object identity, coercion, evaluation order, and exceptions.

### Runtime metadata components

Migrate `EmittedRuntime` metadata one feature family at a time. DNS is the first component:
`Dns` is null when `UsesDns` is false, and `RequireDns()` reports accidental use of a disabled
feature. `RuntimeEmitter.EmitAll` creates the component before emission and completes it before
returning the runtime. Completion validates all required handles and promise wrappers and rejects
subsequent writes. Consumers can read the component but cannot replace its handles or mutate its
wrapper registry.

Zlib follows the same contract through `Zlib` / `RequireZlib()`. `EmitAll` starts the component
only when `UsesZlib` is enabled, before the transform constructor is emitted. `EmitZlibMethods`
then emits compression and streaming handles in dependency order and registers named-import
wrappers. `EmitAll` completes the component after type finalization. The 24 handles live only in
`EmittedZlibRuntime`; module consumers use that component. Buffer and base stream types remain
separate dependencies, and zlib retains
its standalone output behavior.

TLS uses `Tls` / `RequireTls()` for its eleven migrated module and socket handles.
`EmitAll` creates it only for `UsesTls`, before socket/server phase 1. The declared
`Connect` handle is also the source used to emit its deferred body after closure creation.
Completion follows socket, runtime, and server finalization; net types remain separate
dependencies. TLS-only constant and certificate emitters accept `EmittedTlsRuntime` directly.
The shared built-in module registry still owns named-import dispatch (including
`checkServerIdentity`); emitter-local closure and field state stays with the emitter.

Dgram uses `Dgram` / `RequireDgram()` only when `UsesDgram` is enabled. Its component owns the
socket type and constructor, module factory, forward-declared receive worker, and message closure
constructor/entry point. Phase 1 declares the receive worker before `bind` references it; phase 2
emits its body after the message closure is available and then finalizes the socket type.
Completion follows runtime finalization. The factory and socket finalizer take `EmittedDgramRuntime`
directly; event-loop, Buffer, and EventEmitter dependencies remain on the shared runtime. Socket
fields and synchronous method helpers stay local to the emitter, and named-import dispatch remains
in the shared built-in module registry.

Net uses `Net` / `RequireNet()` for its module factories, TCP/IPC socket and server metadata,
and native BlockList enforcement handles. `EmitAll` starts it for `UsesNet`, including net implied
by HTTP, TLS, or fetch. Socket methods are declared before factories, TLS subclasses, and accept/read
closures reference them; phase 2 uses those same component handles to emit bodies and finalize the
socket and server types. Completion follows runtime and dependent-type finalization. Net factories
and BlockList emission take `EmittedNetRuntime` directly; EventEmitter, Buffer, event-loop helpers,
and module registration remain separate dependencies. Migrated type/method handles have no duplicate
emitter fields. Socket/server fields, other transport methods, and closure construction state remain
local to the emitter.

Promise uses `Promise` / `RequirePromise()` for its 77 type, capability, resolving-callback,
combinator, prototype, adoption, and reaction handles. `EmitAll` starts it for `UsesPromise`,
including implied async/module features and hosted emission. Adoption helpers are declared before
resolving callbacks, and capability helpers before static wrappers; later body emitters reuse those
declarations. Completion follows runtime and dependent-type finalization. Promise type emission,
task wrapping, and capability-only helpers accept `EmittedPromiseRuntime` directly. State-machine
carriers and the Promise task field stay local to the emitter. The shared FIFO `QueuePromiseJob`,
timer/stream Promise APIs, and filesystem/module registries remain owned by their respective
infrastructure or feature families; they are not duplicate Promise handles and remain in the
residual migration scope of #1599.

Array storage uses the required `ArrayStorage` component for its 47 declarations: the `$Array`
and `$ArrayHole` types, constructors, sparse and packed-double accessors, rest builders, and four
immutable numeric/boolean queue records. These types are always emitted, including for minimal
tree-shaken programs, so the component is created with `EmittedRuntime` rather than gated on an
optional feature. `EnsureBoxed` is declared before the base-list methods that call it; its later
body emission uses the same component handle without an emitter-local alias. Array-only helpers
accept `EmittedArrayStorageRuntime` directly, while descriptor, error, and undefined dependencies
still require the shared runtime. `EmitAll` completes storage after runtime finalization, validating
queue declarations as well as array handles. Boolean queues intentionally omit unboxed numeric
reads. Array operations have a separate component described below. ArrayBuffer/TypedArray
metadata and the shared call-argument pool remain separate residual work under #1599.

Array operations use the required `ArrayOperations` component for 108 declarations: construction
and static helpers, ordinary and specialized operations, prototype population, bound-method
dispatch, the live array iterator constructor, and array-like receiver/callback context. Like
storage, these helpers are emitted even for minimal tree-shaken programs. Bound-method invocation,
prototype population, and array-like materializers/loaders are declared before their consumers
and filled in later through the same handles. Declaration-only and operation-only helpers accept
`EmittedArrayOperationsRuntime` directly; storage, invocation, descriptors, and coercion remain
separate dependencies. The original and lazy array-like receiver paths share one checked handle
for the existing thread-static field, without duplicate metadata storage. `EmitAll` completes
operations after runtime and bound-method finalization. General iteration/destructuring helpers,
collection iterators, generic element/length/key access, coercion, static-member dispatch, and
call-argument expansion remain in their respective residual families under #1599.

Node crypto uses `Crypto` / `RequireCrypto()` for its 105 hash/cipher constructors, module helpers,
digest/encoding primitives, scrypt, signing, key exchange, KeyObject, and X509 declarations. `EmitAll`
starts it only for `UsesCrypto`, before the primitives and value types, and completes it after the
deferred Sign/Verify, DH/ECDH, and bound-method bodies and types are finalized. DH/ECDH type handles
and their forward-declared `GetMember` methods have a single source in the component. Crypto-only
helpers accept `EmittedCryptoRuntime` directly; Buffer, Promise, and generic property/invocation
helpers remain separate dependencies. X509 constructor dispatch checks feature availability
explicitly so a user class still resolves when crypto is absent.

The shared built-in module registry still owns crypto named-import wrappers and aliases. Node crypto's
other type-local fields and construction state remain with their emitters; the residual-state audit
must review that ownership before closing #1599.

WebCrypto uses required `WebCrypto` metadata for the `GetObject` accessor, which is declared in runtime
phase 1 even when crypto is disabled. Its optional `Implementation` owns 49 helper, type, constructor,
and key-field declarations. `EmitAll` starts that implementation only for `UsesCrypto`;
`RequireImplementation()` diagnoses accidental use while it is absent. The accessor later receives
either the lazy singleton body or the existing null-returning stub. `EmitAll` completes the required
component after runtime/type finalization; completion validates the accessor and every enabled
implementation handle, then freezes both. Failed validation leaves them incomplete so missing
declarations can be supplied before retrying. A completed stub cannot later enable an implementation.

WebCrypto-only helpers accept `EmittedWebCryptoImplementation` directly. All 49 former mutable emitter
fields are removed; the immutable hash-name table, BCL method lookups, and method-local construction
state stay with the emitter. Buffer, ArrayBuffer, TypedArray, Promise, and generic property/coercion
helpers remain separate dependencies. Module and global-property consumers share the checked accessor;
the built-in module registry still owns the `getRandomValues` wrapper.

HTTP module/server metadata uses `Http` / `RequireHttp()` for 47 declarations: module factories,
utilities and agent helpers; server/request/response types, constructors and fields; and the deferred
accept worker and its closure. `EmitAll` starts the component only for `UsesHttp`, including HTTP
implied by fetch or its Web API constructors. Declaration of the accept worker precedes the listen
method's delegate reference; phase 2 fills that same handle after the accept closure is available.
Completion validates and freezes all handles after runtime and dependent types are finalized.
The 18 former flat properties and 29 emitter fields have no parallel aliases. HTTP-only helpers
accept `EmittedHttpRuntime` directly; net/TLS, EventEmitter, streams, Buffer, scheduling, and generic
property/invocation helpers remain separate dependencies.

The built-in module registry still owns HTTP named-import wrappers, while the TypeScript standard
library retains client/agent behavior. BCL type lookups and method-local construction state remain
with the emitter; include those ownership boundaries in the final residual-state audit.

Fetch uses required `EmittedFetchRuntime` metadata for the global function-cache field, which is
declared even in minimal programs. Its optional `Implementation` contains 31 fetch/Headers/Request/
Response declarations. `EmitAll` enables it for `UsesHttp`, preserving Web API emission for HTTP-only
imports as well as fetch-family references. The global getter still exposes fetch only for `UsesFetch`.
The implementation's optional `Client` owns 15 declarations for async dispatch, four cached clients,
the shared cookie container, and cookie-jar helpers. It is enabled only when the existing HttpClient
and HttpRequestMessage lookups succeed; otherwise fetch retains its rejected-promise fallback and
does not require client declarations.

Completion validates each parent's handles before completing its child, then freezes the parent.
Missing declarations name the handle and leave completion retryable; a completed parent cannot enable
a child later. All 15 former flat properties and 28 emitter fields are removed, including the duplicate
Headers setter alias. The dispatch helper field has one checked handle instead of a reflection lookup;
the four cached-client fields also live in the client component. Fetch-only helpers take the relevant
component directly. HTTP/net/TLS, Promise, streams, Buffer/ArrayBuffer, scheduling, and generic
property/coercion helpers retain their owners. BCL lookups and method-local helpers remain with the
emitter. Declaration order, feature implications, emitted signatures, and cache/cookie behavior are
unchanged.

Node Buffer uses optional `EmittedBufferRuntime`, started only for `UsesBuffer`. It owns 79 checked
handles: the former 76 flat properties plus the backing-data field and two module coercion helpers.
`HasTypedArrayCopy` captures the existing `HasAnyTypedArray` gate at creation; `CopyBytesFrom` rejects
access when disabled and is required for completion only when enabled. The other 78 declarations
are required whenever Buffer is emitted. Completion validates and freezes the component after
runtime finalization, preserving the early `$Buffer` class emission and later module-helper emission.

The 84 Buffer-only helpers accept the component directly, including the numeric read/write and
encoding helpers that previously depended on the emitter's backing-data field. Cross-feature
consumers retain their own dependencies on typed arrays, crypto, streams, HTTP, filesystem, generic
property access, and iteration. Optional type probes explicitly check component availability.
SharedArrayBuffer, DataView, and TypedArray remain separate migration phases; the
typed-array bytes-per-element getter retains that family's ownership. No flat Buffer aliases or
Buffer declaration fields remain in the emitter, and guest signatures and feature implications
are unchanged.

ArrayBuffer uses optional `EmittedArrayBufferRuntime`, enabled by the existing `HasAnyTypedArray`
gate. It owns 13 checked declarations: the former ten flat type/constructor/accessor/slice/static
helper properties, both backing-storage fields, and the `Detach` method. The shared gate still
emits all typed-array families together; this component does not introduce per-kind tree shaking.
Declarations become readable during the early `$ArrayBuffer` emission and later `$Runtime` helper
emission; completion validates and freezes them after runtime finalization.

Six ArrayBuffer-only helpers take the component directly. Constructor error handling, dynamic-slice
coercion, `isView` checks, DataView/TypedArray backing storage, structured cloning, WebCrypto, and
stream consumers retain their other dependencies. Optional probes check component availability.
ArrayBuffer has no remaining flat aliases; method-local IL construction state and BCL lookups stay
with the emitter. Backing-storage identity, slice copies, and detached-byte-length behavior are
unchanged. TypedArray storage has its own component, described below.

SharedArrayBuffer uses optional `EmittedSharedArrayBufferRuntime`, also enabled by
`HasAnyTypedArray`. Its nine declarations replace eight flat properties and own the readonly
backing-buffer field: type, constructor, byte-length and backing-storage accessors, slice, and
three runtime wrappers. All seven SharedArrayBuffer-only helpers take the component directly.
Early type emission and later wrapper emission retain their order; completion follows runtime
finalization and validates every handle before rejecting further writes. Minimal and worker-only
programs leave the component absent; the existing typed-array, view, Atomics, and crypto implications
still enable it. Optional probes test component availability rather than reading undeclared types.

DataView/TypedArray views, structured cloning, worker-realm sharing, and generic property access
retain their other dependencies. The stable `$SharedArrayBuffer` shape and `GetBuffer` entry point
still expose the same backing byte array to cross-realm consumers; slices allocate independent
storage. Method-local construction handles and BCL lookups stay with the emitter. No flat
SharedArrayBuffer aliases remain. Worker/Atomics and other residual families
remain tracked by #1599.

DataView uses optional `EmittedDataViewRuntime` under the same `HasAnyTypedArray` gate. Its 53
checked declarations replace 49 flat properties and own four readonly fields: backing bytes,
original buffer identity, byte offset, and byte length. It includes the emitted type/constructor,
properties, ten numeric/BigInt getter-setter pairs, and their runtime adapters. Instance methods
are emitted first; adapters follow before generic property dispatch binds method values.
`Reflect.construct` tests component availability and uses the checked constructor adapter.
Completion validates all declarations and freezes the component after runtime finalization.

Fourteen DataView-only reader/property/adapter and method-lookup helpers receive the component
directly. Constructors retain ArrayBuffer/SharedArrayBuffer dependencies, while setters retain
undefined and numeric/BigInt coercion dependencies. Bounds and byte-emission utilities keep
explicit method-local IL inputs. Field and method shapes, endian behavior, feature implications,
and buffer aliasing are unchanged. No flat DataView aliases remain; the remaining worker/Atomics
and runtime families stay in #1599's residual scope.

TypedArray uses required `EmittedTypedArrayRuntime` for its always-emitted detection helper and
optional `EmittedTypedArrayImplementation`, enabled under the existing `HasAnyTypedArray` gate.
Its 107 declarations replace 62 flat handles, own seven previously emitter-held storage/factory
handles, and validate 38 entries in four registries. The duplicate emitter base-type field is
removed. The implementation owns the base type and fields, all eleven concrete types and their
constructors, bulk methods, the bound-method wrapper, and element/member adapters.

The unboxed getter/setter registries require all eight numeric kinds; clamped and BigInt arrays
keep their boxed paths. Constructor registries require all eleven kinds. Registries expose live
read-only views during declaration, reject unsupported/duplicate/null registrations, and reject
all writes after completion. Completion checks every required handle and entry before freezing
the implementation and then the required detection component. Feature-absent programs still emit
the false-returning detection helper, with no TypedArray implementation types or constructors.

Thirty-four implementation/storage helpers and the required detection helper now take their
owning component directly. Buffer constructors, bound-method finalization, structured cloning,
Buffer, WebCrypto, Atomics, and generic dispatch retain their other dependencies and feature gates.
Base/concrete type emission, early bound-method declarations, later constructor adapters, and
bound-method finalization keep their order. Inlining, receiver/backing hoists, byte access, and
fallback selection are unchanged. BCL lookups and method-local construction handles remain with
the emitter; no flat TypedArray aliases or mutable registries remain. The remaining runtime
families and final residual-state audit stay open in #1599.

EventEmitter uses required `EmittedEventEmitterRuntime`, preserving its unconditional emission
for process events and the stream/network/worker types that inherit from it. Its 26 checked
declarations replace 19 flat handles and own seven previously emitter-held listener-storage,
wrapper, and rejection-routing handles. All metadata is readable at declaration time, completion
validates every handle after runtime finalization, and subsequent assignments are rejected.

Seventeen EventEmitter-only declaration, listener, and limit helpers take the component directly.
Listener-array results, callback invocation, errors, and Promise-aware rejection routing retain
their other runtime dependencies. The listener wrapper is still emitted first; the virtual
listener-added hook precedes registration, and the mutually recursive Emit/rejection-routing
methods preserve their declaration/body order. Promise-specific rejection branches keep their
existing feature gate even though EventEmitter itself is always present. Listener ordering,
once/removal behavior, subclass hooks, error monitoring, and emitted signatures remain unchanged.
No EventEmitter flat aliases or emitter-held guest declarations remain. The twelve open-generic
BCL method caches, constant monitor key, and method-local construction state remain with the
emitter. The process-specific singleton/access helpers, streams, networking, and worker scheduling
retain their own owners and remain in #1599's residual scope where not already migrated.

Timers use required `EmittedTimerRuntime` for 26 timeout and virtual-queue declarations, with
required `EmittedMicrotaskRuntime` owning the four shared FIFO callback/Promise-job helpers.
Optional `EmittedTimerPromiseRuntime` follows the existing `UsesPromise` gate, including hosted
output's implied Promise support. Its 22 declarations replace seven flat helpers and take
ownership of all fifteen emitter-held promise/async-interval closure handles. Together these
components remove 37 flat properties without changing guest type or member signatures.

Twelve timer-only, microtask-only, and promise-timer-only helpers accept their component directly.
Helpers that invoke callbacks, wrap promises, inspect abort signals, or interact with the event
loop retain their other runtime dependencies. `$VirtualTimer` still precedes `$TSTimeout`, and
the shared `QueuePromiseJob` declaration still precedes Promise reaction emission. Its body is
filled after `ProcessMicrotasks` is declared, preserving the forward call and common FIFO queue.
Completion validates and freezes all enabled handles after runtime finalization. Plain timer and
microtask programs still omit Promise timer types; hosted and full emission complete them.

Timer cancellation, ref/unref accounting, interval rescheduling, Date.now cooperative pumping,
module wrappers, AbortSignal cancellation, and Promise-job ordering remain unchanged. No timer
flat aliases or emitter-held guest declarations remain. Method-local queue, callback-wrapper,
and timeout construction handles stay local. Event-loop/synchronization-context metadata,
process nextTick accessors, and AbortSignal helpers remain separate families in #1599.

The event loop uses required `EmittedEventLoopRuntime` for fifteen scheduler, queue, wake,
reference-count, timer-callback, and synchronization-context declarations. Its optional
`EmittedHostedEventLoopRuntime` owns seven hosted hooks and two hosted storage fields, enabled
only by hosted output. These components replace nineteen flat handles and five emitter-held
fields, and consolidate the duplicate timer-processor handle. The core component completes
its hosted child, when present, after runtime finalization; incomplete declarations leave both
retryable, while successful completion freezes handles and prevents enabling hosted hooks later.

Ten event-loop helpers accept the component or hosted child directly. The class orchestrator,
Run, and WaitForTask retain shared cancellation metadata. `$EventLoop` still precedes its
synchronization context and the runtime's timer helpers: its public timer-processor field remains
the bridge to the later timer delegate. Hosted await consumers test component availability
instead of an undeclared method handle. Plain output, including full-feature emission, omits the
hosted fields, methods, and hosting-contract reference; hosted output preserves its existing
declarations and dependency rules. Callback ordering, continuation dispatch, ref/unref accounting,
cooperative pumping, quiescence, beforeExit handling, and hosted shutdown behavior are unchanged.
No event-loop flat aliases or emitter-held guest handles remain. Method-local singleton,
constructor, synchronization-context, and closure construction metadata stay local. General
cancellation and module/host-execution infrastructure remain separate scope in #1599.

Node streams use optional `EmittedNodeStreamRuntime` for 109 checked declarations: the former
55 flat properties and 54 emitter-held fields, constructors, and methods. The component starts
only for `UsesNodeStreams`, preserving direct stream imports and the filesystem, HTTP, zlib,
child-process, and process-stream implications. Its immutable `HasAbortSignal` flag records
the separate `UsesAbortController` gate for `AddAbortSignal`; the listener closure is still
emitted for all Node stream programs. All other handles, including the previously nullable
factory setters and stream Promise wrappers, are required whenever Node streams are selected.
The optional iterator dispatch now checks component availability instead of probing individual
handles. The existing addAbortSignal fallback remains in place when its wrapper is unavailable.

Writable construction, Readable/Duplex declarations, Readable Push/Pipe bodies, Duplex
finalization, Transform/callback emission, and Readable Map/Filter finalization keep their
existing order. Stream utility and abort-wrapper declarations finish before component
completion at runtime finalization. Forty-three stream-only helpers receive the component;
cross-feature helpers retain their EventEmitter, Promise, function, Buffer, filesystem, and
other runtime dependencies. All migrated flat aliases and emitter-held Node stream handles
are removed. Method-local IL construction, BCL lookups, and the shared built-in module method
registry remain in their existing locations. Filesystem-owned stream subclasses and the final
shared-registry/residual-state audit remain separate work under #1599.

Web streams use optional `EmittedWebStreamRuntime` for 45 checked declarations: fourteen
former flat properties and 31 emitter-held guest handles. `UsesWebStreams` starts the component;
ordinary Node streams and hosted output do not enable it by themselves. Readable, writable,
and transform streams, their peer reader/writer/controller storage, both queuing strategies,
and the transform sink adapter share that existing group gate. Optional constructor lookup
checks component availability and retains its missing-variable fallback. Bare queuing-strategy
names still do not activate the group without a `stream/web` import or another Web stream
reference; that existing detector gap is separate from metadata ownership.

Late Web stream emission still follows the runtime method declarations. Writer and controller
forward references, Readable stream and reader/controller calls, transform sink creation,
and type finalization keep their order. Completion follows runtime finalization and freezes all
selected handles. Thirty helpers receive the component directly; cross-feature helpers keep
their Promise, invocation, iteration, Buffer, abort-signal, and scheduling dependencies. The
three BCL construction types (`List<object>`, `TaskCompletionSource<object>`, and its queue),
method-local declaration builders, and shared dispatch registries stay with their existing
construction infrastructure for the final #1599 audit. No migrated flat aliases or emitter-held
Web stream guest handles remain.

Six backing fields are assembly-visible because peer emitted classes access them: writable
`_state`, `_storedError`, `_writer`, `_highWaterMark`, and readable `_locked`, `_reader`.
This repairs the six existing field-access IL-verifier errors in controller `Error`, writer
`ReleaseLock`/`DesiredSize`, and reader `ReleaseLock`. Other storage remains private. Generated public
signatures and method bodies are unchanged; standalone and hosted tests verify these
peer accesses as well as the component lifecycle.

During emission, each handle becomes readable as soon as its declaration is assigned, so forward
references do not require a method body to exist yet. An early read names the missing declaration.
Completion is an orchestration boundary, not an IL verifier: body emission and type finalization
must finish before that boundary. Preserve existing emission order when migrating a family.

Follow this pattern for subsequent families: explicit optional availability, checked declarations,
one completion boundary, and no retained flat aliases. Pass the component to helpers that only
need that family's metadata (for example, DNS resolver declaration); retain `EmittedRuntime` where
cross-feature helpers are needed. These holders belong to the compiler host and must never appear
as metadata dependencies in guest output. Keep feature gating, emitted signatures, and deployment
capability recording unchanged.

### Specialized representations

The compiler may replace ordinary boxed JavaScript storage with generated CLR locals, fields,
arrays, or compact record carriers when conservative whole-program analysis proves the value's
shape, element kind, call-only use, and escape behavior. These representations are internal
implementation details, not a guest-visible object ABI.

Every specialization needs an ordinary representation or materialization path for dynamic access,
mutation, export, reflection-like operations, unknown calls, and other proof boundaries. Uncertainty
disables the optimization. Generated record shapes and JSON-specialized carriers must preserve
property order, aliases, recursive identity, `null`/`undefined`, and observable JavaScript object
semantics when materialized.

### Emitted-runtime constraint

Normal compiled output embeds the JavaScript runtime helpers it uses. Code emitted into the guest
assembly must not accidentally introduce a metadata reference to implementation types in
`SharpTS.dll`. In particular, an emitter must not put `typeof(SharpTSType).GetMethod(...)` tokens in
guest IL.

There are three legitimate dependency forms:

| Form | Rule |
| --- | --- |
| Embedded/pure BCL helper | Preferred. Emit the helper/type into the guest and tree-shake it when unused. |
| Soft managed SharpTS dependency | Emit the canonical late-bound reflection pattern and call `RequireSharpTSRuntime` with a stable capability flag. The CLI copies `SharpTS.dll` only when required. |
| External .NET assembly | Emit the intentional hard reference and copy the used assembly plus its copy-local closure unless deployment copying is suppressed. |

`--standalone` suppresses automatic copies; it does not change dependency semantics. A feature that
normally requires a soft dependency must fail clearly when the runtime is absent. Native AOT
compiler hosts reject required managed-runtime capabilities before producing unusable output.

### Runtime tree-shaking

`RuntimeFeatureDetector` derives a conservative feature set from the whole checked graph. The
emitter uses it to gate runtime types and helper groups. Uncertainty over-emits. A false negative can
make an assembly unloadable and is never an acceptable size optimization. See the
[runtime tree-shaking outcome](docs/plans/archive/runtime-tree-shaking-outcome.md).

### Debug information

Portable PDB generation consumes the final metadata shape. Documents, checksums, sequence points,
scopes, locals, and async mappings refer back to TypeScript sources. Any post-emission metadata
rewrite must keep PDB table counts synchronized with the finished PE.

## Execution-mode contract

The interpreter and compiler are peers sharing a front end. Supported programs should have equal
observable behavior, but neither backend is assumed correct solely because it disagrees with the
other. Shared dual-mode tests, Node reference tests, and committed Test262 baselines identify drift.
Documented deviations live in [Execution modes](docs/execution-modes.md) and
[STATUS.md](STATUS.md).

Backend-only implementation details are allowed; backend-only public semantics require a deliberate
contract decision. Performance optimizations must add parity tests before benchmark evidence.

## Hosting and Native AOT

Managed embedding APIs orchestrate front-end/backend services without CLI console or exit behavior.
The bounded single-source service is not a sandbox; untrusted code requires process and OS-level
limits.

Hosted compiled DLLs expose a versioned factory/lifecycle ABI through
`SharpTS.Hosting.Abstractions`. Generated `$` types are private. Native AOT uses explicit AST
catalogs, reflection annotations, and generated closed .NET interop catalogs; the native host does
not discover arbitrary application types at runtime. See [Embedding](docs/embedding.md) and
[Native AOT](docs/native-aot.md).

## Architectural invariants

Changes should preserve these rules:

1. One parser/checker/project model feeds both execution backends.
2. Compile-time environments never substitute for runtime environments.
3. Public built-in declarations, interpreter exports, compiled exports, and tests stay synchronized.
4. Guest abrupt completion preserves value identity and origin across host boundaries.
5. Compiled output gains no accidental hard reference to `SharpTS.dll`.
6. Soft and external dependencies are declared by the compiler and handled conditionally by the
   deployment layer.
7. Runtime feature detection is conservative and whole-program.
8. Optimizations preserve aliases, evaluation order, exceptions, and backend parity.
9. Library services return structured results; the CLI owns presentation and process exit.
10. Native AOT support is closed-world and explicit.
11. Interpreter debugging is cooperative and optional; DAP protocol ownership stays outside the
    execution engine.
12. Specialized CLR representations require conservative proofs and a semantics-preserving
    materialization or fallback boundary.
13. GUI application extension stays within the documented TypeScript API; internal native-provider
    seams have no public compatibility promise.
14. Volatile counts, benchmark snapshots, and exhaustive file lists do not define architecture.

## Where to continue

- [Documentation hub](docs/README.md)
- [Contributor workflow](CONTRIBUTING.md)
- [Implementation status](STATUS.md)
- [Execution modes](docs/execution-modes.md)
- [Interpreter debugging](docs/debugging-interpreter.md)
- [MSBuild SDK](docs/msbuild-sdk.md)
- [Benchmark methodology](benchmarks/cross-runtime/README.md)
