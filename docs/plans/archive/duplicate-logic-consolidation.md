# Duplicate logic consolidation plan

## Status

Implemented (2026-07-26) with two review adjustments, on branch
`refactor/duplicate-logic-consolidation`:

1. **Cluster 2 gained a fifth copy**: `Runtime/Types/IpcSerializer.cs` carried a
   byte-identical `JsonElement` converter the original inventory missed; it now
   routes through `RuntimeJson` with the other four.
2. **Cluster 8 (crypto byte encoding/decoding IL) is PARKED, not implemented.**
   The June 2026 tech-debt assessment's C#-merge spike (Roslyn-compiled built-ins,
   Cecil-merged into the persisted output) passed all gates and would replace the
   crypto BCL-wrapper IL wholesale. Perfecting shared IL body emitters for DH/ECDH/
   Sign is likely wasted work until that direction is decided; revisit after.

Everything else below (clusters 1–7, 9, 10) is implemented. This is a
behavior-preserving refactor plan; no consolidation described here should
intentionally change TypeScript, JavaScript, CLI, interpreter, or
compiled-runtime behavior.

## Goal

Move repeated production logic into shared functions or narrowly scoped helper
types so that:

- semantic fixes have one implementation point;
- interpreter, type-checker, and compiler paths do not drift accidentally;
- emitted-runtime IL patterns remain consistent;
- new call sites reuse an established helper instead of copying an existing block;
- standalone compiled DLL constraints remain intact.

## Success criteria

- Each selected duplicate cluster has one authoritative implementation.
- Call sites retain their existing phase-specific diagnostics and defaults.
- Interpreter and compiled-mode parity tests remain green.
- Standalone compiled DLLs gain no new metadata reference to `SharpTS.dll`.
- The full test suite passes after every phase.
- No phase combines consolidation with unrelated behavior changes.

## Non-goals

- Merging the interpreter, type checker, and IL compiler into a single AST visitor.
- Replacing intentional phase-specific representations.
- Redesigning the parser grammar or `CompilationContext`.
- Changing JavaScript coercion, omitted-argument, or error-message behavior.
- Consolidating test setup merely to reduce test-file line count.

## Guardrails

1. **Preserve standalone output.** Code under `Compilation/` must continue using
   emitted helpers or late-bound reflection where required. It must not introduce
   direct `SharpTS.dll` references into generated assemblies.
2. **Keep diagnostics at phase boundaries.** Shared evaluators should return a
   structured error or accept an error factory; they should not force the
   interpreter, type checker, and compiler to throw the same exception type.
3. **Do not conflate `null` and `undefined`.** Emitter helpers must name their
   default precisely. `EmitBoxedArgumentOrNull` and `EmitOmittedArgument` are not
   interchangeable.
4. **Preserve speculative parsing.** Arrow parsing must still backtrack on an
   invalid candidate rather than throwing errors intended for a committed function
   parse.
5. **Prefer narrow helpers.** Extract the repeated semantic unit, not a large
   options object that obscures control flow.
6. **Characterize before moving.** Add or identify tests for edge cases before
   changing high-fan-out code.

## Inventory and priority

| Priority | Duplicate cluster | Current spread | Proposed shared owner | Risk |
|---|---|---:|---|---|
| P0 | Type-member leaf resolution | Two dispatch paths in `TypeChecker` | Existing `CheckGetOn*` helpers | Low–medium |
| P0 | Runtime JSON element conversion | Four exact runtime copies | `RuntimeJson` helper | Low |
| P0 | Boxed emitter arguments and defaults | Multiple type/module emitters | `EmitterArgumentHelpers` | Low–medium |
| P0 | Async builder reflection helpers | Two subclasses of `AsyncBuilderBase` | `AsyncBuilderBase` | Low |
| P1 | Const-enum expression evaluation | Compiler, interpreter, type checker | Phase-neutral evaluator | Medium |
| P1 | Weak target/value validation IL | WeakMap, WeakSet, WeakRef | Parameterized runtime-emitter helper | Low |
| P1 | Upward configuration-file discovery | Three loaders | Filesystem discovery helper | Low |
| P1 | Crypto byte encoding/decoding IL | DH, ECDH, Sign, crypto primitives | Shared IL body emitters | Medium |
| P2 | `CompilationContext` initialization | 24 constructions in 13 files | Layered context factories | High |
| P2 | Runtime parameter parsing | At least eight named-parameter tails | Parser parameter helpers | High |
| P3 | Object integrity state | Object, instance, array plus built-in switches | Integrity-state component/interface | Medium |
| P3 | Net/TLS server control methods | Net and TLS server types | Server lifecycle component | High |

## Phase 0: characterization and baselines

Before production edits:

1. Record the clean full-suite result.
2. Confirm targeted suites for every P0/P1 cluster.
3. Add focused characterization cases only where the current behavior is not
   already covered.
4. For compiled-runtime changes, retain or add standalone-DLL coverage.

Targeted coverage:

| Area | Existing coverage to run or extend |
|---|---|
| Property lookup | `TypeCheckerTests`, especially class/interface/generic member access |
| JSON conversion | `JSONTests`, `JSONProxyTests`, `HttpModuleTests`, `StandaloneDllTests` |
| Emitter defaults | Relevant shared tests plus `ILVerificationTests` |
| Async builders | Async/await shared tests, `ILVerificationTests` |
| Const enums | `EnumTests`, `CliCompileTests` |
| Weak collections/references | `WeakMapSetTests`, `WeakRefTests`, `RuntimeTypeSyncTests` |
| File discovery | `TsConfigLoaderTests`, `PackageJsonLoaderTests`, `SharpTsManifestLoaderTests` |
| Crypto encoding | Crypto shared tests and standalone compiled-mode coverage |
| Parser parameters | `ArrowFunctionTests`, `FunctionTypeAnnotationTests`, rest/default/destructuring tests |

## Phase 1: low-risk local extractions

### 1. Reuse type-member leaf resolvers

#### Problem

`TypeChecker.Properties.cs::CheckGetOnType` reimplements class, interface,
record, instance, union, and intersection lookup even though
`TypeChecker.Properties.Helpers.cs` already contains `CheckGetOnClass`,
`CheckGetOnInterface`, `CheckGetOnRecord`, and `CheckGetOnInstance`.

The two top-level paths have intentionally different context in some cases,
especially union missing-member handling, but the leaf lookup is duplicated.

#### Plan

1. Keep recursive-alias and mapped-type normalization in `CheckGetOnType`.
2. Replace its class/interface/record/instance blocks with calls to the existing
   `CheckGetOn*` helpers.
3. Keep union and intersection orchestration local initially.
4. If union behavior is later shared, represent the difference explicitly with a
   small missing-member policy rather than exception-catching differences.
5. Add parity tests that access the same member directly and through a constrained
   type parameter/union.

#### Acceptance

- One implementation performs each class/interface/record/instance leaf lookup.
- Static visibility enforcement still names the declaring class.
- Generic instance substitution is unchanged.
- Existing TS diagnostic codes remain unchanged.

### 2. Centralize runtime JSON conversion

#### Problem

The same `JsonElement` switch appears in:

- `Runtime/BuiltIns/JSONBuiltIns.cs`
- `Runtime/Types/SharpTSFetchResponse.cs`
- `Runtime/Types/SharpTSRequest.cs`
- `Runtime/Types/SharpTSResponse.cs`
- `Runtime/Types/IpcSerializer.cs` (found in review — a fifth byte-identical copy)

The three body types also duplicate the small `ParseJson(string)` wrapper.

#### Plan

1. Add an internal runtime helper, tentatively:

   ```csharp
   internal static class RuntimeJson
   {
       public static object? Parse(string text);
       public static object? FromElement(JsonElement element);
   }
   ```

2. Return `SharpTSArray` and `SharpTSObject` exactly as the current runtime copies
   do.
3. Leave exception translation at each caller:
   - `JSON.parse` keeps its current syntax-error behavior;
   - Request/Response/Fetch keep their promise-rejection behavior.
4. Do not route `Compilation/RuntimeTypes.Json.cs` through this helper. Its
   `List<object?>`/`Dictionary<string, object?>` representation is intentional for
   compiled standalone code.

#### Acceptance

- Only `RuntimeJson` recursively converts runtime `JsonElement` values.
- Numeric conversion remains `GetDouble()`.
- HTTP body-consumption and promise rejection behavior are unchanged.
- Standalone JSON tests still pass.

### 3. Add shared emitter argument helpers

#### Problem

`EmitSingleArgOrNull`, `EmitSecondArgOrNull`, `EmitBoxedArg`, and
`EmitBoxedArgument` repeat the same expression/boxing/default sequence across
Map, Set, WeakMap, WeakSet, Iterator, Process, DataView, event-related, and other
emitters. `ExpressionEmitterBase` has a private equivalent that strategy emitters
cannot reuse.

#### Plan

1. Add a helper accessible to `IEmitterContext` implementations:

   ```csharp
   internal static class EmitterArgumentHelpers
   {
       public static void EmitBoxedArgumentOrNull(
           IEmitterContext emitter,
           IReadOnlyList<Expr> arguments,
           int index,
           LocalBuilder[]? preEvaluated = null);
   }
   ```

2. Migrate the exact null-default copies first:
   Map, Set, WeakMap, WeakSet, Iterator, and Process emitters.
3. Add separately named helpers only for repeated semantics:
   - boxed argument or `null`;
   - numeric argument or zero;
   - string-coerced argument with an explicit default.
4. Do not create a generic “optional argument” helper whose default is implicit.
5. Do not merge `object.ToString()` and runtime JavaScript `Stringify`; they have
   different coercion semantics.
6. Support pre-evaluated locals so await-safe paths do not re-evaluate arguments.

#### Acceptance

- Exact boxed-or-null copies are removed.
- Helper names state the emitted default.
- Async/await argument evaluation order remains unchanged.
- Omitted arguments that require the `$Undefined` sentinel still use
  `EmitOmittedArgument`.

### 4. Complete `AsyncBuilderBase`

#### Problem

`AsyncBuilderBase` already owns common awaiter accessors, but
`AsyncStateMachineBuilder` and `AsyncArrowStateMachineBuilder` still duplicate:

- type finalization and label validation;
- builder `Create`;
- builder `Task` getter;
- generic builder `Start`;
- builder `SetException`;
- `AwaitUnsafeOnCompleted`.

#### Plan

1. Expose `BuilderType` as an abstract protected/public property on
   `AsyncBuilderBase`.
2. Implement the common reflection accessors in the base using `BuilderType`,
   `AwaiterType`, and inherited `StateMachineType`.
3. Implement the common `CreateType` override in the base.
4. Keep `GetBuilderSetResultMethod` specialized:
   `AsyncTaskMethodBuilder` has a parameterless `SetResult`, while generic builders
   do not.
5. Remove subclass copies only after both builders compile against the base API.

#### Acceptance

- Common builder accessors exist only in `AsyncBuilderBase`.
- Both ordinary async functions and standalone/nested async arrows pass.
- IL label validation still occurs before `CreateType`.

## Phase 2: shared semantic and emitted-runtime helpers

### 5. Extract const-enum expression evaluation

#### Problem

Literal, member-reference, grouping, unary, and binary evaluation is effectively
triplicated in the compiler, interpreter, and type checker. Only exception types,
message prefixes, and TypeScript diagnostic codes differ.

#### Plan

1. Add a phase-neutral evaluator near the AST, tentatively:

   ```csharp
   internal static class ConstEnumExpressionEvaluator
   {
       public static ConstEnumEvaluationResult Evaluate(
           Expr expression,
           IReadOnlyDictionary<string, object> resolvedMembers,
           string enumName);
   }
   ```

2. Return a typed error containing:
   - error kind;
   - operator/member name where applicable;
   - formatted neutral message.
3. Map error kinds at each boundary:
   - compiler → `CompileException`;
   - interpreter → `InterpreterException`;
   - type checker → `TypeCheckException` with the existing TS code.
4. Preserve numeric casts and supported operators exactly.
5. Migrate all three callers in one change so no fourth implementation remains.

#### Acceptance

- Arithmetic and reference resolution have one implementation.
- Phase-specific exception types and TS codes are preserved.
- Forward references, invalid operands, null values, and string concatenation are
  characterized.

### 6. Share weak-target validation emission

#### Problem

WeakMap, WeakSet, and WeakRef each emit the same primitive probes and throw
sequence. They differ only in method name, `EmittedRuntime` slot, and error text.

#### Plan

1. Add a parameterized IL body helper on `RuntimeEmitter`, for example:

   ```csharp
   private MethodBuilder EmitWeakTargetValidator(
       TypeBuilder owner,
       string methodName,
       WeakTargetErrorMessages messages);
   ```

2. Keep the public/internal `EmittedRuntime` fields separate where call sites
   require distinct method handles.
3. Preserve construct-specific error messages.
4. Put primitive classification in one helper so future Symbol/BigInt/null
   conformance fixes cannot drift across weak constructs.

#### Acceptance

- The primitive-probe IL sequence has one source.
- WeakMap, WeakSet, and WeakRef retain their current messages and call sites.
- `RuntimeTypeSyncTests` and standalone weak-collection tests pass.

### 7. Extract upward file discovery

#### Problem

`TsConfigLoader`, `PackageJsonLoader`, and `SharpTsManifestLoader` repeat:

- normalization of temp and user-profile ceilings;
- parent-directory traversal;
- the “search a ceiling only when it is the starting directory” rule;
- case-insensitive path comparison.

All three also repeat the same lenient JSON serializer options.

#### Plan

1. Add an internal filesystem helper in a neutral configuration/support namespace:

   ```csharp
   internal static string? FindNearestFile(
       string startDirectory,
       string fileName,
       string? stopDirectory = null);
   ```

2. Centralize ceiling normalization once.
3. Preserve `PackageJsonLoader`'s explicit stop-directory behavior.
4. Let each loader retain its own parse result and error translation.
5. Optionally expose one shared read-only `JsonSerializerOptions` instance after
   verifying no caller mutates it.

#### Acceptance

- All three loaders share the same upward-walk policy.
- Start-at-ceiling and ascend-into-ceiling cases are tested.
- Stop-directory exclusivity remains unchanged.
- Malformed-file errors still name the correct manifest/config type.

### 8. Share crypto byte encoding and decoding IL bodies

**PARKED (2026-07-26).** Deliberately not implemented: the C#-merge structural
direction (see the June 2026 tech-debt assessment; all spike gates passed) would
replace this hand-written crypto IL wholesale, making a shared-IL-body refactor
here likely throwaway. It is also the riskiest Phase 2 item (Sign fall-through /
return-label behavior, non-uniform `base64url` support). Revisit only if the
C#-merge direction is rejected.

#### Problem

DH and ECDH emit nearly identical Buffer/byte-array/string decoding with optional
hex/base64 handling. DH, ECDH, Sign, and `CryptoEncodeBytes` repeat substantial
byte-to-hex/base64/Buffer dispatch.

#### Plan

1. First extract host-side IL body helpers; do not immediately alias all runtime
   methods to one `MethodBuilder`.
2. Parameterize:
   - how the byte array is loaded;
   - the encoding argument index;
   - accepted encodings (`base64url` support is not identical everywhere);
   - invalid-input behavior.
3. Use the shared body helper for DH and ECDH exact copies.
4. Migrate Sign only after confirming its fall-through and return-label behavior.
5. Consider routing wrappers to emitted `CryptoEncodeBytes` only if its accepted
   encoding set matches the Node API being implemented.

#### Acceptance

- DH and ECDH decode semantics are byte-for-byte equivalent.
- Unknown-encoding fallback remains unchanged.
- Hex casing and Buffer return types remain unchanged.
- No generated assembly gains a `SharpTS.dll` dependency.

## Phase 3: high-fan-out compiler context factory

### 9. Layer `CompilationContext` construction

#### Problem

There are 24 direct `new CompilationContext(...)` sites across 13
`ILCompiler` partial files. Large object initializers repeat closure, enum, runtime,
module, type-emitter, import, and class-expression registries.

An existing method named `CreateCompilationContext` is not a safe universal base:
it also sets module-top-level state, including `IsModuleTopLevel = true`.

#### Proposed shape

```csharp
private CompilationContext CreateBaseCompilationContext(ILGenerator il);
private CompilationContext CreateModuleTopLevelContext(ILGenerator il);
private CompilationContext CreateFunctionContext(
    ILGenerator il,
    IReadOnlyList<Stmt>? body,
    string? currentClassName = null);
private CompilationContext CreateStateMachineContext(
    ILGenerator il,
    StateMachineContextOptions options);
```

The base factory should contain only values that are invariant for all emission
contexts, such as shared registries and compilation-wide maps. Specialized
factories apply scope-sensitive values:

- current module and namespace;
- strict-mode determination;
- module-top-level status;
- class/private-member context;
- async builder maps;
- captured/display-class fields;
- lock-decorator fields;
- current return type.

#### Migration steps

1. Inventory every assigned `CompilationContext` property by call site.
2. Classify each property as:
   - compilation-wide invariant;
   - module-scoped;
   - function-scoped;
   - class-scoped;
   - state-machine-scoped;
   - call-site-only.
3. Rename the current module helper to `CreateModuleTopLevelContext` before
   introducing the base factory.
4. Add `CreateBaseCompilationContext` with only proven invariants.
5. Migrate one context family per commit:
   - synchronous functions;
   - class methods/accessors/constructors/statics;
   - arrows and inner functions;
   - generators;
   - async and async generators;
   - module/CommonJS entry points.
6. Leave short call-site overlays visible rather than hiding every property behind
   a broad options object.
7. After migration, prevent new direct construction outside the factory file by a
   source-level architecture test or analyzer-style test.

#### Acceptance

- Every production `ILCompiler` emission context comes from a named factory.
- Module-top-level state cannot leak into function or state-machine contexts.
- All context properties used by private members, modules, CommonJS, locks,
  closures, and strict mode retain coverage.
- Cross-module, async, generator, class-expression, and standalone-DLL tests pass.

## Phase 4: parser parameter consolidation

### 10. Extract parameter parsing components

#### Problem

Named runtime parameters repeatedly parse:

1. `...`;
2. identifier/contextual keyword;
3. `?`;
4. type annotation and `TypeNode`;
5. default initializer;
6. rest-last validation;
7. `Stmt.Parameter` construction.

Array/object destructured parameters and synthetic `_paramN` creation are also
repeated in function declarations, methods, arrows, and function expressions.

Not every loop is identical:

- constructors allow parameter-property modifiers and decorators;
- signatures may forbid or ignore defaults;
- function types produce `ParameterTypeNode`, not `Stmt.Parameter`;
- speculative arrows must backtrack rather than throw;
- explicit `this` parameters are type-only;
- some contexts accept contextual keywords.

#### Plan

1. Extract the deterministic named runtime-parameter tail first:

   ```csharp
   private ParsedParameter ParseNamedRuntimeParameter(
       ParameterModifiers modifiers,
       bool allowDefault);
   ```

2. Keep list-loop ownership and error/backtracking policy with each caller.
3. Extract destructured runtime parameters separately:

   ```csharp
   private (Stmt.Parameter Parameter, DestructurePattern Pattern)
       ParseDestructuredParameter(int parameterIndex, List<Decorator>? decorators);
   ```

4. Share the prologue generation that lowers synthetic parameters into
   destructuring statements.
5. Do not force function-type/signature parameter parsing through
   `Stmt.Parameter`; share only token-level/type-annotation primitives where the
   AST outputs genuinely differ.
6. Convert committed function/method/function-expression parsers first.
7. Convert speculative arrow parsing last, with explicit success/failure results.

#### Acceptance

- Runtime named-parameter construction has one implementation.
- Runtime destructured-parameter construction and prologue lowering have one
  implementation.
- Constructor parameter properties and decorators remain supported.
- Arrow candidates still backtrack correctly.
- Trailing commas, rest-last errors, optional parameters, defaults, explicit
  `this`, contextual keyword names, and nested type annotations retain tests.

## Deferred candidates

### Object integrity state

`SharpTSObject`, `SharpTSInstance`, and `SharpTSArray` duplicate the
frozen/sealed/extensible state transitions. `ObjectBuiltIns` then repeats type
switches for freeze, seal, preventExtensions, and status queries.

Revisit after the P0–P2 work. A small composed `ObjectIntegrityState` plus a common
runtime interface could consolidate both the transitions and built-in switches,
but the interface must not expose public setters or erase array-specific mutation
rules.

### Net/TLS server lifecycle

`SharpTSNetServer` and `SharpTSTlsServer` share address formatting,
`getConnections`, `ref`, and `unref`, with similar close behavior. A lifecycle
component or base class may help, but Net's IPC/cluster behavior and callback
adaptation make premature unification risky. Extract only exact leaf helpers unless
a broader server abstraction is independently justified.

## Suggested PR sequence

1. `refactor: reuse shared type-member resolvers`
2. `refactor: centralize runtime JSON conversion`
3. `refactor: share boxed emitter argument helpers`
4. `refactor: complete AsyncBuilderBase common helpers`
5. `refactor: centralize const-enum expression evaluation`
6. `refactor: share weak target validation emission`
7. `refactor: centralize upward config discovery`
8. `refactor: share crypto byte encoding and decoding emitters`
9. `refactor: layer CompilationContext factories` — multiple small commits
10. `refactor: consolidate parser parameter components` — multiple small commits

Each PR should contain one semantic cluster, its characterization tests, and no
feature work.

## Verification

Run targeted tests during each phase, then the full suite:

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --filter "FullyQualifiedName~JSONTests|FullyQualifiedName~HttpModuleTests"
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --filter "FullyQualifiedName~EnumTests"
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --filter "FullyQualifiedName~WeakMapSetTests|FullyQualifiedName~WeakRefTests"
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --filter "FullyQualifiedName~TsConfigLoaderTests|FullyQualifiedName~PackageJsonLoaderTests|FullyQualifiedName~SharpTsManifestLoaderTests"
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --filter "FullyQualifiedName~ArrowFunctionTests|FullyQualifiedName~FunctionTypeAnnotationTests"
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj --filter "FullyQualifiedName~ILVerificationTests|FullyQualifiedName~StandaloneDllTests"
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj
```

Also run:

```powershell
dotnet build
git diff --check
```

For compiler/runtime-emitter phases, explicitly verify at least one representative
standalone DLL without `SharpTS.dll` beside it.

## Completion checklist

- [x] P0 clusters are consolidated and fully tested.
- [x] Phase-neutral const-enum evaluation preserves all diagnostic mappings.
- [x] Weak emitted-runtime helper preserves standalone output (crypto: parked, see cluster 8).
- [x] All configuration loaders use one upward-discovery policy.
- [x] Direct `CompilationContext` construction is limited to its factory layer
      (enforced by `CompilationContextFactoryTests`).
- [x] Runtime parameter parsers reuse shared named/destructured components.
- [x] Full build and test suite pass.
- [x] `git diff --check` reports no whitespace errors.
- [x] Follow-up opportunities are documented rather than folded into the same PRs
      (cluster 8 parked pending the C#-merge decision; P3 deferrals unchanged).
