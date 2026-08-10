# SharpTS implementation status

This document is the canonical present-tense capability matrix for SharpTS. It describes public
behavior, not the history of how a feature landed. Detailed usage belongs in the
[documentation hub](docs/README.md), and changes over time belong in Git history and releases.

Legend: ✅ supported, ⚠️ supported with a documented deviation, ❌ not supported.

## 1. Execution engines

SharpTS has one lexer, parser, type checker, project resolver, and declaration pipeline feeding two
execution backends. The interpreter walks the checked syntax tree. The compiler independently
emits .NET IL and an application-specific runtime. Neither backend is implemented in terms of the
other, so shared tests and conformance baselines enforce their parity contract.

| Capability | Interpreter | Compiled IL | Notes |
| --- | :---: | :---: | --- |
| Run a script or module graph | ✅ | ✅ | Compiled output can target a DLL or executable. |
| Async functions, promises, generators, and top-level module work | ✅ | ✅ | Hosted output has additional lifecycle rules. |
| TypeScript-source debugging | — | ✅ | `--debug`/`-g` emits a portable PDB. |
| Project checking and declarations | ✅ | ✅ | These use the shared front end rather than an execution backend. |
| External .NET interop | ✅ | ✅ | Deployment requirements differ; see [.NET types](docs/dotnet-types.md). |
| Node-compatible built-ins | ✅ | ✅ | Per-API ceilings and deviations are documented below and in the API guide. |

The contract is equal observable behavior for supported language and library features. Intentional
deviations—such as compiled indirect `eval`, selected compiled-only Node ceilings, and hosted ABI
rules—must be documented and tested rather than described as unconditional equivalence. See
[Execution modes](docs/execution-modes.md).

## 2. TypeScript language and type system

| Area | Status | Current surface |
| --- | :---: | --- |
| Primitive and special types | ✅ | `string`, `number`, `boolean`, `bigint`, `symbol`, `null`, `undefined`, `void`, `any`, `unknown`, `never`, `object` |
| Structured types | ✅ | Objects, arrays, tuples, readonly forms, index signatures, optional properties, call and construct signatures |
| Type composition | ✅ | Unions, intersections, aliases, literal types, `keyof`, indexed access, mapped/conditional types, `typeof` in type position |
| Generics | ✅ | Generic functions, classes, interfaces, constraints, inference, variance annotations, and variadic tuples |
| Narrowing | ✅ | `typeof`, `instanceof`, discriminants, property checks, user predicates, assertion functions, and control-flow narrowing |
| Classes and interfaces | ✅ | Inheritance, structural compatibility, private/protected branding, abstract members, accessors, static blocks, `#private`, parameter properties |
| Functions | ✅ | Declarations, expressions, arrows, closures, overloads, defaults, rest/spread, explicit `this`, async functions, and generators |
| Enums | ✅ | Numeric, string, heterogeneous, and `const enum` forms |
| Decorators | ✅ | TC39 Stage 3 and legacy experimental decorators; metadata is opt-in. |
| Modules | ✅ | ESM imports/exports, re-exports, dynamic import, CommonJS `require`, `export =`/`import =`, namespaces, ambient modules, augmentation |
| JSX/TSX | ✅ | Automatic and classic runtimes, pragmas, intrinsic/component prop checking; SharpTS defaults to `react-jsx`. |
| Projects | ✅ | `tsconfig` discovery/extends, files/include/exclude, references/build mode, incremental/watch, paths/baseUrl, declaration inputs |
| Declaration output | ✅ | `.d.ts`, declaration-only output, declaration directories, root/out directory mapping |

SharpTS deliberately keeps its own defaults where .NET output makes TypeScript emit settings
irrelevant: `strictNullChecks` defaults on, most other strictness flags default off,
`strictFunctionTypes` defaults off, and `target`/`module` do not select IL behavior.

## 3. JavaScript runtime APIs

| Area | Status | Notes |
| --- | :---: | --- |
| Control flow and exceptions | ✅ | Loops, labels, switch, `try`/`catch`/`finally`, guest `throw`, optional catch binding |
| Operators and destructuring | ✅ | Optional chaining, nullish and logical assignment, spread/rest, array/object patterns |
| Core objects | ✅ | Object/Array/String/Number/Boolean/BigInt/Symbol/Math/JSON/Reflect APIs used by the maintained suite |
| Collections | ✅ | Map, Set, WeakMap, WeakSet, iterators, and async iterators |
| Date, RegExp, and Intl | ✅ | Broad current surface; locale results follow the host .NET globalization data. |
| Binary data | ✅ | ArrayBuffer, SharedArrayBuffer, DataView, typed arrays, Atomics |
| Promises and scheduling | ✅ | Promise combinators, timers, microtasks, abort signals/controllers |
| Web-shaped APIs | ✅ | URL, URLSearchParams, TextEncoder/Decoder, fetch primitives, streams, Blob, FormData |
| `eval` | ⚠️ | Direct lexical eval in the interpreter; compiled mode uses indirect runtime eval and cannot see compiled locals. |
| `Function` constructor | ❌ | Constructing functions from source strings is not supported. |

## 4. Node.js built-in modules

SharpTS recognizes bare and `node:` forms for 34 maintained Node module specifiers. This is a
compatible subset, not a claim to implement every export in the corresponding Node release. The
[Node API guide](docs/node-modules-api.md) is the user reference; breadth and depth work is tracked
on [#1282](https://github.com/nickna/SharpTS/issues/1282).

| Category | Modules | Status and boundaries |
| --- | --- | --- |
| Files, paths, and process | `fs`, `fs/promises`, `path`, `os`, `process`, `tty` | ✅ Core sync/async filesystem operations, streams/watchers, platform/path variants, process lifecycle and stdio. Some POSIX and rejection lifecycle behavior remains mode/platform-specific. |
| Data and utilities | `assert`, `buffer`, `crypto`, `querystring`, `string_decoder`, `url`, `util`, `zlib` | ✅ Broad tested subsets. Crypto is bounded by .NET algorithms; selected advanced key APIs remain interpreter-only. |
| Events and scheduling | `events`, `async_hooks`, `timers`, `timers/promises`, `perf_hooks`, `readline` | ✅ Includes EventEmitter, AsyncLocalStorage, timer promises, performance entries/observer, and readline basics. |
| Streams and networking | `stream`, `stream/promises`, `stream/web`, `http`, `https`, `net`, `tls`, `dgram`, `dns`, `dns/promises` | ✅ HTTP/TCP/TLS/UDP/DNS and stream subsets. Resolver callback timing and a few platform facilities have documented deviations. |
| Processes and isolation | `child_process`, `cluster`, `worker_threads`, `vm` | ⚠️ Main APIs are available, but the implementation uses .NET processes/threads and does not reproduce every Node isolation/resource option. Some compiled operations require the co-located managed runtime and source payload. |

Modules absent from the maintained surface include `module`, `v8`, `http2`,
`diagnostics_channel`, and `node:test`. Per-export signatures and examples are in the
[Node API guide](docs/node-modules-api.md).

Several user-facing modules are TypeScript sources embedded from `stdlib/node`; host I/O remains
behind narrow primitives or C#/IL implementations. The provider chain and contribution rules are
documented in [`stdlib/CONTRIBUTING.md`](stdlib/CONTRIBUTING.md).

## 5. .NET integration and hosting

| Capability | Status | Notes |
| --- | :---: | --- |
| `dotnet:` named imports | ✅ | Single-type and namespace forms synthesize a checked surface from reflection. |
| `@DotNetType` declarations | ✅ | Manual curated declarations, closed generics, and overload hints. |
| External DLL/NuGet references | ✅ | `sharpts.json` and repeatable `-r`; used dependencies are copied for compiled output unless suppressed. |
| Delegates and events | ⚠️ | Both modes support them; callbacks follow the source .NET thread, and event deployment may require the managed runtime. |
| Exception mapping | ⚠️ | Interpreter maps common CLR exceptions to JS-style errors; compiled mode currently propagates raw CLR exceptions. |
| Generate interop declarations | ✅ | Managed CLI only: `--gen-decl` inspects types, namespaces, or assemblies. |
| Compile TypeScript for C# | ✅ | Consumers can use normal reflection or a direct assembly reference where the emitted public surface permits it. |
| Managed embedding API | ✅ | Structured compile/run and bounded single-source execution services. It is not a security sandbox. |
| Native AOT host | ✅ | Closed generated interop catalog; open-world reflection and managed-only tooling are excluded. |
| Hosted compiled ABI | ✅ | `--hosted --target dll` emits the versioned hosting contract and requires `SharpTS.Hosting.Abstractions`. |

See [.NET types](docs/dotnet-types.md), [.NET integration](docs/dotnet-integration.md),
[Embedding](docs/embedding.md), and [Native AOT](docs/native-aot.md).

## 6. GUI and tooling

| Capability | Status | Notes |
| --- | :---: | --- |
| Avalonia TypeScript/TSX applications | Supported | Retained/reactive application API, interpreted and compiled guests, Headless tests, Windows publishing, experimental Apple Silicon candidate. |
| Public custom controls | ❌ | No supported public third-party provider or descriptor-registration API; internal provider seams have no compatibility promise. |
| TypeScript-source PDBs | ✅ | Portable PDBs with source documents, sequence points, locals/scopes, and async metadata. |
| VS Code extension | ✅ | Diagnostics, interop IntelliSense, and Debug Current File. |
| Standalone language server | ✅ | Diagnostics, navigation, references, safe rename domains, completion, hover, signatures, and quick fixes. |
| General property/member rename | Deferred | Refused when workspace completeness cannot make the edit safe. |

See the [GUI overview](docs/gui/README.md), [language server guide](docs/language-server.md), and
[debugging guide](docs/debugging-typescript.md).

## 7. Conformance baselines

Both corpora use committed path-by-path baselines. Percentages below are recalculated from the
committed files; `Skipped` entries are policy exclusions rather than passes.

### Test262

The selected Test262 subset has 11,384 entries in each mode and 879 skips in each baseline.

| Mode | Pass | Fail | RuntimeError | ParseError | HarnessError | Timeout | Skipped | Pass / all | Pass / non-skipped |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Interpreted | **9,291** | 878 | 326 | 10 | 0 | 0 | 879 | **81.61%** | **88.44%** |
| Compiled | **8,090** | 1,779 | 606 | 10 | 18 | 2 | 879 | **71.06%** | **77.01%** |

These totals establish current capability; they do not make one engine the reference
implementation for the other. Any disagreement can identify a bug in either backend. The completed
interpreter parity effort is recorded in [#1279](https://github.com/nickna/SharpTS/issues/1279);
ongoing harness and coverage work is tracked in
[#1280](https://github.com/nickna/SharpTS/issues/1280). See the
[Test262 runner guide](SharpTS.Test262/README.md).

### TypeScript conformance

The pinned TypeScript 6.0.3 subset contains 514 entries: 151 `Pass`, 214 `Fail`, 141
`ParseError`, and 8 `Skipped`. That is 29.38% of all selected entries or 29.84% excluding skips.
The result is subset-relative, not whole-language coverage. Work and corpus growth are tracked on
[#1281](https://github.com/nickna/SharpTS/issues/1281); see the
[runner guide](SharpTS.TypeScriptConformance/README.md).

## 8. Performance

SharpTS maintains two complementary suites: an external whole-program comparison against Node/Bun
and managed BenchmarkDotNet microbenchmarks against C# ceilings. Current measurements and exact
reproduction commands live in [`benchmarks/README.md`](benchmarks/README.md); this status page does
not freeze historical benchmark magnitudes.

## 9. Current known gaps

- Type aliases are validated lazily in some paths, so an error in an unused alias can be reported
  later than `tsc` reports it.
- A compiled dynamic-index miss on a plain object can produce `null` rather than `undefined`.
- Compiled indirect `eval` cannot access compiled local variables.
- The `Function` constructor is absent.
- Compiled .NET interop propagates raw CLR exceptions rather than applying the interpreter's
  JavaScript error-name mapping.
- `AsyncLocalStorage.run`/`exit` do not yet forward optional trailing arguments through the
  embedded TypeScript facade.
- Node compatibility is intentionally a maintained subset; unsupported modules and per-export
  ceilings should be checked before porting a Node application.
- GUI custom-control providers are internal implementation seams, not a public extension API.
