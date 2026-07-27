# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SharpTS is a TypeScript interpreter and compiler implemented in C# using .NET 10.0. It supports both tree-walking interpretation and ahead-of-time compilation to .NET IL.

## Build and Run Commands

```bash
dotnet build                              # Build the project
dotnet test                               # Run xUnit tests
dotnet test --filter "FullyQualifiedName~SomeTest"  # Run specific test
dotnet run                                # Start REPL mode
dotnet run -- script.ts                   # Interpret a TypeScript file
dotnet run -- --compile script.ts         # Compile to .NET assembly
dotnet run -- --compile script.ts -o out.dll  # Custom output path
dotnet run -- --compile script.ts --verify    # Verify emitted IL
```

## Architecture

### Pipeline Flow

```
Source → Lexer → Parser → TypeChecker → Interpreter (tree-walk)
                                     ↘ ILCompiler (AOT to .NET IL)
```

### Directory Structure

| Directory | Purpose |
|-----------|---------|
| `Parsing/` | Lexer, Parser (partial files), AST node definitions |
| `TypeSystem/` | Static type checking, type compatibility, generics |
| `Execution/` | Tree-walking interpreter |
| `Compilation/` | IL compilation (largest directory), async state machines, bundling |
| `Runtime/` | Runtime values, environment, built-ins |
| `Runtime/Types/` | TypeScript value types: arrays, classes, promises, etc. |
| `Runtime/BuiltIns/` | Built-in method implementations |
| `Runtime/Exceptions/` | Non-local unwinding exceptions (throw, yield) |
| `Modules/` | Module resolution, script detection |
| `Diagnostics/` | Error reporting, source locations |
| `Packaging/` | NuGet package generation |
| `Cli/` | Command-line argument parsing |
| `Configuration/` | tsconfig.json discovery/`extends`/merge + strictness option resolution |
| `Declaration/` | TypeScript declaration generation from .NET types |
| `Repl/` | Interactive REPL (PrettyPrompt-based; needs a real terminal — untestable end-to-end) |
| `References/` | `sharpts.json` manifest → third-party DLL / NuGet reference resolution (restore, assets parsing, load) |
| `stdlib/` | TypeScript source for Node-compatible modules, embedded into SharpTS.dll and compiled with user programs |
| `Examples/` | Showcase programs (`Examples/test-examples.ps1` harness; `examples.yml` workflow runs it on demand) |
| `SharpTS.Sdk/` + `SharpTS.Sdk.Tasks/` | MSBuild SDK package (`Sdk="SharpTS.Sdk"`) and its build tasks |
| `SharpTS.LanguageServer/` | Standalone OmniSharp-based LSP server (`sharpts-lsp` tool) for IDE integration |
| `SharpTS.Tests/` | xUnit test project |

### Critical Architecture Patterns

**Two-Environment System:**
- `TypeEnvironment` - Tracks types during static analysis (compile-time)
- `RuntimeEnvironment` - Tracks values during execution (runtime)
- Never mix these - they serve completely different phases

**Control Flow:**
- Return/break/continue are signaled via the `ExecutionResult` struct (`Execution/ExecutionResult.cs`), not exceptions
- `ThrowException` (guest `throw`) and `YieldException` (generator suspension) remain as intentional exception-based unwinding

**Type System:**
- Structural typing for interfaces (duck typing)
- Classes are compared **structurally** for assignment compatibility (matching `tsc`), **except** when the target class is branded by a private/protected member anywhere in its hierarchy — then it is nominal (source must be the same class or a subclass). Inheritance/subtyping is still nominal (name-based) for the hierarchy walk. See `TypeChecker.Compatibility.cs` (`Instance` vs `Instance`) and `HasNominalClassBrand` (`TypeChecker.Compatibility.Helpers.cs`).
- `TypeInfo` records represent types statically; runtime objects are independent

### RuntimeValue Boxing Elimination (Active Optimization)

The codebase is migrating from `object?` to `RuntimeValue` struct to eliminate boxing:

**RuntimeValue** (`Runtime/RuntimeValue.cs`):
- 24-byte discriminated union storing primitives inline
- `ValueKind` enum: Undefined, Null, Boolean, Number, String, Object, Symbol, BigInt
- Factory methods: `FromNumber()`, `FromString()`, `FromBoolean()`, `FromObject()`, `FromBoxed()`
- Accessors: `AsNumber()`, `AsString()`, `AsBoolean()`, `AsObject<T>()`
- JavaScript semantics: `IsTruthy()`, `TypeofString()`

**ISharpTSCallable** (`Runtime/Types/SharpTSFunction.cs`) is the single callable interface:
- Boxed `Call(Interpreter, List<object?>)` (being retired) plus `CallV2(Interpreter, ReadOnlySpan<RuntimeValue>)`
- `CallV2` has a default implementation that bridges to `Call` — unmigrated implementors work unchanged; migrated ones override it to run boxing-free
- Call sites holding boxed args should use `.CallBoxed(...)` from `Runtime/Types/CallableInterop.cs` (routes through `CallV2`) rather than invoking legacy `Call` directly. NOTE: the name **"Call"** is a reflection contract — emitted IL invokes it via `Ldstr "Call"` at 5 sites (`RuntimeEmitter.VmHelpers.cs`, `RuntimeEmitter.Objects.Invocation.cs`, `RuntimeEmitter.Objects.Properties.cs`); renaming the interface method silently breaks compiled DLLs. `CallBoxed` is only a C# extension method, not a reflected name.
- Spans can't cross `await`: async call sites evaluate all args first, then call synchronously; async/generator implementors copy span → list before starting the state machine

**BuiltInMethod** (`Runtime/BuiltIns/BuiltInMethod.cs`):
- `CreateV2()` factory for RuntimeValue-based methods; `HasV2Implementation` gates the interpreter fast path
- Nearly all production built-in bodies use `CreateV2`/`MethodV2` form. Known stragglers on the legacy delegate (`Func<Interpreter, object?, List<object?>, object?>`): the `ProcessBuiltIns` diagnostics/report registrations, and `BuiltInAsyncMethod` (`Runtime/BuiltIns/BuiltInAsyncMethod.cs`, ~60 async built-in registrations — never migrated, no `CallV2` override). The legacy plumbing otherwise survives only for the emitted-IL "Call" reflection contract above and `CreateConstant`. Do not register new built-ins with the legacy constructor.
- Thread-local pooling in array built-ins to avoid allocations

### Visitor-Style Traversal Pattern

All major phases use switch pattern matching on AST node types:
- `TypeChecker.Check()` / `CheckExpr()` - static analysis
- `Interpreter.Execute()` / `Evaluate()` - runtime
- `ILEmitter.EmitStatement()` / `EmitExpression()` - IL compilation

### AST Nodes

All AST nodes are C# records in `Parsing/AST.cs`:
- Expression nodes inherit from `Expr`
- Statement nodes inherit from `Stmt`
- Use pattern matching to traverse

### IL Compilation Phases

ILCompiler runs in multiple phases:
1. Emit runtime types
2. Analyze closures
3. Define classes/functions
4. Collect arrow functions
5. Emit arrow bodies
6. Emit class methods
7. Emit entry point
8. Finalize types

Arrow functions use display classes for captured variables; non-capturing arrows compile to static methods.

### CRITICAL: Standalone DLL Constraint

**Compiled TypeScript DLLs must NOT reference SharpTS.dll.** The output DLL must be fully standalone.

**NEVER do this in Compilation/ files:**
```csharp
// BAD - embeds SharpTS.dll reference in output
var method = typeof(RuntimeTypes).GetMethod("SomeMethod");
il.Emit(OpCodes.Call, method);
```

**Instead, emit reflection-based IL that resolves the type at runtime via
`Type.GetType("…, SharpTS")`.** The canonical helpers live in
`Compilation/RuntimeEmitter.ReflectionHelpers.cs` — reuse them rather than calling
`typeof(...)` directly or hand-rolling the idiom:
```csharp
// GOOD - defines a static helper on the emitted runtime type whose body does
// Type.GetType("SharpTS.Compilation.RuntimeTypes, SharpTS").GetMethod(name).Invoke(...)
runtime.SomeMethod = EmitReflectionHelper(typeBuilder, "SomeMethod", argCount);

// GOOD - same idiom emitted inline into an existing method body; leaves the
// Invoke result on the stack (pop it yourself for void targets); onMissing
// customizes the SharpTS.dll-absent path (ret/throw); emitArg customizes
// argument loading. EmitReflectionCreateInstance covers the
// Activator.CreateInstance(Type.GetType("…, SharpTS"), args) form.
EmitReflectionCall(il, RuntimeTypesLateBoundName, "SomeMethod", argCount);
```
Specialized variants emit the same late-bound `Type.GetType("…, SharpTS")` dispatch
for shapes the canonical helpers don't cover (e.g. `EmitVmReflectionCall` in
`RuntimeEmitter.VmHelpers.cs`, `EmitReflectionConstructFromType` in
`ILEmitter.Calls.Constructors.cs`). When no existing helper fits, add one alongside
them — never reference a SharpTS type via a metadata token.

The same applies to `PropertyDescriptorStore`, `ObjectBuiltIns`, and any other SharpTS types.

**Why:** When emitting IL with `typeof(X).GetMethod(...)`, the method token references the SharpTS assembly directly. This creates a hard dependency. The reflection pattern emits IL that does `Type.GetType("..., SharpTS")` at runtime, allowing graceful degradation if SharpTS isn't present.

**Soft-dependency signal + auto-copy:** Some features (eval, Proxy, Intl, vm, dns, `@DotNetType` dynamic events) emit a `Type.GetType("…, SharpTS")` path whose *normal* execution needs SharpTS.dll present. When such a path is emitted, the emit site records a reason via `EmittedRuntime.RequireSharpTSRuntime(reason)`, surfaced through `ILCompiler.RequiredSharpTSRuntimeReasons`. After `Save`, the CLI (`Program.cs` `CopySharpTSRuntimeIfNeeded`) co-locates SharpTS.dll with the output **only when** that set is non-empty — programs using none of these stay fully standalone. `--compile … --standalone` suppresses the copy (those features then throw a clear "not supported" error at runtime). Do NOT record reasons for pure-BCL features (zlib, child_process, JSON) or unconditional graceful-fallback plumbing (`process`) — only paths a normal program actually reaches.

### Error Handling Conventions

- Type errors: "Type Error:" prefix; runtime errors: "Runtime Error:" prefix
- The structured model is `Diagnostics/Diagnostic.cs` (`DiagnosticCode` + optional canonical `TSnnnn` code); type-checker throws carry `tsCode:` where a `tsc` analogue exists
- Compiler warnings are collected on `ILCompiler.Warnings` (never written to Console from `Compilation/`); the CLI prints them to stderr

## Important Implementation Details

- **For Loop Desugaring:** Parser converts `for` loops into `while` loops
- **console.log:** Hardcoded special case in type checker, interpreter, and compiler
- **Inner function declarations:** Supported in IL compiler with hoisting, closure capture, and recursion
- **Method Lookup:** Searches up inheritance chain (see `TypeChecker.cs` CheckGet, `Interpreter.Properties.cs` EvaluateGet)

## Conformance Suites

Two standalone test projects pin SharpTS against external corpora. Neither is in `SharpTS.sln`; invoke explicitly. Both use a committed-baseline + diff harness — runs hard-fail on regression or new-pass.

- **`SharpTS.Test262/`** — TC39 ECMA-262 (interpreter + compiled). Update baseline: `SHARPTS_TEST262_UPDATE_BASELINE=1`.
- **`SharpTS.TypeScriptConformance/`** — `microsoft/TypeScript` conformance (type-checker only). Update baseline: `SHARPTS_TSCONFORMANCE_UPDATE_BASELINE=1`. Bucket model: Pass/Fail/ParseError/TypeCheckError/Skipped/HarnessError. Match strategy: `(line, tsCode)` tuples — `tsCode` is the canonical `TSnnnn` code carried on every type-checker `Diagnostic` (see `Diagnostics/Diagnostic.cs`). See `SharpTS.TypeScriptConformance/README.md`.

## Benchmarking

Two **complementary** benchmark suites measure different things — they are not redundant and cannot be merged (one drives external runtime executables; the other must run in-process against managed code). Pick by the question you're answering.

- **`benchmarks/`** — *external / competitive.* "Are we as fast as Node/Bun?" PowerShell harness (`run-benchmarks.ps1`) runs `scripts/*.ts` whole-program across the SharpTS interpreter, SharpTS compiled, Node.js, and Bun via a shared `scripts/lib/bench.ts`. Wired into CI (`.github/workflows/benchmarks.yml`, `workflow_dispatch` only). Goal: **meet or exceed Node.** See `benchmarks/README.md`.
- **`SharpTS.Microbenchmarks/`** — *internal / headroom.* "How close are we to the C# ceiling, and where's the overhead?" BenchmarkDotNet project in `SharpTS.sln`; compiles TS in-process and compares against idiomatic C# (native-type ceiling) and "equivalent" C# (`object?`/boxing tax), with `MemoryDiagnoser` allocation profiling. This is the harness behind compiler perf work. See `SharpTS.Microbenchmarks/README.md`.

`benchmarks/scripts/lib/algorithms.ts` is **shared byte-identical** between the two (embedded into the microbenchmark assembly as `SharpTS.Microbenchmarks.algorithms.ts`) so both measure the same source. Embedded-resource names are referenced by string and `RootNamespace`/`AssemblyName` are pinned in the `.csproj` — a wrong name compiles but throws at `[GlobalSetup]`.

## See Also

- `STATUS.md` - Feature implementation status and known bugs
- `README.md` - User documentation and examples
