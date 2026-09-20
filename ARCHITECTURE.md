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

`EmittedDeploymentRequirements`, exposed as `EmittedRuntime.Deployment`, owns sorted diagnostic
reasons and deployment capability flags for one compilation. Feature emitters and late guest
call sites record requirements through its checked, idempotent `Require` method. The registry
stays open after runtime emission so eval, Worker and other guest calls can contribute. Single-
file and module compilation complete it only after all guest types are finalized, including
timed and hosted paths. Completion validates the reason/flag state and freezes recording.
Reasons are immutable snapshots; consumers cannot clear the deployment signal through a
collection cast. `ILCompiler` retains its existing read-only deployment API and forwards it to
this owner. Pure-BCL output, soft runtime dependency selection and deployment policy are unchanged.

`FrameworkEmitMetadata` owns the shared array span method references and the ten-row crypto
digest catalog. Its get-only handles refer exclusively to framework assemblies; the digest
rows use `ImmutableArray`, so consumers cannot modify the table through a mutable collection
view. These references are initialized once and never hold generated types or methods. Runtime
and user-code emitters use the same handles. Feature selection and per-compilation declaration,
forward-reference and completion checks remain with the emitted runtime components. This
catalog preserves method selection, digest order, platform support checks and emitted IL;
method-local reflection and the other shared infrastructure still require the residual audit.

`CryptoPrimeEmitConstants`, `ProcessSignalEmitConstants` and `WebCryptoEmitConstants`
own the fixed trial divisors, signal mappings and algorithm-name pairs used by their emitters.
These process-wide catalogs expose get-only `ImmutableArray` values; callers cannot change
later compilations through a mutable array alias. Catalog order remains emission order.
They contain no generated declarations or feature selection. Per-compilation handles and
completion remain with the corresponding runtime components.

`DatePrototypeEmitCatalog` owns the immutable prototype wiring definitions: JavaScript
names, declared lengths and static selectors. Each selector reads a checked method from
the supplied `EmittedDateImplementation`; the shared catalog never retains a generated
handle. Date feature selection, forward declarations and completion remain per compilation.

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
carriers stay local to the emitter. The Promise task field handle stays local to
`EmitTSPromiseClass`. The shared FIFO `QueuePromiseJob`,
timer/stream Promise APIs, and filesystem/module registries remain owned by their respective
infrastructure or feature families; they are not duplicate Promise handles and remain in the
residual migration scope of #1599.

Array storage uses the required `ArrayStorage` component for its 47 declarations: the `$Array`
and `$ArrayHole` types, constructors, sparse and packed-double accessors, rest builders, and four
immutable numeric/boolean queue records. These types are always emitted, including for minimal
tree-shaken programs, so the component is created with `EmittedRuntime` rather than gated on an
optional feature. `EnsureBoxed` is declared before the base-list methods that call it; its later
body emission uses the same component handle without an emitter-local alias. The eight private
storage fields, sparse dictionary type and nine BCL collection methods are immutable construction
inputs local to emitting `$Array`; 31 helper boundaries receive those inputs explicitly. Collection
methods resolve before type definition, and private fields retain their declaration order. The
component rejects duplicate declarations before completion and permits missing-declaration repair.
Array-only helpers accept `EmittedArrayStorageRuntime` directly, while descriptor, error, and undefined dependencies
still require the shared runtime. `EmitAll` completes storage after runtime finalization, validating
queue declarations as well as array handles. Boolean queues intentionally omit unboxed numeric
reads. Array operations have a separate component described below. ArrayBuffer/TypedArray
metadata and the shared call-argument pool remain separate residual work under #1599.

Object literal storage uses the required `ObjectStorage` component for 18 declarations: the
`$Object` type, constructor, field access, accessor maps, and mutation restrictions. Its type
slot retains the original `TypeBuilder` identity after finalization, and its members retain
their builder kinds. Object helpers receive this owner and their exact descriptor, invocation,
undefined, interface, and error-constructor dependencies. The six generated storage fields are
local construction inputs rather than state retained on `RuntimeEmitter`. Early strict-mode
errors still wrap the guest TypeError in a CLR exception before `$Runtime.CreateException` exists.
`EmitAll` validates and freezes the metadata after runtime finalization; this does not freeze guest
objects or their original mutable field dictionaries. Generic object operations and shared
interfaces remain separate residual work under #1599.

Descriptor, prototype and extensibility storage uses required `EmittedDescriptorStorageRuntime`
for 35 declarations, exposed through the get-only `DescriptorStorage` component. The three support
types and their ten reflected properties publish after type finalization; the descriptor constructor
retains its original builder through its `ConstructorInfo` slot. These early declarations remain
usable before the 21 storage methods exist. Checked access reports missing declarations, failed
completion permits repair, and successful `EmitAll` completion freezes metadata assignments.
The family helpers take the owner and exact function-key or undefined inputs where needed.
Function wrappers still normalize to their MethodInfo when available; methodless wrappers keep
their own identity. Four ConditionalWeakTable fields and closed BCL construction references stay
local to emission, and each saved output owns fresh mutable descriptor/prototype/extensibility
and symbol tables. Generic Object operations and the shared auto-property emitter remain
separate ownership and infrastructure work under #1599.

Reflect uses required `EmittedReflectRuntime` for ordinary receiver-aware reads, plus three
explicit optional capabilities. Assignment has three declarations selected by `UsesReflect || UsesProxy`;
its Set and DefineProperty shells keep their phase-one positions and receive bodies later.
Namespace has eight declarations and thirteen callable wrappers selected by `UsesReflect`.
Metadata has seven declarations selected independently by `UsesReflectMetadata`; its five helpers
precede the later decorator constructor and Invoke method. These nineteen handles retain their
original builder kinds, emitted signatures and declaration order. Checked getters report missing
declarations. Completion validates every selected capability and required wrapper before freezing
any, so failed validation remains repairable across the entire family.

Namespace registration is ordinal, rejects duplicate/unknown names and closes at completion; its
public dictionary is a read-only view. Guest singleton fields and metadata dictionaries remain mutable.
Value-form property reads still populate and then inspect the singleton so replacement/deletion stays
observable. The lazy metadata-store field is a local construction input; repeated compilation keeps
stores and decorator closures isolated in their own generated assemblies. Family operations receive
their owner and exact immutable inputs, including actual optional Promise/DataView components.
Shared Proxy bridges accept exact callback/unwrapping inputs and retain adapters for generic callers.
The separate constructor classifier, generic Object/invocation/Proxy ownership, and shared
BCL/singleton infrastructure remain residual work under #1599.

JSON uses the required `Json` component for its singleton field and populate method. Its
optional `Implementation` is selected by `UsesJSON` (also implied by HTTP detection) and owns
25 declarations: ten parse/stringify/raw-value handles and fifteen cached helper methods.
The raw-value type precedes the JSON helper bodies; the singleton field and populate shell
retain their existing runtime-class positions after phase one. Checked reads report missing
declarations, recursive helpers reuse internal declaration availability, and completion checks
all selected declarations before freezing the two owners. Every compilation gets fresh handles,
including when one RuntimeEmitter emits JSON, a minimal runtime and JSON again.

The guest string-identity shape table remains lazy and isolated per saved assembly. Its field
is a local construction input, and the generated GetJsonShapeTable method remains without a
persistent emitter cache. Guest JSON namespace members and shape associations remain mutable.
Family helpers take the JSON implementation, peer capabilities, exact handles and relevant
analysis facts; optional RegExp branches inspect the supplied type. Shared BCL/invocation
infrastructure remains required work under #1599.

Map and Set use independent optional `Map` and `Set` components, selected at orchestration
from `UsesMap` and `UsesSet`. The required `CollectionKeys` component owns the per-output
equality comparer and null-key sentinel, also needed by shared iteration when Map is absent.
Map owns 22 declarations and Set owns 25; there are no flat compatibility aliases. Family
helpers receive those owners, array components and explicit error/invocation/iteration handles.
Consumer dispatch follows provided component availability. Bound wrapper declarations precede
generic function helpers and early property dispatch; iterator constructors precede operation
bodies; bound Invoke bodies and type finalization follow runtime-class emission. Completion
validates every selected handle and freezes compiler metadata after these stages. Guest
collections, iterator behavior and per-output comparer/sentinel identity remain mutable or
isolated as before. General iteration and the full residual audit remain separate work
under #1599.

Date has a required `Dates` owner for the prototype field and population declaration, plus
an optional implementation selected once by orchestration. The implementation owns 58 type,
constructor and operation handles and a checked 44-entry instance-method registry. The Date
type and its methods are available early; runtime wrappers follow timer infrastructure, and
prototype population follows those wrappers. Completion validates every selected handle,
registry entry and the emitted prototype body, including its single-return absent path.
Failed completion remains repairable. The registry exposes a read-only view and rejects
unknown, null, duplicate and frozen declarations. Three Date field builders are local
construction inputs; GetTime uses the instance registry instead of an emitter-held alias.
Sixty emission signatures receive scoped metadata and exact peers. Generic consumers inspect
the supplied optional implementation. Native numeric signatures, mutable guest prototypes,
timer calls and generated order are preserved. The locale/options wrapper exists whenever
Date is selected, while its soft runtime dependency is recorded only at call sites using it.

Error metadata uses the required `Errors` owner for 68 type, constructor, prototype,
operation and exception-bridge declarations. Forty-five emission helpers receive scoped
owners and exact dependencies; nine construction fields become local inputs. The two early
factory declarations and eight prototype declarations keep their original body-emission
order, with four explicit completion stages. Checked access remains available before
completion; missing declarations/stages can be repaired, and completed metadata rejects
further writes. Guest prototypes and error values retain their original mutable lifetimes.
The guest Error hierarchy still derives from object, with a separate CLR thrown-value
carrier; the strict SyntaxError helper retains its existing CLR exception representation.
Exception wrapping follows supplied optional Promise metadata. Error-cause Proxy handling
receives six explicit invocation/delegate handles through the shared reflection bridge.

Array binding-pattern normalization belongs to the required `ArrayOperations`
component through `DestructureSource`. Its emitter receives only that owner and
the symbol type, iterator materializer and array-storage constructor. The helper
keeps its original late emission point and the existing array-operations completion
boundary. Indexed-source pass-through, iterator materialization, generated method
signatures and deployment behavior remain unchanged by this ownership migration.

Required `IteratorHelpers` owns normalization, lazy adapter constructors,
factory declarations and eager iterator operations. Scoped emitters receive
exact wrapper, error, invocation, truthiness, generator and undefined inputs;
callback helpers receive only their supplied invocation and truthiness handles.
The existing generator-type availability branch remains unchanged. The component
completes after the original helper orchestration call. Five adapter types keep
their original local fields, construction order and finalization boundaries.
Lazy callback timing, indices, eager consumption, completion values and generated
method signatures remain unchanged by this ownership migration.

Required `IteratorCollection` owns iterable-to-list and append-to-list method
handles. Both remain forward-declared before earlier consumers, and completion
follows both bodies at their original orchestration boundary. Scoped helpers
receive exact peer metadata, explicit array-mutation selection, optional
TypedArray implementation, Map availability and the fine-selected Buffer
component. Dense appending receives array storage and the undefined sentinel.
The live array-iterator helper keeps its ArrayOperations owner and receives
only Arguments metadata and indexed-read dependencies. Collection fast paths,
Unicode handling, captured next, completion-value behavior and local iterator
construction remain unchanged.

Required `IteratorProtocol` owns iterator lookup, next invocation, result
reading, close and dynamic protocol-call declarations. Its early dynamic-call
declaration stays available before the basic iterator helpers; the component
completes after their original emission sequence. Nine helpers receive exact
owner/peer inputs. Compact-result readers receive the selected shape set,
shape dictionary and descriptor flag explicitly, preserving materialization
and descriptor guards. Optional NodeStreams lookup stays nullable. The
write-only InvokeIteratorNextWithSent holder store is removed while its
generated public method, local handle, signature and IL remain unchanged.

Required `IteratorWrappers` owns the custom-iterator adapter type, constructor
and sent-value method. It completes immediately after the original wrapper
emission call, which receives the owner and exact captured-next/result-helper
dependencies. Both nullable generator probes, the ignored constructor Type
argument, captured next callable, both Current getters, completion values,
Reset exception, no-op Dispose and local construction order are preserved.

Required `IteratorRecords` owns captured-next lookup and invocation metadata.
Its three declarations complete together immediately after their original
basic-iterator emission call. The helper receives the owner and exact
Undefined/Symbol/ObjectRead/Invocation/Error dependencies, including explicit
error construction. `RequireIteratorObject` remains an emitted public method
and a local construction handle; its unused holder store is removed. Captured
callable identity, receiver/sent-value handling, object-result validation and
the original method order are preserved.

Optional `AsyncGenerators` owns the async generator interface. Its optional
`Continuations` and `FromSync` children independently own async-generator
continuation/result helpers and the async-from-sync adapter. The interface
begins at the original async-generator-or-for-await gate. Each child begins
and completes at its original, separate feature gate; the root completes
after adapter emission and validates selected children without completing
them sequentially. This leaves an incomplete child repairable. Thirteen
helpers receive exact owners and peer inputs, including the runtime type
builder. Three adapter construction handles leave the emitter and become
per-compilation child metadata. Optional runtime/interface probes remain
nullable; selected consumers use checked accessors. Local state-machine and
adapter type builders stay local, preserving type/member/body order.

Required `Generators` owns synchronous generator protocol declarations and the
internal numeric bridge. Both interfaces retain their original creation order;
completion validates all eight declarations after the nested bridge helper.
Independent optional `StableIteratorResults` owns the numeric result value type
and its baked constructor and fields. Its original compact-iterator feature gate
remains early, before function and generator emission. Availability checks use
the nullable owner; selected consumers use `RequireStableIteratorResults()`.
All three helpers receive only their respective owner. Generated interfaces,
layouts, overrides and numeric iteration behavior remain unchanged.

Required `CallArguments` owns the shared argument-array pool accessor and
spread-expansion helper. The pool retains its separate type and early body;
expansion stays after iterator support in the runtime class. Completion
validates both declarations after expansion is emitted. Pool construction
receives its owner alone; expansion receives Symbol metadata and IterateToList
explicitly. Four per-arity thread-static fields remain local to pool emission.
The migration preserves allocation, argument evaluation and iteration behavior.

Required `DynamicConstruction` owns function-valued `new` and general
constructor-value dispatch. The original Function shell remains available
before the runtime class; its body and Value helper keep their later order.
Completion checks that the delayed Function body was emitted. Both helpers
receive exact peer metadata, preserving proxy construction, prototype links,
return-object precedence, ambient receiver restoration, boxed and RegExp
construction, and the existing non-constructor policy.

Required `Invocation` owns dynamic value, receiver-aware and zero-argument
call helpers. The Method shell keeps its original forward-declaration location;
completion follows all three original bodies and checks the delayed Method body.
Six helper boundaries receive explicit metadata, including cancellation and
error declarations. Text decoding, stream callbacks, promises and TypedArray
wrappers retain separate feature selections; Map and Set remain selected by
metadata availability. Tests cover missing declarations, reuse, disabled
optional metadata, receiver identity, zero-argument fallback and saved output.

Required `BuiltInStatics` owns the built-in static-member lookup declaration.
The forward handle is declared before property and descriptor consumer bodies;
its body is filled after the backing methods are available, then completion
requires both the declaration and a body-emission marker before freezing the handle.
The marker rejects missing declarations, repeated marking and completed writes.
Missing, null, duplicate and completed declaration writes
are rejected, while failed completion permits supplying an omitted declaration
or emitting the missing body.
The body emitter receives explicit method handles and cohesive peer components,
including optional BigInt, Promise and Date implementations selected by orchestration.
It does not read global feature flags. Tests cover independent supplied metadata,
all optional combinations, reused emitters, cached wrappers, method names/arities,
standalone and hosted deployment. Hosted emission retains its existing Promise
requirement. Signatures, original declaration order and generated instructions
are preserved. Shared reflection helpers and peer-component boundaries remain
subject to the complete ownership audit.

Optional `ProxyConstruction` owns the ordinary and revocable factory
declarations. The original Proxy feature gate starts and completes this component
and records the same soft runtime requirement. Missing/null/duplicate declarations
and post-completion writes are rejected; failed completion can be repaired.
The scoped factories and argument validation take five explicit metadata inputs:
undefined and Symbol types, the undefined instance, exception wrapper and TypeError
constructor. Tests cover absent/present/reused emission, independent supplied
metadata, invalid arguments, target identity, revocation and runtime deployment.
Generated signatures, method order and instructions remain unchanged. Proxy uses
late binding and needs SharpTS.dll under normal deployment; forcing `--standalone`
continues to suppress copying that DLL and emits the existing warning. Original
type-checking rejection and constructor-timeout observations remain separately
tracked, with unchanged sources, Node expectations and execution deadlines.
Shared reflection helpers and local builders remain in the full ownership audit.

Required `Strings` also owns the shared symbol-protocol dispatch declaration.
Its scoped emitter receives the destination, owner and seven metadata inputs,
including an explicitly optional RegExp type. The existing string completion
boundary validates this handle, and missing/null/duplicate declarations and
post-completion writes are rejected. Tests cover repair after failed completion,
fresh handles across reused emitters with/without RegExp, custom and nullish
hooks, lookup order, exceptions, standalone output and hosted declarations.
The original signature, by-reference flags, method order and generated IL remain
unchanged. Three existing output differences from Node (own-null RegExp match,
primitive prototype hooks and borrowed receivers) are retained separately;
preserving them establishes refactor parity, not passing language conformance.
Shared reflection metadata and local builders remain in the full ownership audit.

Required `ReceiverGuard` owns the receiver-validation helper. Its emitter takes
the destination type, owner, undefined and Symbol types, exception wrapper and
TypeError constructor. Completion follows the original helper emission point.
Missing/null/duplicate declarations are rejected, failed completion can be
repaired, and completed metadata is frozen. The helper preserves receiver
identity and the original null, undefined and Symbol errors, signature,
instructions and declaration order. `$TSFunction` continues resolving the helper
by name in its own emitted assembly, with a fresh cache for each emitted type.
Tests cover these contracts, borrowed and bound methods, primitive conversion,
custom conversion hooks, standalone execution and hosted declarations. Shared
reflection metadata, local builders and the reflection cache remain in the full
ownership audit.

Required `Enums` owns the enum reverse-lookup declaration. Its emitter receives
only the destination type and owner; completion follows the original helper
emission point. Missing/null/duplicate declarations are rejected, an omitted
declaration can be supplied after failed completion, and completed metadata is
frozen. The helper signature, unused enum-name argument, numeric matching,
original missing-value exception, locals, instructions and declaration order
remain unchanged. Tests cover independent helper emission, fresh ownership
across reused emitters, dynamic and duplicate-value lookup, module use,
standalone execution and hosted declarations. The caller's `EnumReverse`
registry, shared reflection metadata and local builders remain in the full audit.

Required `ResourceDisposal` owns the using-declaration disposal helper. Its
emitter receives only the destination type, owner and exact indexed-read,
undefined-type and method-invocation dependencies. Completion follows the
original helper emission point. Missing/null/duplicate declarations are rejected;
an omitted declaration can be supplied after failed completion, and completed
metadata is frozen. Lookup timing, null/missing handling, managed `IDisposable`
fallback, receiver binding, signature, locals, instructions and declaration order
remain unchanged. Tests cover lifecycle repair/freeze, injected dependencies,
fresh ownership across reused emitters, symbol precedence, managed fallback,
standalone executions and hosted declarations. Shared reflection metadata and
local builders remain within the complete audit.

Required `EventSubscriptions` owns the emitted .NET event registry field and
add/remove helpers. The main helper receives its destination type, owner and
static initializer; the two method helpers receive the registry field directly.
Completion follows the original emission point. Checked declarations reject
missing, null and duplicate handles; a failed completion can be repaired by
supplying omitted handles, and completed metadata is frozen. The private static
readonly registry, initializer position, signatures, declaration order, locking,
handler identity and generated instructions remain unchanged. Tests cover the
lifecycle, independent helper emission, fresh registries across reused emitters,
duplicate/removal/re-add behavior and standalone/hosted deployment. Dynamic
event names retain their existing runtime-backed bridge. Shared reflection
lookups and local construction metadata remain within the complete audit.

Required `Namespaces` owns the emitted namespace type, constructor and member
get/set methods. Its helper receives only the destination module and namespace
owner; completion follows the original type-creation point. Checked declarations
reject missing, null and duplicate handles, support repair before completion, and
freeze completed metadata. The private member dictionary, name field, display
method, signatures, declaration order and generated instructions remain unchanged.
Tests cover lifecycle repair/freeze, scoped emission, fresh owners across reused
emitters, member identity and isolation, and standalone/hosted deployment.
Namespace construction registries, local builders, shared type-provider lookups
and the remaining ownership infrastructure stay within the complete audit.

Required `GlobalObject` owns the global singleton, property dictionary, forward
get/set methods and indirect-eval helper. Forward declarations complete at their
original phase-one position; singleton initialization stays in the runtime static
constructor, and the late helper bodies complete the owner. Missing, duplicate,
out-of-order and completed writes fail explicitly. Nullable singleton probes preserve
reference-assembly and partial-runtime paths without reading an undeclared handle.
Property/eval emitters receive exact peer handles or cohesive descriptor/number/error
owners, with optional global choices selected explicitly by orchestration. Generated
signatures, attributes, order, cached identities and optional eval deployment remain
unchanged. Tests cover lifecycle repair/freeze, emitter reuse, absent and combined
features, globals through aliases and indexes, and standalone/hosted deployment.
Shared runtime-type metadata, local builders, registries and remaining ownership
infrastructure stay within the complete audit.

Required `UriComponents` owns the encode/decode component helpers. Its scoped emitter
receives only the runtime type builder, owner, string-conversion method and nullable
undefined-padding attribute constructor. Completion follows the original helper call
before global property dispatch. Generated method signatures, attributes, coercion,
BCL calls, declaration order and cached function wrappers are preserved. Tests cover
missing/duplicate/completed declarations, supplied conversion inputs, attribute presence,
fresh ownership under emitter reuse, direct and value calls, Unicode, and deployment.
GlobalThis/eval state, wrapper caches and shared reflection infrastructure remain in
the complete ownership audit.

Required `ObjectFields` owns the baked `$IHasFields` interface and its four methods.
The scoped emitter receives only the module and owner; completion follows the
original interface-emission point before object storage and record implementations.
The same interface and method handles feed class emitters and existing narrow input
records for property dispatch, conversion, enumeration and record storage. Checked
reads, single declarations and completion guards make the per-output protocol
explicit. Tests cover early availability, reuse, method signatures and order,
dictionary identity, guest mutation through the interface, class and record programs,
and standalone/hosted output. Local builders, class/shape registries, reflection caches
and the existing object components remain in the complete ownership audit.

Required `UnionValues` owns the emitted `$IUnionType` interface and its object-valued
`Value` getter. Its emitter receives only the module and owner; completion follows
the original interface-emission point before dependent declarations. Checked reads,
single declarations and completion guards keep the protocol specific to one output.
The union generator receives that output's interface, and the typeof helper receives
the exact interface/getter pair through its existing inputs. Generated union layouts,
conversions and recursive typeof behavior remain unchanged. Tests cover interface
identity, emitter reuse, wrapped primitive/null/undefined values, typed union programs
and standalone/hosted output. Generator dictionaries and shared reflection caches
remain part of the complete construction and shared-infrastructure audit.

Required `Sentinels` owns the distinct `$Undefined` and `$LexicalUninitialized`
singleton types and fields. Both helpers receive only the module and this owner.
They publish the baked declarations once; temporary builders remain local.
Undefined is available before the unchanged four array-queue declarations, and
the owner completes after lexical sentinel emission. Checked reads, duplicate
declaration guards and completion checks prevent incomplete or reused metadata.
Tests cover per-compilation singleton isolation, the early undefined dependency,
captured lexical reads, coercion, omitted arguments, generators, async returns,
and standalone/hosted deployment. Existing captured-assignment behavior is
retained; this migration does not change language semantics.

Required `ReflectedMethods` owns method lookup, parent-method lookup, the weak
receiver/name cache, unwrapped reflection invocation and the staged
`$MethodCallable` wrapper. The original cache field and static initialization stay
in place. Callable type, constructor and Invoke shell remain available before the
runtime helpers; late finalization emits Invoke and creates the type before
completing the owner. Six helpers receive the owner, with explicit error metadata
for unwrapping and the function constructor for parent-method wrappers. Parent
lookup keeps its original emission point, hierarchy search and fresh-wrapper
behavior. Tests preserve overload selection, stable cached wrappers, cache
isolation, exception identity, inherited lookup, generator and async super values, Invoke/Call
fallback behavior and standalone crypto/event dispatch.

Required `FunctionIntrospection` owns function-property lookup and constructor
capability declarations. Both bodies are emitted at their original separate
locations; completion follows constructor-capability emission. The two helpers
receive exact function/object owners and shared sentinel or invocation handles.
Tests cover early property availability, fresh wrappers and prototype caches,
name/length overrides, missing properties, bound constructor capabilities,
reused emitters and isolated deployment.

Required `FunctionPrototypes` owns the Function prototype singleton field and
its early population declaration. The original later body marks population as
emitted before completion; declaring both handles alone cannot complete the
owner. Six helpers receive their owner and exact dependencies, using the shared
scoped descriptor installers and explicit error metadata. Original declaration,
static initialization and wrapper construction order remain unchanged. Tests
cover forward declarations, fresh dictionaries/wrappers, idempotent population,
borrowed call/apply/bind, property descriptors and isolated deployment.

Required `FunctionValues` owns fifteen function-value, invocation-cache and
receiver-context declarations; `Arguments` owns five early context and later
branded argument-object declarations. `FunctionBindings` owns twenty bound
function and bind/call/apply wrapper declarations. The value owner completes
after function creation, arguments after the branded type, and bindings after
the final wrapper. Thirteen helpers receive explicit owners and peer metadata;
function creation also writes the existing construction owner. Optional Map/Set
dispatch follows supplied metadata, with the original declaration order,
receiver restoration, conversion and absent-runtime fallback paths preserved.
Tests cover staged declarations, emitter reuse, opposite collection selections,
argument snapshots, numeric allocation and isolated deployment. General
invocation dispatch and function prototypes remain separate migration work.

Function construction uses required `FunctionConstruction` ownership for ordinary
and cached wrapper constructors, the identity-preserving wrapper factory, and
dynamic Function construction. The owner completes after the original function
wrapper type is created. Dynamic construction receives only that owner and its
exact global/undefined declarations. Factory keys preserve method, name and arity;
per-output instance and invoker caches remain independent across repeated emission.
Invocation metadata, argument context and binding wrappers remain separate work.

Function attribute metadata uses required `FunctionAttributes` ownership for the
seventeen handles of seven attribute classes. Each class is created in its original
order, and the owner completes before function-wrapper emission. Attribute emitters
and wrapper-cache readers take that owner directly. Compiler marker paths preserve
their absent-runtime behavior and require declared attributes when a runtime exists.
Saved-output, marker payload, numeric-rest allocation, reuse and reference-assembly
tests cover this boundary; invocation and per-output function caches remain separate.

Readline helper emission relies on its supplied required declarations; feature
selection remains at the orchestration boundary. AbortSignal.any receives its
fine deployment selection explicitly through the Abort helper group. Ordinary
controller use remains standalone, and selecting any records its soft runtime
dependency at the original point before the helper declaration. Opposite-input,
repeated-emitter, hosted and isolated console-input checks cover these contracts.

Operators use required `Operators` ownership for eleven declarations, including
numeric updates, comparison, classification, membership, addition and equality.
Equality keeps its early declaration and validated late body. Completion checks
every declaration and rejects later metadata writes. Thirteen existing helpers
receive scoped owners and exact dependencies; Promise classification follows
supplied optional metadata. The whole-holder Proxy membership adapter is removed
while its existing scoped implementation is preserved. Comparison helpers retain
the absent-runtime fallback and require declared operators when a runtime is
supplied. Declaration order, guest semantics and deployment remain unchanged.

Named, computed, field and strict writes use required `ObjectWrite` ownership
for six declarations. Property keeps its early declaration and validated late
body; completion checks every handle and rejects subsequent metadata writes.
Twelve helpers receive scoped owners and exact dependencies. Optional receiver
branches follow supplied metadata, while Proxy selection is explicit. The
redundant whole-holder Proxy adapter is removed; its declaration reads remain
inside selected branches and its existing exact call record is preserved.
Strict/sloppy behavior, setter receivers, descriptor and Symbol rules, declaration
order, mutable guest state and deployment remain unchanged.

Property, index, field, list, length and element reads use required `ObjectRead`
ownership for six declarations. Property keeps its early declaration and late
body; completion requires both the declarations and body, and rejects subsequent
writes. Twenty-three helper boundaries take scoped owners and exact dependencies.
Optional receiver branches follow supplied metadata, while list optimization
uses explicit prototype-mutation and descriptor selections. TypedArray detection
also follows its supplied implementation. Declaration order, receiver and key
evaluation, getter behavior, prototype lookup, mutable guest state and deployment
remain unchanged.

Property and index deletion use required `ObjectDeletion` ownership for five
declarations. Checked access rejects missing, null, duplicate and post-completion
writes. Ten helpers receive scoped owners and exact dependencies, including shared
strict TypeError and Proxy callbacks. Index deletion selects Promise callback
receivers from supplied optional metadata. Named strict arguments, declaration and
body order, dictionary compaction, strict/sloppy failures, descriptor and Symbol
rules, array holes, guest mutation and deployment remain unchanged.

Object construction, spread, enumerable projection and rest use required
`ObjectConstruction` ownership for six checked declarations. The owner rejects
missing, null, duplicate and post-completion writes. Seven helpers receive their
owner and scoped dependencies. The dictionary-spread helper receives explicit
Proxy selection, preserving the orchestration gate without reading emitter feature
state. Literal identity, dictionary/object spreading, descriptor and Symbol
filtering, key snapshots, getter effects, JSON projection and rest exclusions stay
unchanged. The plain-data spread path retains its allocation-free guarded copy.
Generated member order, mutable guest state and deployment are preserved.

Object value, entry, assignment, comparison and grouping operations use required
`ObjectOperations` ownership for six declarations. Checked access rejects missing,
null, duplicate and post-completion writes. Bodies and declarations retain their
original order. Seven helpers receive scoped ownership and exact dependencies,
including the shared Proxy enumeration branch. Existing Proxy own-key callbacks and
array deoptimization use scoped inputs. Value and entry ordering, SameValue signed
zero/NaN behavior, assignment identity and setters, grouping callbacks, iterator
inputs, errors, mutable guest state, generated member order and deployment remain
unchanged.

Own-property predicates and legacy accessors use required `ObjectOwnProperties`
ownership for `hasOwnProperty`, `propertyIsEnumerable`, `Object.hasOwn`, and the four
getter/setter lookup and definition helpers. Seven declarations reject missing, null,
duplicate and post-completion metadata writes. Their bodies are emitted with their
declarations in the original order. Six helper signatures receive scoped ownership
and exact dependencies; both Promise callback receiver branches follow supplied
optional metadata. Proxy descriptor calls and computed-message errors use existing
scoped inputs. Descriptor identity, prototype traversal, callable validation, guest
state, receiver argument names, generated member order and deployment are preserved.

Own-property key enumeration uses required `ObjectKeys` ownership for enumerable keys,
own names and symbols, key normalization, ordinary mixed keys and Proxy array-like key
lists. The six declarations reject missing, null, duplicate and post-completion writes.
The two early Proxy callback declarations require an explicit later-body stage. Seven
helper signatures receive this owner and exact dependencies; supplied optional Promise
metadata controls callback name branches. Proxy own-key and descriptor calls retain
their scoped peer inputs. Key filtering and order, index boundaries, read-only symbol
enumeration, mutable guest state, declaration order and deployment remain unchanged.

Object prototypes use required `ObjectPrototypes` ownership for the singleton, its
population helper, `toString`/`valueOf`/`toLocaleString`, `isPrototypeOf`, object creation
and prototype get/set operations. `ClassPrototypes` separately owns the early class marker
and the stable class-prototype lookup/registration declarations. Both components reject
missing, null, duplicate and post-completion metadata writes. The two early Object lookup
and population tokens require explicit later-body completion; the class marker retains its
original declaration position. Thirteen helper signatures receive scoped owners and exact
dependencies. Raw Task and Promise callback branches follow supplied optional Promise
metadata; callable Proxy branding receives its fine-grained selection explicitly. Prototype
installation uses the existing scoped descriptor helpers. The per-output class cache and
legacy prototype table remain local construction inputs. Constructor-free class prototype
creation, registration order, stable guest identities, mutable prototype state, generated
member order and deployment behavior are preserved.

Object descriptor APIs use required `ObjectDescriptors` ownership for `defineProperty`,
`getOwnPropertyDescriptor`, `defineProperties` and `getOwnPropertyDescriptors`. The lookup
token stays available at its early declaration point; its later body is required explicitly
before completion. All four declarations reject missing, null, duplicate and post-completion
writes. Forty-nine helper signatures receive their owner and exact dependencies, including
the two tuple-returning normalization stages. Promise callback and JSON singleton receiver
arms follow supplied component availability. Shared Proxy calls receive explicit invocation
and descriptor handles; their adapters retain other callers. This component is separate from
descriptor storage. Receiver order, shared labels and stack contracts, generated member
order, existing null/undefined behavior, guest descriptor mutation and deployment are unchanged.

Object integrity and deleted-built-in metadata use required `ObjectState` ownership for
four weak-table declarations, six integrity operations and two deletion helpers. Nine helper
signatures receive this component and exact peers; their field parameters come from the owner.
The early `IsExtensible` declaration keeps its later body-emission stage, checked explicitly at
completion. Declaration access is available before completion, incomplete emission can be
repaired, and completion rejects further compiler-metadata writes. Generated table initialization
order and mutable per-output guest contents remain unchanged. Proxy dispatch uses the existing
method-only reflection bridge with explicit invocation and error-construction dependencies.

RegExp has a required `RegExps` owner for its prototype field and population declaration,
with an optional implementation selected once by orchestration. The implementation owns 66
type, constructor, field, operation and protocol handles. Sixty-six flat declarations are
removed, including three write-only stores whose emitted helpers remain. Thirty emitter-held
construction fields are eliminated: 23 become local inputs, five move to the implementation,
and two duplicate aliases use existing owned handles. Eighty-eight emission signatures receive
scoped owners and exact peers. The prototype field is always declared early; the selected
population method and split/matchAll protocol tokens are reserved before the RegExp class.
The absent population method is declared later and has a single-return body, preserving member
order in both configurations. Completion validates all declarations, prototype population and
both late protocol bodies before freezing metadata; incomplete emission remains repairable.
Generic consumers inspect the supplied implementation. Compiled-regex caches stay on each
generated type, while lastIndex and guest prototype mutations retain their existing lifetimes.
The shared string symbol dispatcher is an explicit peer. The late AST-identity regex-hoist
registry remains separate and must be included in the residual lifecycle audit.

Symbols use two required owners. `Symbols` owns 27 primitive, well-known, storage and
prototype declarations, and `SymbolAccessors` owns the nine class-accessor registry
declarations. The primitive class is emitted before comparer/iterator consumers; runtime
storage and prototype bodies arrive later. Class-accessor signatures are available before
generic index dispatch, the runtime initializer creates the registry, and late body emission
completes its helpers. Accessor completion checks both emitted phases as well as all handles;
failed completion remains repairable. Family helpers take the owners and explicit undefined,
storage, descriptor, function-cache, string-conversion and error dependencies. The prototype
toString/valueOf helper builders stay local instead of duplicating metadata on the root.
Completion freezes compiler metadata only: guest Symbol.for registries, symbol-keyed object
storage and the six-slot class registry remain mutable and isolated per output. Guest class
initializers can register methods and accessors after runtime emission. Generic owner/method
closing, inherited lookup, foreign-symbol recognition and generated method order are unchanged.

WeakMap, WeakSet and WeakRef have independent optional components selected once by
orchestration. Their six, five and three declarations include the validation helpers.
`FinalizationRegistry` always owns the per-output poke table; its optional implementation
owns the entry type, constructor, suppression method and three operations. Entry metadata
is published before runtime-class emission, while operations are emitted before generic
property dispatch. The three entry field builders are local to entry-type construction.
Family helpers receive these owners and explicit poke-table/undefined dependencies; generic
dispatch uses provided component availability, preserving WeakMap's shared has/delete
preference when both weak collections are enabled. Completion validates required metadata
before freezing the optional implementation, so failed completion remains repairable.
Compiler metadata freezes after emission; guest weak tables, queues and registrations remain
mutable and isolated per output. Finalizer IL, suppression and deployment rules are unchanged.

Record storage uses the required `Records` component for the published compact-marker interface
and thirteen layout registries. `Scalars` is selected by `UsesJSON || UsesCompactObjectRecords`
and owns five scalar-record declarations. Empty registries preserve ordinary optional-layout
lookups when storage is absent. Registration stays at the original declaration sites, including
forward uses before type finalization; public dictionary views cannot mutate those registries.
Completion checks all selected declarations, four inline arities and ten getters, correlated
layout keys and each layout's declared field count before freezing either owner. Typed JSON
records may have more than four fields; compact records retain their one-to-four-field limit and
recursive self-field types. Missing final slots therefore cannot silently appear complete.
Family emitters receive explicit IHasFields contracts, shape maps, recursive-field facts and
undefined metadata. Guest slots, per-instance materialization and per-output weak tables remain
mutable and isolated when one emitter is reused.

`JsonShapes` separately owns the later compiler registry for shape fields on `$Program`.
Ordinal fingerprints reuse the same field; first registration retains its insertion-count name
and assembly/static visibility. This registry stays writable after `EmitAll`, through function,
method and entry-point emission, and completes in both single-file and module finalization paths.
Timing and hosting use those same boundaries. Compiler completion freezes declarations while
generated shape values remain lazy and mutable. This registry is distinct from the JSON guest
string-identity table. Generic Object/invocation/iterator operations, other compiler registries,
and the residual construction and shared-infrastructure audit remain required under #1599.

Array operations use the required `ArrayOperations` component for 109 declarations: construction
and static helpers, ordinary and specialized operations, prototype population, bound-method
dispatch, the live array iterator constructor, and array-like receiver/callback context. Like
storage, these helpers are emitted even for minimal tree-shaken programs. Bound-method invocation,
prototype population, and array-like materializers/loaders are declared before their consumers
and filled in later through the same handles. Declaration-only and operation-only helpers accept
`EmittedArrayOperationsRuntime` directly; storage, invocation, descriptors, and coercion remain
separate dependencies. The original and lazy array-like receiver paths share one checked handle
for the existing thread-static field, without duplicate metadata storage. `EmitAll` completes
operations after runtime and bound-method finalization. Remaining family metadata and the
construction, registry and shared-infrastructure audit continue under #1599.

Node crypto uses `Crypto` / `RequireCrypto()` for its 105 hash/cipher constructors, module helpers,
digest/encoding primitives, scrypt, signing, key exchange, KeyObject, and X509 declarations. `EmitAll`
starts it only for `UsesCrypto`, before the primitives and value types, and completes it after the
deferred Sign/Verify, DH/ECDH, and bound-method bodies and types are finalized. DH/ECDH type handles
and their forward-declared `GetMember` methods have a single source in the component. Crypto-only
helpers accept `EmittedCryptoRuntime` directly; Buffer, Promise, and generic property/invocation
helpers remain separate dependencies. X509 constructor dispatch checks feature availability
explicitly so a user class still resolves when crypto is absent.

Crypto construction values now stay within one `EmitAll` invocation, replacing 65 retained emitter
fields. Hash/HMAC, cipher/decipher and DH/ECDH builders receive immutable construction records;
Sign/Verify reuse their existing construction records. Six early-declaration/finalization pairs
pass those values explicitly, preserving forward declarations and emission order. The EC padding
and X509 public-key helpers are returned to their callers and passed directly to consumers.
The 105 checked declarations reject duplicate assignment, validate completion and freeze writes.
Hosted and standalone reuse tests verify assembly ownership, saved IL and active operations from
earlier emissions. The shared built-in module registry still owns crypto named-import wrappers
and aliases; its contract and the full residual-state audit remain required under #1599.

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
does not require client declarations. Eleven BCL types are resolved in their original order into an
immutable construction value local to HTTP/Fetch emission. Six helper boundaries receive it explicitly;
direct fetch emission without construction metadata takes the same rejection path, independently of
any earlier emission by that emitter.

Completion validates each parent's handles before completing its child, then freezes the parent.
Missing declarations name the handle and leave completion retryable; duplicate assignments cannot
replace declarations, and a completed parent cannot enable a child later. All 15 former flat properties
and 28 emitter fields are removed, including the duplicate
Headers setter alias. The dispatch helper field has one checked handle instead of a reflection lookup;
the four cached-client fields also live in the client component. Fetch-only helpers take the relevant
component directly. HTTP/net/TLS, Promise, streams, Buffer/ArrayBuffer, scheduling, and generic
property/coercion helpers retain their owners. Construction inputs and method-local helpers remain
local to emission. Declaration order, feature implications, emitted signatures, and cache/cookie behavior are
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
three BCL construction types (`List<object>`, `TaskCompletionSource<object>`, and its queue) now
form an immutable value local to readable-stream emission, passed explicitly to twelve helpers.
All 45 checked Web stream declarations reject duplicate assignment before completion. Hosted and
standalone reuse tests verify saved IL, current-assembly ownership, buffered values and earlier
pending reads settled by enqueue, close and error after subsequent emissions. Method-local
declaration builders and shared dispatch registries remain within the full #1599 ownership audit.
No migrated flat aliases or emitter-held Web stream construction handles remain.

Six backing fields are assembly-visible because peer emitted classes access them: writable
`_state`, `_storedError`, `_writer`, `_highWaterMark`, and readable `_locked`, `_reader`.
This repairs the six existing field-access IL-verifier errors in controller `Error`, writer
`ReleaseLock`/`DesiredSize`, and reader `ReleaseLock`. Other storage remains private. Generated public
signatures and method bodies are unchanged; standalone and hosted tests verify these
peer accesses as well as the component lifecycle.

Filesystem data and synchronous I/O use optional `EmittedFileSystemRuntime` for 78 checked
handles: 60 former flat properties plus eighteen emitter-held declarations. The component
starts only for `UsesFs`; ordinary Node/Web streams, Buffer, HTTP, workers, and hosted output
do not select it by themselves. Both `fs` and `fs/promises` keep their existing implications
for Buffer, Node streams, Promise, and scheduling support.

The component owns synchronous file operations, encoding and raw-stat helpers, file-descriptor
access, directory/Dirent constructors and backing fields, Stats metadata/storage, and both
platforms' native hard-link declarations. Twenty-seven helpers receive it directly. Cross-feature
consumers retain their error, coercion, Buffer, function-wrapper, Promise, and event-loop
metadata. Descriptor types and directory entries still precede their consumers; Stats keeps
its post-`CreateType` type/constructor resolution, and low-level/encoding helpers still precede
synchronous and asynchronous wrappers. Completion follows runtime finalization and rejects
missing declarations or subsequent writes. No migrated flat aliases or emitter-held copies
remain; feature selection, generated public signatures, native imports, and method bodies
are preserved.

The synchronous value-import wrappers are emitted once after their filesystem declarations.
The `fs.lstatSync` registry entry refers to the single `Fs_lstatSync_Wrapper` declaration in
that assembly; hosted and standalone emitter reuse keep earlier exports independent.

Local descriptor table fields and builders, BCL lookups, and the shared built-in module registry
remain with their existing construction infrastructure for the final ownership audit.

Filesystem async I/O uses optional `EmittedFileSystemAsyncRuntime` for 26 fixed declarations
and 21 Promise-wrapper declarations. It replaces 22 flat method handles, the mutable wrapper
registry, and four emitter-held background-operation handles. The same `UsesFs` gate selects
both filesystem components, including for synchronous-only `fs` imports; neither hosted output
nor unrelated async/Promise use enables filesystem I/O by itself.

The component owns async operations, the namespace accessor, background worker construction,
dispatch and unref handles, and a stable read-only view of the Promise-wrapper registry. Internal
registration rejects null methods, unknown names, and duplicates; checked lookup supports
forward calls. Completion validates every fixed handle and required wrapper, stays retryable
after missing declarations, and freezes assignments and registration. The namespace keeps its
existing wrapper declaration order. Seven helpers take the async component directly, alongside
explicit event-loop or Promise metadata where needed. Synchronous helpers and shared module
registration retain their existing owners.

Background closure creation, unref continuation emission, operation bodies, Promise wrappers,
and namespace emission keep their order. Worker exception unwrapping, event-loop Ref/Unref,
the existing callback grace period, generated signatures, and method bodies are preserved.
Method-local closure fields, the removal implementation, intermediate unref methods, and BCL
construction lookups remain local to their emission helpers for the final #1599 audit.

Filesystem stream metadata uses optional `EmittedFileSystemStreamRuntime`, also enabled by
`UsesFs`. It owns 22 declarations: the read/write stream types, their 14 private storage fields,
two constructors, the cross-type Write/End declarations, and two runtime factories. This removes
two flat properties and 18 emitter fields; factories use the checked constructors instead of
rediscovering them through reflection. Node streams alone still leave this component absent.

Nine helpers take this component directly with explicit Node stream, EventEmitter, filesystem,
or Buffer dependencies as needed. The read type continues to inherit from `$Readable`; the
write type inherits from `$EventEmitter`. Early type and field declaration, deferred Write/End
bodies, and later `$Runtime` factories retain their order. Write/End bodies still precede the
read Pipe body. Completion validates every declaration and freezes writes after finalization.
Listener replay, ranges/chunking, descriptor ownership, close behavior, and both filesystem
and ordinary Writable pipe paths keep their existing behavior. Method-local property/accessor
builders and BCL lookups remain local construction state for the final ownership audit.

Filesystem watchers use optional `EmittedFileSystemWatcherRuntime`, selected by the same `UsesFs`
gate. Its 31 checked declarations replace three flat factory properties and 28 emitter fields:
the filesystem and stat watcher types, constructors, private storage, event/poll and close methods,
both scheduled callback closures, the factories, and the emitted stat-watcher registry field.
Thirteen helpers accept explicit watcher, EventEmitter, event-loop, or filesystem dependencies.
The two callback-accepting factories retain shared function and object-access metadata.

Closures are still emitted before their watcher types; the runtime registry and factories follow
later. Completion validates every declaration and freezes writes after runtime finalization.
Volatile closed-state access, event-loop scheduling and Ref/Unref, callback argument order, polling
error handling, path normalization, and lazy guest registry initialization are preserved. The
component owns the registry's field declaration; the generated dictionary retains its runtime
lifecycle. Method-local builders and BCL reflection lookups remain local construction state for
the final #1599 ownership audit. No migrated flat aliases or emitter-held copies remain.

Process metadata uses required `EmittedProcessRuntime` for 70 declarations: process-object and
value helpers, direct stdio operations, runtime state, the deferred event closure, and native
process-identity imports. It also owns optional `EmittedProcessStreamRuntime` for six stream
singleton getters/cache fields and `EmittedHostedProcessRuntime` for two hosted lifecycle
methods. Together these replace 56 flat properties and 22 emitter-held declarations. Twenty-seven
helpers accept explicit process, EventEmitter, or event-loop metadata; helpers with shared
function, object, error, or child-process dependencies retain the broader runtime context.

The process owner is present even in minimal output. `UsesNodeStreams` alone selects its stream
group; hosted emission selects its hosted group. Availability checks replace the old null-handle
sentinels in process instance getters and module exports, preserving their null-emission fallback
when streams are absent. Enabled groups still reject reads of undeclared handles. This also keeps
the eager stdlib process shim usable when downstream code only needs non-stream APIs.

The early process-object declaration and monotonic uptime baseline retain their positions.
Cyclic signatures still precede value helpers, the deferred closure, the process type, and late
bodies. Completion validates required handles and both enabled optional groups before freezing
either group, so a missing declaration leaves all unfinished groups writable for retry. Completed
groups and the parent reject subsequent writes and replacement.

Guest singleton/cache and signal-registration state, process/stdio behavior, hosted lifecycle,
event-loop integration, native imports, signatures, and method bodies are preserved. Local
process-info and closure builders, environment/report construction state, BCL reflection lookups,
and signal-name tables retain their construction scope for the final #1599 ownership audit.

Child-process metadata uses optional `EmittedChildProcessRuntime`, selected by `UsesChildProcess`.
It owns 58 emitted declarations and 12 family-specific BCL method references, replacing 13 flat
properties and 57 emitter fields. The context and push-closure types, captured worker results,
dispatch methods, output helpers, and process-ownership registry all have checked access and one
completion boundary. Twenty-eight helpers accept explicit child-process, EventEmitter, event-loop,
Node-stream, or Buffer dependencies; shared invocation, object-factory, and module-registry helpers
retain `EmittedRuntime`.

The feature detector still implies streams, Buffer, and Promise for child-process support. Minimal
and process-only output leave the component absent. Entry-point cleanup, hosted disposal, and
process lifecycle emission check that availability before requesting the termination declaration.
Registry fields and initialization keep their early positions; context signatures still precede
worker bodies and dispatch wiring. Completion validates every handle, including cached BCL
references, after runtime finalization and rejects later writes.

The generated ownership registry, process-tree termination, stream pumping, callback ordering,
event-loop references, output limits, encodings, and timeout behavior are preserved. The `fork`
bridge retains its existing soft dependency and standalone error contract. Method-local builders
and reflection lookups remain scoped construction locals; the guest registry retains its runtime
lifecycle. No migrated flat aliases or emitter-held handle copies remain.

OS helpers use optional `EmittedOsRuntime`, selected by `UsesOs`, for `freemem`, `loadavg`, and
`networkInterfaces`. Four emission helpers accept the component directly. Module calls use checked
declarations; completion validates all three methods after runtime finalization and freezes writes.
Other OS APIs continue to emit inline through `OsModuleEmitter`. Method-local BCL reflection handles
remain scoped construction locals. Memory queries and the existing compiled load-average and
network-interface behavior, emitted signatures, feature selection, and standalone dependencies are
unchanged. No flat OS aliases remain.

Console uses required `EmittedConsoleRuntime` for logging, formatting, assertions, inspection,
grouping, tracing, timers, and counters. Its 32 checked declarations include the early group-level
field and the later timer/count dictionary fields. Those dictionaries remain lazy guest state;
their declaration order and emitted initialization are unchanged. Ten console-only emission helpers
and the six shared call helpers accept the component directly, including the call path used by async
and generator expressions. Completion validates every declaration after runtime finalization and
freezes writes. Shared coercion dependencies remain on the runtime until their own migration;
method-local BCL lookups remain construction locals. No flat console aliases or emitter-held copies
remain. Output streams, formatting, argument suspension, and standalone behavior are unchanged.

Inspection uses required `EmittedInspectionRuntime` for the three mutually recursive helpers used
by `console.dir`. All signatures are declared before console extensions and their bodies are emitted
later, preserving forward calls. Five inspection helpers and the console-dir emitter accept explicit
component dependencies. Completion validates all three declarations after runtime finalization and
freezes writes. Formatting, recursion/depth boundaries, emitted signatures, and standalone dependencies
are unchanged. The `util` module remains in `stdlib/node/util.ts`; method-local BCL reflection stays
scoped construction state. No flat inspection aliases or emitter-held copies remain.

Text encoding uses optional `EmittedTextEncodingRuntime`, selected by `UsesTextEncoding`, for seven
encoder, decoder, and detached decode-wrapper declarations. Three emission helpers accept the
component and `EmittedBufferRuntime` directly. Constructors, type access, and invocation dispatch use
checked access under their existing feature gates; text encoding still implies Buffer. Completion
validates declarations after runtime finalization and freezes writes. Emitted type/field/method order,
encoding and wrapper behavior, argument evaluation, and standalone dependencies are unchanged.
Method-local wrapper fields, constructors, getters, and BCL lookups remain scoped construction locals.
No flat text-encoding aliases or emitter-held copies remain.

Readline uses optional `EmittedReadlineRuntime`, selected by `UsesReadline`, for seven declarations
previously split between the flat holder and emitter fields. Thirteen helpers accept the component
with explicit EventEmitter or invocation dependencies where needed. The interface and constructor
remain available before runtime helper emission; Question and type finalization still follow
InvokeValue emission. Completion validates all declarations after finalization and freezes writes.
Prompt/closed/paused state, repeated event behavior, synchronous input and callbacks, emitted order,
and standalone dependencies are unchanged. The TS readline facade remains in the standard library;
method-local builders and BCL reflection remain scoped construction locals. No migrated aliases or
emitter-held copies remain.

Module loading uses required `EmittedModuleRuntime` for the registry field and its initialization
and registration methods, plus optional CommonJS (`UsesCjsRequire`) and dynamic-import
(`UsesDynamicImport`) components. They own fourteen checked declarations, including seven former
CommonJS emitter fields. Completion validates every enabled group before freezing any of them.
Four CommonJS helpers, four module helpers, and two property-dispatch helpers accept their owning
components; the dynamic-import wrapper retains orchestration across module, Promise, and event-loop
families. The registry remains unconditional for static multi-module bundles. CommonJS type creation,
exports write-through, lazy registry state, relative resolution, hosted import dispatch, emitted
order, and output dependencies are unchanged. Promise wrapping retains its existing owner;
method-local BCL references and property/constructor builders remain scoped construction locals.
No migrated flat aliases or CommonJS emitter-held copies remain.

`EmittedBuiltInModuleRegistry` owns the shared callable-export index for each compilation.
`EmitAll` snapshots feature selection into required export keys before emission; timers are
unconditional and optional families reserve only their selected exports. Registration requires
one non-null declaration from that emitted module per key. Distinct aliases may intentionally
index the same family-owned method, including DNS result-order and TLS Server exports.
Selected declarations are available before bodies and types are completed; an undeclared selected
export fails checked lookup, while unselected/unknown optional exports preserve generic consumer
fallthrough. Completion after all runtime families validates every selected key and freezes writes.
The registry retains no mutable feature set, and no parallel index remains on `EmittedRuntime`.
Generic import/require consumers use optional lookup; required TLS identity calls use `Require`.
The existing declaration order, method bodies, export surface and deployment requirements remain
unchanged. Other module/type registries and the complete residual audit remain separate work.

VM uses optional `EmittedVmRuntime`, selected by `UsesVm`, for twelve checked method declarations.
Thirteen VM emission helpers take this component; memory measurement also takes the Promise
component. A module-scoped registration callback keeps the eight exported callable registrations
at their original declaration points, before their bodies are emitted. The shared built-in-module
registry remains an orchestration-owned dispatch index for generic import/require consumers;
it does not replace the family's declaration ownership or freeze boundary. Script, constants,
context, compiled-function, and source-text/synthetic-module consumers use checked VM metadata.
VM still records its SharpTS runtime requirement and loads the interpreter by reflection, without
adding a guest assembly reference. Normal CLI output co-locates that runtime; standalone output
preserves its explicit missing-runtime error. Emission order, existing cross-runtime behavior,
and Promise wrapping are unchanged. BCL reflection handles remain method-local construction
values; no migrated flat aliases or emitter-held VM copies remain.

The source-execution bridge uses optional `EmittedSourceExecutionRuntime`, selected by
`UsesSourceExecution`, for `RunJson` and `ConfigureUntrustedProcess`. Its emission helper takes
the component and a module-scoped registration callback, preserving both declarations' early
registration in the shared dispatch index. Completion checks and freezes both handles. Consumers
use checked metadata; the late-bound service name and generic reflection helper remain unchanged.
The bridge still requires `FullDependencyClosure | ManagedCompilerHost` in addition to the runtime
assembly, so normal CLI output deploys the managed compiler closure and standalone output retains
its missing-runtime diagnostics. Process configuration behavior is unchanged and tested in isolated
subprocesses. No migrated flat aliases or emitter-held bridge handles remain.

AbortController/AbortSignal uses optional `EmittedAbortRuntime`, selected by `UsesAbortController`,
for nineteen checked declarations: seventeen methods and the namespace field/populate method.
The namespace field is declared early for instance checks; event dispatch precedes controller and
signal helpers, which precede dynamic property and stream wiring; namespace population is emitted
later. Completion validates and freezes the whole family after these phases. Sixteen Abort helpers
take explicit metadata dependencies; event dispatch receives the four invocation handles it needs.
The generic namespace wrapper takes its function-cache method explicitly, while its callers retain
orchestration across AbortSignal and Intl. All other consumers use checked Abort handles.
`AbortSignal.any` retains its precise runtime-requirement flag and late-bound reflection call;
ordinary controllers and their implied stream/fetch support keep existing standalone behavior.
Signal dictionaries, cancellation sources, listener order, namespace identity, and wrapper behavior
are unchanged. BCL reflection and body locals remain scoped; NodeStreams still owns its abort
callback metadata. No migrated flat aliases or Abort emitter-held copies remain.

AsyncLocalStorage uses optional `EmittedAsyncLocalStorageRuntime`, selected by
`UsesAsyncLocalStorage`, for its checked constructor declaration. The class is still emitted
after TSFunction and created before family completion. Four emission helpers take explicit
component/function dependencies; the primitive factory reads the constructor through its checked
owner. The class builder, AsyncLocal field, enabled field, five method builders, and BCL reflection
handles remain scoped construction locals. Run/Exit keep their existing try/finally restoration,
callback casts, disabled behavior, and async context propagation. The TypeScript facade and
standalone deployment are unchanged; no flat constructor alias or emitter-held copy remains.

Intl uses optional `EmittedIntlRuntime`, selected by `UsesIntl`, for eight factories and the
namespace field/populate method. The field is declared early, factories follow in their existing
order, and lazy namespace population precedes completion. Factory emission and the two shared
AbortSignal/Intl namespace orchestrators take explicit component dependencies. Consumers check
availability before loading the namespace and use checked handles for direct construction.
Emitted `CreateIntl*` names retain the constructor-dispatch contract. The shared reflection helper,
runtime requirement, wrapper identity/member order, and instance dispatch are unchanged; no flat
Intl aliases or emitter-held copies remain. BCL reflection and body locals stay scoped.

Performance timing and terminal checks have separate optional owners:
`EmittedPerformanceRuntime.Now` is selected by `UsesPerf`, and `EmittedTtyRuntime.Isatty`
by `UsesTty`. Each validates its declaration before freezing at runtime completion. The two
performance helpers take the performance component, while the TTY helper takes its component
and explicit number-coercion method. Primitive consumers use the checked owners. The three lazy
stopwatch fields remain scoped construction values; TTY retains its declaration assignment after
body emission. Clock initialization, descriptor coercion/redirection checks, emitted names,
standalone deployment, and the TypeScript facades are unchanged. No flat aliases or emitter-held
copies remain; BCL references and body locals retain their scope.

Node-style errors are shared by filesystem and process emission through the required
`EmittedNodeErrorRuntime`. Its six checked declarations cover the created error type, constructor,
three getters, and the later conversion helper. Fifteen helpers take this owner and explicit
filesystem or exception-creation dependencies. The four error fields and errno getter remain
scoped to class construction. Completion validates the class and conversion stages before freezing.
Constructor formatting, exception-data markers, errno behavior, descriptor lifetimes, process
error paths, declaration order, and standalone dependencies are unchanged. No flat aliases or
emitter-held error fields remain; BCL references retain their original scope.

Atomics has an optional `EmittedAtomicsRuntime`, selected by the existing
`HasAnyTypedArray` gate. It owns fifteen operation declarations, including the optimized signed/
unsigned add and discarded-increment helpers. Twenty-one emission helpers take explicit typed-array
and error-construction dependencies. The five private locking/conversion/update methods remain
scoped construction values, passed to their consumers; the conversion emitter also receives its
method explicitly. Operations are recorded after their bodies and validated before completion.
Static dispatch, optimized calls, and pause function values use the checked owner. Shared guest-error
helpers accept explicit constructor/wrapper declarations while retaining their existing root-based
entry points. Emitted order, inlining, numeric coercion, bounds and pause errors, shared-buffer locks,
current wait/notify behavior, and standalone dependencies are unchanged. No flat aliases or private
Atomics method copies remain on the emitter; BCL/Unsafe references and body locals retain their scope.

Cluster has an optional `EmittedClusterRuntime`, selected by the existing `UsesCluster`
gate. It owns the `Fork` and `Invoke` declarations; all three module call sites use its checked
accessor. The three helper boundaries take the cluster owner, event-loop metadata, and entry-script
configuration only where needed. `RuntimeEmitter.EntryModulePath` remains compiler configuration,
passed explicitly when emitting the fork body. Both handles are recorded after body emission and
validated at completion. Late-bound bridge dispatch, worker event-loop delegates, live scheduling
policy, namespace method values, emitted IL, and runtime deployment are unchanged. BCL reflection
references and body locals remain scoped; no flat cluster aliases remain.

Message channels have required `EmittedMessageChannelRuntime` and `EmittedMessagePortRuntime`
owners for seventeen distinct declarations, replacing three flat properties and fifteen emitter
fields. Thirteen emission helpers receive explicit family, event-emitter, event-loop, clone, and
runtime-type dependencies as needed. The deferred worker receive body takes the port owner and
undefined instance explicitly; its own declaration and reflection-cache state belong to the worker
owner. Port declarations are readable before body/type completion, while channel declarations keep
their existing later publication. Completion validates both owners before freezing either. Peer-field
visibility, clone-error sentinel, reflected worker bridge ABI, queue ordering, volatile notification,
cross-thread keep-alive behavior, and standalone deployment remain unchanged. Channel property
backing fields/getters and BCL reflection references remain scoped construction values.

Workers have a required `EmittedWorkerRuntime` owner for nine method declarations and two
foreign-receive cache fields. Four emission helpers take explicit worker, event-loop, port, and
undefined dependencies. The receive method remains forward-declared before MessagePort emission,
with its body filled before RuntimeClass finalization. The former `TSWorkerType` was a BCL Object
alias, so its sole consumer now uses `TypeProvider.Object`; no generated worker type is introduced.
`ConfigureWorkerContext`/`ClearWorkerContext` and their four realm-local fields stay scoped to
module emission. Reflected bridge names, hit/miss caching, worker factory delegates, managed
dependency deployment, and environment/undefined behavior retain their existing contracts.
The mixed worker helper orchestrator still coordinates buffer, typed-array, Atomics, and structured
clone emission; structured-clone metadata has its own component as described below.

BroadcastChannel has an optional `EmittedBroadcastChannelRuntime` owner under the existing
`UsesBroadcastChannel` gate. Thirteen shared declarations replace two flat aliases and seventeen
emitter-held builders: four method copies used only during their own construction become scoped
locals, while type/constructor/drain and ten fields belong to the component. Two mutable BCL
dictionary-type caches also become scoped values. Twelve helpers receive only their family,
event-emitter, event-loop, function, and clone dependencies. Declarations retain their original
construction order, and external consumers read checked handles after class emission. Ordinal
registry lookup, per-subscriber cloning, receiver-side errors, queued delivery after close,
property/listener callbacks, and event-loop reference behavior remain unchanged.

Structured clone has a required `EmittedStructuredCloneRuntime` component containing the public
clone method and the dedicated `$DataCloneError` type and constructor. The exception is declared
and created before `$Runtime`; the clone method is published after its body, and all three handles
are checked and frozen at completion. The private recursive core remains local to construction.
The clone and error emitters receive explicit dependencies; optional binary, Date, RegExp, and
Buffer inputs preserve their existing gates. Readonly, scoped object/error/Date/RegExp inputs bridge
peer families awaiting migration; they retain no emitter state and remain part of the final audit.
Numeric-array materialization uses the ArrayStorage component through a narrow shared helper;
other generic-consumption adapters and Date's construction handle remain for their own phases.
Global calls, exception normalization, MessagePort, and BroadcastChannel use the checked component.
The existing wrapper signature and ignored transfer argument, foreign-runtime pass-through,
recursive data copying, and receiver-side clone-error behavior remain unchanged.

Core string operations and their prototype metadata live in required `EmittedStringRuntime`:
43 method declarations and one prototype field replace 44 unchecked flat properties. The prototype
field and population shell are declared early; operation bodies and prototype wiring remain in their
original order, and completion validates and freezes every handle. Primitive intrinsic signatures,
inlining flags, local integer/slow-path helpers, method names, descriptor flags, and function caches
are preserved. Core emitters take the component and explicit peer declarations. The mixed stub
orchestrator keeps Object and Array helpers with their existing owners.

Prototype descriptor installation has narrow shared helpers with immutable scoped inputs; existing
entry points adapt remaining peer families. String population receives scoped descriptor, function,
symbol, and optional RegExp protocol inputs. Optional null protocol methods still skip wiring;
search helpers preserve the existing optional RegExp brand check. These inputs retain no emitter
state and remain subject to the final peer-ownership audit. String coercion, templates/String.raw,
boxed-primitive unwrapping, and RegExp-aware protocols remain separate later families.

Template-literal metadata lives in required `EmittedTemplateRuntime`. Its seven declarations
cover the created strings-list type, constructor and raw getter, concatenation, String.raw, and
both tag invocation forms. The list class is created before runtime helper bodies; completion
validates and freezes all handles. Five emitters take the component and explicit coercion,
property, freezing, invocation, undefined and error declarations. The list's field and builders
remain local construction values. Direct, value, async and generator consumers use checked
handles while retaining the existing argument shapes, freezing and receiver behavior. General
coercion and function/object metadata remain with their families for later migration and audit.

String conversion metadata lives in required `EmittedStringCoercionRuntime`: display Stringify,
language ToJsString, String(value), implicit StringifyCoerce, and typed integer concatenation.
The three forward declarations retain their separate early positions; later body emission and
completion preserve those references. Five emitters take the component and explicit dependencies.
ToJsString receives existing array components plus immutable scoped inputs for object, descriptor,
function, symbol and global peers; the RegExp type is supplied only under its existing feature gate.
Those inputs are never retained as emitter state and remain part of the final ownership audit.
Number formatting stays with its own family. The integer formatting buffer and span/BCL handles
remain construction-local, with unchanged guest thread-static storage and optimization flags.
Display formatting, language conversion, Symbol errors and String(value)'s Symbol exception keep
their distinct behavior, including live prototype and coercion-hook lookups.

Primitive-wrapper metadata lives in required `EmittedBoxedPrimitiveRuntime`. Its six declarations
cover wrapper creation, foreign-eval normalization, ToObject, brand checks, string receiver
unwrapping, and default-hint primitive conversion. ToObject, UnwrapIfBoxed and the brand-check
shell retain their separate early declaration stages. Eight emitters accept the component and
explicit dependencies; object/descriptor/prototype peers remain immutable construction-scoped
inputs. BigInt prototype and Date conversion inputs are supplied only under their existing feature
gates. String exotic descriptors, prototype population order, foreign undefined recognition,
wrapper identity, coercion hooks and guest errors remain unchanged. No peer input or BCL handle is
retained as duplicate emitter state. The remaining peer families and these scoped boundaries stay
in the final metadata-ownership audit.

Number parsing, formatting and prototype metadata live in required `EmittedNumberRuntime`.
Its 24 declarations include the early FormatNumber shell, prototype field/population shell,
cached UInt64 formatting callback and field, typed/general parsers and formatters, predicates,
and radix helpers. Completion validates and freezes these declarations. Number body emitters
take the component and narrow dependencies; method and prototype orchestration use immutable
scoped peer inputs and the shared descriptor-installation helpers. The exact fixed formatter's
BigInteger fallback and prototype valueOf builder remain local to their callers. Cached delegate
initialization, native signatures, optimization flags, parsing/rounding behavior and prototype
identity remain unchanged. General numeric coercion has its own owner, and scoped BCL/descriptor
inputs remain in the final audit.

During emission, each handle becomes readable as soon as its declaration is assigned, so forward
references do not require a method body to exist yet. An early read names the missing declaration.
Completion is an orchestration boundary, not an IL verifier: body emission and type finalization
must finish before that boundary. Preserve existing emission order when migrating a family.

Math metadata lives in required `EmittedMathRuntime`: singleton storage/population, Random,
exact summation and 35 numeric adapters. Its 39 declarations retain their original early
singleton, adapter/Random and late exact-sum stages before completion validates and freezes
them. Adapters receive only their numeric conversion dependency; exact summation uses
immutable iterator, symbol, invocation and error inputs. Shared Math/JSON/Reflect singleton
installation takes immutable descriptor/function/symbol inputs, using the existing descriptor
installation helper. Canonical Math method enumeration and lookup receive the Math component.
The early descriptor fallback keeps its historical absent sumPrecise target; populated PDS
descriptors contain the actual late-declared method. Random's private field, exact-unit helper
builders and BCL/Half metadata remain local construction values. Numeric algorithms, coercion
order, cached function identity, descriptors and initialization order remain unchanged.

BigInt has a required `EmittedBigIntRuntime` owner for its prototype field/population shell and
binary64 conversion helper, plus an optional `EmittedBigIntImplementation` with 24 declarations.
Implementation availability follows UsesBigInt and is established before the early strict ToBigInt
declaration used by DataView. Callable conversion and strict-conversion bodies retain their later
stage; prototype population remains feature-gated. Completion checks the required declarations
before validating and freezing the enabled implementation. Fifteen family emission boundaries
take these owners and explicit primitive-conversion, error, descriptor and prototype inputs.
ToPrimitive, prototype helpers and BCL metadata stay local to construction. DataView adapters and
static dispatch use explicit implementation availability. Binary64 rounding, width/value coercion
order, callable versus strict conversion, prototype descriptors, function identity and declaration
order remain unchanged. These components own compiler metadata; guest prototypes remain mutable.

Truthiness and Boolean prototype metadata live in required `EmittedBooleanRuntime`. Its three
checked declarations retain the early IsTruthy signature used by RegExp, later truthiness body,
prototype field/population shell and late prototype helpers. Completion validates the declarations
and freezes metadata assignments. Five family emitters receive the owner or exact declarations
with explicit Undefined, receiver, guest-error and immutable descriptor/prototype inputs. The
truthiness helper uses BCL BigInteger metadata without requiring optional BigInt operations and
does not invoke object conversion hooks. Local toString/valueOf builders remain local; prototype
descriptors, function identity, receiver branding and mutable guest prototype state are unchanged.
The broader construction/shared-infrastructure audit remains separate.

General numeric conversion metadata lives in required `EmittedNumericCoercionRuntime`. Its five
checked declarations are ToNumber, ConvertToNumber, JsNumberToInt32, JsToInt32 and
ToIntegerOrInfinity. ToNumber and JsToInt32 remain early forward declarations for RegExp;
later declarations and bodies keep their existing order. Five emission boundaries take the owner
and immutable inputs for primitive/object conversion, descriptors, invocation and guest errors.
Explicit Number conversion receives the required BigInt numeric adapter independently of optional
BigInt operations. The native int32 body and prefixed-integer parser retain their local BCL inputs.
Completion validates all five declarations and freezes metadata assignments. Native signatures,
inlining, conversion hooks, abrupt completions, undefined defaults and generated instructions stay
unchanged. TSFunction's indirect ToNumber lookup retains its generated name and per-assembly cache.

Cooperative cancellation lives in required `EmittedCancellationRuntime`. Its public per-assembly
flag is declared at the original early runtime-field position; the check and exception factory
retain their later method positions. Completion requires all three checked declarations and both
body-emission markers, then freezes assignments. Failed completion leaves the owner repairable.
Three family helpers take the owner directly. Invocation guards receive the declared check;
guest loops use the checked flag/factory and preserve volatile reads followed by a separate throw,
including accumulator flushes on the cold path. Runtime-free emission still omits cancellation.
The public `_cancelRequested` reflection contract and exception type/message remain unchanged.

Event-loop construction and its Run/WaitForTask helpers receive the event-loop component and an
explicit nullable cancellation method. Normal orchestration still constructs the event loop before
cancellation methods exist and passes null, preserving the existing omission; supplying a method
to these helpers emits the checks. This phase does not change that scheduling behavior. Hosted
and feature-free assemblies retain independent cancellation state. Class-initializer unwrapping,
regex hoisting, runtime-type construction and the residual ownership audit remain separate work.

Class-definition initialization lives in required `EmittedClassInitializationRuntime`. Its checked
RunDefinition declaration retains the original RunClassDefinition signature and position after
the cancellation helpers. The family emitter receives only the owner; completion requires its
body marker, permits repair after an incomplete attempt, then rejects further writes. Ordinary
and state-machine declarations and expressions all consume the same checked handle. The helper
forces the CLR initializer and unwraps one TypeInitializationException, preserving the original
exception object and the wrapper when its inner exception is absent. CLR initialization caching,
guest evaluation order, emitted instructions and standalone/hosted dependencies remain unchanged.
Regex hoisting, runtime-type construction and the final residual ownership audit remain required.

Regex literal caches use required `EmittedRegexLiteralCacheRuntime`. The compiler snapshots the
analyzer-selected AST nodes by reference identity after defining `$Program`, declares every
`$rx_N` field in the original order, and completes the registry before emitting guest bodies.
An empty selection is explicitly initialized and completed. Null, duplicate, unselected and
late declarations are rejected; incomplete completion remains repairable. Consumers share a
stable read-only view, including ordinary, async and generator emitters and intrinsic fast
paths. Completion freezes metadata registration, while the generated public static object
fields remain mutable for lazy initialization. Escaping literals, stateful test/exec exclusions,
throw-on-evaluation, guest identity and hosted/standalone behavior are unchanged. Shared
construction, registry and residual-state auditing remain separate work under #1599.

The shared `$Runtime` declaration uses required `EmittedRuntimeClass`, exposed through
`EmittedRuntime.RuntimeClass`. Its checked `Type` handle is assigned once in phase one and
remains available to forward consumers. The finalizer receives that owner explicitly, creates
the type at the original boundary after deferred helper bodies, and completes metadata only
when `TypeBuilder.IsCreated()` confirms finalization. Missing/null/duplicate declarations,
uncreated completion and later assignments are rejected; failed completion remains repairable.
All consumers use this owner; the emitter no longer retains `_runtimeTypeBuilder`. Type name,
attributes, base type, member order, cross-family tokens and hosted/standalone behavior remain
unchanged. Removing the last flat type declaration does not finish the construction, registry,
shared-infrastructure or semantic acceptance audit under #1599.

Promise's private `_task` field handle is local to `EmitTSPromiseClass` and passed to its
constructor, task accessors, completion property and display helper. It is needed only until
that generated type is created; the reusable emitter and the public Promise component do not
retain a second construction handle. The existing optional Promise lifecycle, declaration
order, field attributes and generated task identity remain unchanged.

DNS construction handles are scoped to each emission: the result-order field and CNAME
chase helper remain local to `EmitDnsModuleMethods`; the one- and two-argument promise
worker declarations are immutable construction values passed to their wrappers. The async
runner keeps its completion constructor and scheduling method local and returns only the
runner handle. Its event-loop peer and the wrappers' DNS/Promise peers are explicit inputs.
The reusable emitter retains none of these sixteen handles. DNS module declarations are emitted
once, before Promise wrappers capture their synchronous targets. The former later duplicate
pass is removed, so synchronous and Promise consumers use the same methods and result-order
field, and every DNS callback closure type has one definition. The DNS component rejects
duplicate handle assignments and validates all sixteen
Promise-wrapper names, non-null declarations and completion before freezing its registry.
Consumers use checked wrapper lookup, while forward declarations remain readable before their
bodies exist. Reflection exception unwrapping and settlement before event-loop release are
preserved; construction values do not duplicate ownership.

Net construction metadata stays within one `EmitAll` invocation. Socket and server phase-one
emitters return immutable field/method construction values; closure emission returns separate
constructor/run pairs for phase two. Early module factories receive their field inputs, and late
TLS population receives only the Net client and stream fields it needs. BlockList helpers, the
socket error-code helper and the TCP drop-payload helper remain method-local. These values replace
79 retained emitter fields without adding another persistent owner. `EmittedNetRuntime` continues
to own checked public declarations and completion; optional availability, forward declarations,
field visibility and type-creation order are unchanged.

EventEmitter resolves its twelve open-generic BCL collection methods into an immutable local
value at the original initialization point. Seven helpers receive that value explicitly when
resolving methods on the generated listener-list and event-dictionary types. No collection
method handles remain on `RuntimeEmitter`; the required checked EventEmitter component still
owns its twenty-six generated declarations and rejects duplicate assignments before completion.
Declaration/body order, mutually recursive rejection routing, listener ordering, subclass hooks
and deployment are unchanged. Lifecycle and hosted/standalone reuse tests cover earlier listener
collections, callbacks and static defaults after later emissions. Shared BCL resolution and the
error-monitor constant remain within the final ownership audit under #1599.

Zlib construction is local to its emitting methods. Six transform fields form an immutable
construction value, its chunk-conversion helper is passed directly to Write/End, and eight
synchronous input/option/copy helpers are returned into a separate immutable value. None of
these fifteen handles remain on `RuntimeEmitter`. The checked Zlib component still owns its
twenty-four public declarations and rejects missing, null and duplicate declarations; failed
completion can be repaired before metadata is frozen. Declaration/body order, compression
options, stream finalization, Buffer/NodeStream dependencies and deployment are preserved.
Lifecycle and hosted/standalone reuse tests exercise prior assemblies after later emissions.
The kind constants, method-local BCL lookups and shared infrastructure remain in the final
ownership audit under #1599.

Datagram construction stays within one `EmitAll` invocation. Ten socket field builders form an
immutable construction value passed from early type declaration to the deferred receive body;
five event/callback helpers remain local and are passed directly to Bind, Close and Connect.
The checked Dgram component remains the sole owner of public socket, factory, receive-worker
and message-closure declarations. Its setters reject duplicate declarations, and failed
completion still allows missing declarations to be supplied. No construction handles remain
on the emitter. The original declaration/body/finalization order, feature gates, cancellable
receive loop, event scheduling and deployment remain unchanged. Tests cover every declaration,
hosted and standalone emitter reuse, serialized IL and transport behavior. Shared construction
infrastructure and the complete residual-state audit remain required under #1599.

TLS construction likewise stays within one `EmitAll` invocation, replacing 37 retained emitter
fields. Socket and server emission return immutable field/helper values for deferred handshake
and accept bodies. Closure emission returns constructor/callback pairs; the connect closure type
comes from its constructor. Checked socket metadata and the server constructor remain the sole
sources of their type declarations, including finalization. Protocol and certificate-display
helpers stay local to socket construction, and certificate-identity helpers stay local to their
module emitter. `EmittedTlsRuntime` rejects duplicate declarations as well as null and completed
writes; failed completion remains repairable. Existing declaration/body order, visibility,
feature selection, Net field inputs and deployment rules are preserved. The broader construction,
registry, semantic and final ownership audit remains required under #1599.

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

### Built-in module emission strategies

`BuiltInModuleEmitterRegistry` owns the module-name dispatch index for one compiler.
`CreateDefault` registers each strategy and its intentional aliases, then completes
registration before the compiler exposes a context. Registration rejects null strategies,
invalid names and duplicate keys; completion rejects further writes. Lookups preserve
registered instance identity both before and after completion, and unknown names remain
an optional miss. Alias-only primitive names do not require a canonical registration.
The index stores strategies, not generated metadata; strategies resolve per-compilation
handles through the supplied emission context. `EmittedBuiltInModuleRegistry` separately
indexes generated callable declarations. Module strategies keep their ordered export names in immutable arrays boxed once into
cached read-only views. Callers cannot change later compilations through either generic or
non-generic list interfaces; HTTPS shares HTTP's immutable view. Cluster property classification
uses an immutable ordinal set. Strategies retain no generated metadata in these catalogs.
Other compiler registries remain part of the broader ownership audit.

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
