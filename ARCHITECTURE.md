# SharpTS architecture

SharpTS is one TypeScript front end with two execution backends: a tree-walking interpreter and a
.NET IL compiler. This document records stable subsystem boundaries, data flow, and invariants.
Per-file catalogs and current feature coverage belong in source, tests, and [STATUS.md](STATUS.md).

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
| `Parsing/` | Tokens, source locations, AST records, TypeScript/TSX grammar, syntactic lowering | Runtime values or CLR emission |
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
3. embedded TypeScript implementations under `stdlib/`;
4. internal `primitive:` host seams used only by the embedded standard library; and
5. C#/IL-backed built-in modules.

User code imports only public specifiers. `primitive:` modules are private implementation seams.
The user-facing declaration, interpreter export, and compiled emitter for a built-in must describe
the same surface. See [`stdlib/CONTRIBUTING.md`](stdlib/CONTRIBUTING.md).

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

## IL compiler architecture

The compiler analyzes the checked program, defines the required CLR types/members, emits bodies,
finalizes metadata, and serializes PE/PDB output. Definition and body phases are separate because
closures, recursion, inheritance, modules, async state machines, and runtime helpers need stable
metadata handles before all bodies exist.

Key compiler analyses include module bindings, closure/capture shape, runtime feature detection,
typed/local representation opportunities, and hosted output. Optimizations may use static type
facts but must preserve JavaScript object identity, coercion, evaluation order, and exceptions.

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
11. GUI application extension stays within the documented TypeScript API; internal native-provider
    seams have no public compatibility promise.
12. Volatile counts, benchmark snapshots, and exhaustive file lists do not define architecture.

## Where to continue

- [Documentation hub](docs/README.md)
- [Contributor workflow](CONTRIBUTING.md)
- [Implementation status](STATUS.md)
- [Execution modes](docs/execution-modes.md)
- [MSBuild SDK](docs/msbuild-sdk.md)
- [Benchmark methodology](benchmarks/README.md)
