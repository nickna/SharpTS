# Remaining language hot paths: implementation plan

Status: stages 0–5 are implemented and measured. All 599 focused checks pass
against the final closed-entry compiler. Five paired launches show 10.63×,
138.61×, and 12.03× median speedups for spread, unknown-target, and varying-index
rest, respectively. The full run completed with host-related failures; its
limitations and noisy controls are recorded in the results report.

## Implementation record

- Stage 0: permanent numeric/boxed/plain-list read controls, pre-created dispatch
  controls, spread-length controls, and alternating eligible/ineligible targets.
  The original compiler and updated benchmark executable were frozen before
  production changes. All 21 baseline microbenchmarks passed their checksums.
- Stage 1: guarded boxed reads retain the original property-lookup fallback.
  All 69 initial focused checks passed. Boxed-array and plain-list read loops
  fell from about 960,000 bytes per 10,000 iterations to zero; numeric controls
  also allocate zero.
- Stage 2: separate union/primitive decisions and cached parameter metadata.
  All 303 focused checks passed. Pre-created rest-only dispatch dropped from
  about 360 to 296 bytes per call after stage 1, confirming the expected
  64-byte reduction. Existing compiled coercions are tested separately from
  interpreter semantics; foreign strings passed to CLR `double` slots and a
  missing defaulted argument already behave differently in the baseline.
- Stage 3: native double arguments construct exact-capacity numeric rest storage.
  `arguments` reconstruction and object-array `for…of` materialize storage at
  their existing list boundary. Escaping four-number calls fell from 240 to
  144 bytes/call; varying-index calls fell from 248 after stage 1 to 128.
- Stage 4: numeric-preserving spread append/reservation and numeric-aware regular
  parameter extraction. Spread fell from 184 after stage 1 to 120 bytes/call.
  Collection drives custom iterators directly, avoiding a wrapper allocation
  and avoiding terminal `value` getters that only `yield*` should consume.
  All 589 focused checks, including iterator and `yield*` tests, passed.
- Stage 5: an optional four-double delegate is bound through a small static entry
  to the existing wrapper. This avoids an open-static argument shuffle that the
  default-settings probe exposed as a performance problem. Existing direct
  companions take priority over indirect companions within the variant limits.
  Numeric consumers capture the callee and arguments once and retain generic
  fallback. All 599 focused checks passed again against the final compiler,
  including both constructors, special numbers, target replacement, standalone
  output, zero allocation for alternating eligible targets, and budget exhaustion.
  Five fresh-process pairs against stage 4 show a 31.34× median eligible-call
  speedup and a 0.99× pure-fallback ratio. Eligible-wrapper construction pays
  568 additional bytes, including metadata/delegate setup; ineligible wrappers
  add one reference field (8 bytes on this host).

Interim microbenchmarks used BenchmarkDotNet 0.14.0 ShortRun, in-process,
three warmups and three measurements, .NET 10.0.11 x64, SDK 10.0.400. Their
timings are diagnostic because launch/machine variation is substantial.
See the [results report](language-hot-paths-results.md) for stage commits,
allocation evidence, paired measurements, setup costs, and validation limits.

This plan follows the [performance investigation](language-hot-paths-followup.md) at revision `af72038b83e9c88449b33ca42262f3f8dc156ea7`. The [earlier rest-call plan](language-hot-paths.md) is already implemented. Retain its direct-call, alias, and constant-index optimizations.

## Objective and boundaries

Reduce compiled execution time and allocation for spread rest calls, runtime-selected rest targets, and varying rest indices. Improve the ordinary array and call paths before adding further specialization. Preserve JavaScript evaluation order, observable values, array identity, descriptors, iterators, and existing standalone output support.

Interpreter optimization, floating-point reassociation, generator lowering, parser/formatter changes, general-purpose inline caches, and a wholesale array ABI replacement are outside this tranche. Use the interpreter for differential semantic tests. Keep the existing public benchmark case names and arithmetic trees.

## Evidence and acceptance model

At N=100,000, the investigation measured compiled medians of 20.47 ms for spread, 25.89 ms for unknown targets, and 21.49 ms for varying indices. The corresponding Node medians were 0.92, 0.11, and 0.92 ms. Launch variation was large, so these numbers identify priorities rather than release budgets.

The stronger allocation evidence, on .NET 10 x64, is:

| Workload | Observed allocation | Target |
| --- | ---: | --- |
| Four reads from a reused boxed numeric array | Approximately 96 bytes/iteration | Zero allocation in the read loop |
| No-op conversion scans for one rest parameter | Approximately 64 bytes/call | Remove both parameter-array allocations |
| Ordinary escaping four-number rest array | Approximately 240 bytes/call | Reduce while preserving a fresh observable array |
| Varying-index rest | Approximately 344 bytes/call | Remove read-path and argument-boxing costs in separate stages |
| Spread rest | Approximately 280 bytes/call | Reduce destination growth and boxing after read improvements |
| Unknown-target rest | Approximately 456 bytes/call | Reduce ordinary dispatch first; eliminate per-call allocation for eligible typed targets later |

Some probes include constant per-invocation setup. Measure allocation growth across input sizes rather than treating those setup bytes as per-iteration costs. Rebaseline after each stage: savings overlap, and the table is not an additive allocation or timing budget. Architecture-specific byte counts are diagnostic; correctness tests should assert allocation slopes or compare equivalent paths, not hard-code an x64 object layout everywhere.

## Delivery sequence

Deliver six independently reviewable commits or PRs. Each stage must pass its gates before becoming the baseline for the next stage.

| Stage | Deliverable | Dependencies | Completion evidence |
| --- | --- | --- | --- |
| 0 | Permanent attribution and fallback coverage | None | Reproducible benchmarks and baseline IL/allocation records |
| 1 | Guarded dense boxed numeric reads | 0 | Approximately 96 bytes/iteration removed from reused-array read loops |
| 2 | Cached conversion decisions for ordinary calls | 0; measure after 1 | No repeated parameter-array allocation for guaranteed rest slots |
| 3 | Numeric storage for ordinary non-spread rest | 1–2 | Fewer allocations, fresh arrays, and correct boxing transitions |
| 4 | Numeric-preserving spread construction | 3 | No forced source boxing on the eligible path; fewer storage allocations |
| 5 | Bounded typed indirect-call entry point | 1–2; remeasure after 4 | Eligible four-number targets allocate nothing per call; fallback remains exercised |

Stages 1–2 are the first shipping milestone. Stages 3–4 form the storage milestone. Stage 5 is a separate dispatch milestone, so its larger ABI change does not delay the earlier improvements.

## Stage 0 — Establish permanent measurement and regression coverage

Files:

- `benchmarks/micro/SharpTS.Microbenchmarks/Benchmarks/NumericRestBenchmarks.cs`
- `benchmarks/micro/SharpTS.Microbenchmarks/TypeScriptSources/NumericRest.ts`
- New `Benchmarks/NumericArrayReadBenchmarks.cs` and `TypeScriptSources/NumericArrayRead.ts` in that project
- `benchmarks/cross-runtime/scripts/language-hot-paths.ts`
- `tests/SharpTS.Tests/CompilerTests/UnboxedNumberArrayReadTests.cs`
- `tests/SharpTS.Tests/CompilerTests/StableNumericHotPathTests.cs`
- `tests/SharpTS.Tests/SharedTests/NumericRestOptimizationTests.cs`

Work:

1. Promote the investigation's reused boxed/numeric array controls and spread/unknown-target length controls into allocation-diagnosed benchmarks. Add a reused plain `List<object>` control because indirect rest adjustment uses that representation. Use typed delegates with compilation, module initialization, delegate creation, and input construction outside the read-only measurements.
2. Keep packing benchmarks separate: construct a new rest array per call and explicitly exercise mutation or escape to prevent scalarization. Include zero, one, four, and a larger rest arity in focused allocation checks without multiplying every benchmark parameter combination.
3. Add independently invoked ordinary-dispatch controls with a pre-created wrapper. Retain the existing wrapper-construction case separately. The permanent benchmark must exercise supported runtime behavior; do not ship the investigation's reflective `_needsArgConversion` toggle as an optimization or normal benchmark mode.
4. Add unknown-target coverage that alternates between two eligible functions with distinguishable results, plus an ineligible target that observes/mutates its rest array. Preserve the original monomorphic case. Add mixed eligible/ineligible target switching before stage 5.
5. Establish semantic cases for descriptors, holes, non-double elements, rest identity, iterator side effects, and argument coercion. Add structural IL/allocation assertions with the stage that changes the lowering; do not assert a fixed instruction sequence.
6. Freeze the compiler baseline and run the same updated benchmark source against it and the candidate. Record source hashes, revision/dirty state, tool versions, runtime settings, all input sizes, means, standard deviations, launch ranges, allocated bytes, and Gen0 counts. Capture representative emitted IL.

Gate: all benchmark checksums pass; numeric storage and boxed/list fallback shapes are confirmed by IL or setup inspection; a fresh checkout can reproduce the evidence without `.perf-language-review`. Do not infer storage mode from a `number[]` annotation or a nonempty literal.

## Stage 1 — Read dense boxed numbers without general property lookup

Primary files: `ILEmitter.Properties.cs`, `ILEmitter.Helpers.cs`, `RuntimeEmitter.TSArray.cs`, `RuntimeEmitter.Arrays.cs`, and `EmittedRuntime.cs`, all under `src/SharpTS/Compilation`.

Implementation:

1. Extend `TryEmitNumberArrayGetIndexAsDouble` with a guarded boxed-read arm. Retain its numeric-consumer restriction, exact integral Int32 index check, and current descriptor/prototype feature exclusions. Raw/`any` consumers retain the existing value-returning behavior.
2. Keep the existing numeric-storage arm. Add a narrow internal try-read helper for boxed `$Array` storage and exact plain `List<object>` receivers. Return success plus an unboxed double, without constructing a tuple or other heap object. Check `$Array` before its `List<object>` base; never read the empty base list of a numeric-mode array.
3. A successful boxed read requires an in-range present own element whose actual value is a boxed double. Holes, non-double values, sparse tails outside the dense prefix, descriptors, and unsupported receivers go to the original `GetIndex` plus numeric-consumer conversion. Preserve `-0`, NaN, and infinity as element values.
4. Capture receiver and index once, in source order, and reuse those values on fallback. Do not hoist storage/type assumptions across index evaluation or calls that can mutate the receiver. Retain existing hoisting safeguards and direct-eval exclusions.
5. Keep helper contracts explicit. Do not globally weaken `CanGetDouble`, which other emitters may rely on. Do not add per-object descriptor guards or remove whole-program feature restrictions in this first stage; broader applicability is a separate measured follow-up.

Validation: extend `UnboxedNumberArrayReadTests` and shared numeric-rest tests with boxed `$Array`, plain-list rest, numeric storage, mutation through `any`, getters, prototype properties, holes, nullish/unsupported receivers, fractional/negative/NaN/out-of-range indices, and a side-effecting key. Test both numeric and raw consumers. Run IL verification and standalone execution checks.

Gate: reused boxed and plain-list read loops allocate zero bytes after setup/warmup. The successful arm avoids index boxing and `GetIndex`; fallback still exists and runs in semantic tests. Numeric-storage/scalar controls retain their allocation behavior and show no repeatable throughput regression. Record the change in spread, unknown-target, and varying-index cases without assigning all their time to reads.

## Stage 2 — Remove redundant conversion scans from ordinary dispatch

Primary files: `RuntimeEmitter.TSFunction.cs` and `EmittedRuntime.cs`.

Implementation:

1. Replace the single coarse `_needsArgConversion` decision with cached union/primitive conversion metadata. Determine it at wrapper construction using the actual reflected signature and the existing rest-shape rules.
2. Mark the final rest slot as already materialized only when `AdjustArgs` guarantees that representation. Do not exempt arbitrary `List<object>` parameters: borrowed array-method receivers may still require materialization.
3. Invoke only necessary conversion stages in both `Invoke` and `InvokeWithThis`. Cache parameter types and conversion operations needed by remaining stages so warm calls do not rebuild `ParameterInfo[]` arrays. Preserve existing union-then-primitive conversion order, undefined padding, surplus arguments, and receiver insertion.
4. Retain the cached `MethodInvoker`; rebuilding dispatch is not part of this stage. Keep all constructor variants and callable shells working. Account for wrapper-size/setup cost separately from per-call savings.
5. For conversions that depend on the actual argument type, cache stable parameter metadata and retain the correct dynamic conversion fallback; do not treat a parameter annotation as proof that conversion is unnecessary.

Validation: add focused tests for rest-only and mixed regular/rest signatures, missing/defaulted parameters, `arguments`, union/numeric/string conversion, borrowed array receivers, `call`/`apply`/`bind`, bound `this`, and exceptions during coercion. Exercise both invocation methods and constructors. Inspect emitted helper calls and measure a pre-created wrapper.

Gate: the rest-only dispatch path performs no `GetParameters` calls after setup. The allocation delta for the isolated one-rest-parameter probe is approximately 64 bytes/call on the measured .NET 10 x64 runtime, relative to stage 1. Regular conversion behavior stays correct. Require deterministic allocation evidence; do not claim a throughput gain unless paired runs establish it.

## Stage 3 — Construct ordinary rest arrays in numeric storage

Primary files: `ExpressionEmitterBase.CallHelpers.cs`, `RuntimeEmitter.TSArray.cs`, `EmittedRuntime.cs`, and affected array consumers identified during the storage audit.

Implementation:

1. Add a private exact-capacity numeric rest constructor/fill path. Use it initially for synchronous, directly resolved free-function calls without spreads or suspension. Leave the existing scalar companion route first in dispatch.
2. Evaluate arguments once, in source order. Keep proven native-double values unboxed. A `number` annotation alone must not cause coercion of an uncertain value: if the existing expression representation is boxed, guard its actual type or retain ordinary packing. Preserve raw values rather than calling `ToNumber` merely to qualify for numeric storage.
3. Keep the existing `$Array` identity and public behavior. Allocate a fresh wrapper for every observable rest array, including empty arrays. Maintain logical length and the numeric-storage invariant that the inherited boxed list is empty while numeric storage is active.
4. Audit consumers of a rest parameter's `List<object>` CLR signature before enabling numeric construction. Check length, index access, `arguments` reconstruction, enumeration/spread, array methods, borrowed methods, escape/return, closures, and mutation. Every direct base-list consumer must either use numeric-aware access or explicitly transition to boxed storage first. A passing CLR cast does not establish valid list contents.
5. Reuse the existing transition to boxed storage when heterogeneous writes, holes, descriptors, or list-based consumers require it. Preserve previously stored elements and values exactly. Add necessary guards at audited boundaries; do not broaden to instance methods, indirect adjustment, or suspending calls until their paths are covered.

Validation: numeric and heterogeneous writes, deletion, length changes, enumeration, array methods, defaults, extra/missing arguments, `arguments`, returning/capturing the rest array, repeated-call identity, and standalone output. Existing generic paths continue to handle excluded calls. Check that old ordinary-packing IL assertions accept the new semantic construction shape while retaining explicit boxed-path controls.

Gate: ordinary numeric rest calls have no per-element double boxing during construction, and allocation falls for representative nonzero arities. Escaping arrays remain fresh and correct. Varying-index calls improve without adding index-specific variants. If a consumer immediately forces boxing, record and repair that boundary or keep that call shape on ordinary storage; do not declare the storage optimization complete from construction IL alone.

## Stage 4 — Preserve numeric storage during spread expansion

Primary files: `ExpressionEmitterBase.CallHelpers.cs`, `RuntimeEmitter.Iterator.cs`, `RuntimeEmitter.TSArray.cs`, `RuntimeEmitter.Arrays.cs`, and `EmittedRuntime.cs`.

Implementation:

1. Extend the fused rest builder from stage 3 to append numeric values and transition to boxed storage when required. Keep its destination private until argument evaluation and finalization finish.
2. Add a standard-array-iterator fast path that can inspect/copy numeric storage without calling `Elements` and forcing the source into boxed storage. Use the existing iterator/prototype feature proof; custom or uncertain iteration remains on the protocol path. Never share caller-owned backing storage with an observable rest array.
3. Reserve space from safely known expanded lengths. Do not evaluate a later argument, length getter, or iterator early for sizing. At minimum, avoid starting with source-expression count when a proven spread length is already available. Guard total length/capacity arithmetic.
4. Consume each spread before evaluating later arguments. For mixed arguments or multiple spreads, preserve lookup order, iterator errors, and required closing behavior. Ensure holes produce actual `undefined` values, triggering boxed storage when necessary.
5. Support regular parameters before rest without assuming they occupy fixed positions before expansion. Extract/coerce regular parameters at the existing semantic boundary, then finalize rest length. Retain existing spill behavior for suspension; optimize suspending calls only after equivalent spill/continuation tests pass.
6. Measure direct numeric, boxed, sparse, custom-iterator, and generic dynamic-call paths separately. Dynamic invocation may still need its `object[]` ABI until stage 5; report that limitation explicitly.

Validation: extend `SpreadIntoRestParameterTests`, `NumericRestOptimizationTests`, `ArrayIteratorTests`, and `IteratorProtocolTests`; cover iterator replacement, own iterator getters, mutation while iterating, iterator throws, empty/multiple spreads, holes, regular/default parameters, and suspension. Include numeric source reads after spread to verify the fast path preserves source representation.

Gate: eligible numeric spreads avoid forced source boxing and unnecessary destination growth; allocation falls against stage 3 for the same source. The general protocol path remains correct. Confirm at least one repeatable throughput improvement on a controlled host; do not substitute a hand-flattened benchmark for the original spread case.

## Stage 5 — Add a bounded typed entry point for indirect calls

Primary files: `RuntimeEmitter.TSFunction.cs`, `EmittedRuntime.cs`, `StableNumericRestFunctionAnalyzer.cs`, `CompilationContext.Functions.cs`, `ILCompiler.Functions.cs`, and function-value/callback emission in `ExpressionEmitterBase.cs`, `ExpressionEmitterBase.CallHelpers.cs`, and `ILEmitter.Calls.cs`.

Initial scope: a synchronous plain call with exactly four proven native-double arguments and a numeric consumer. Eligible targets are stable free functions with no receiver/capture/default/`arguments` dependencies and a scalarizable four-element numeric rest body. Optional calls, spreads, constructors, bound functions, state machines, and incompatible callees retain generic dispatch.

Implementation:

1. Expose an optional typed four-double-to-double entry point on eligible `$TSFunction` values, backed by the existing scalar companion. Bind it to the actual callable value at construction, not a source identifier or last-observed target. Avoid additional allocations on ordinary wrappers that never use this capability.
2. Register the required companion even when the function is used only indirectly. Reuse identical existing companions and enforce the current 32-rest-argument, eight-variants-per-function, and 64-variants-per-compilation limits. If the budget is exhausted, leave the capability absent and use generic dispatch.
3. At an eligible call, capture the callable before evaluating arguments. Evaluate all arguments exactly once into native-double locals, then check the captured value's typed entry. Use it when available; otherwise box those already-evaluated values and take the original invocation path.
4. The successful arm must avoid caller argument arrays, rest adjustment/packing, and numeric result boxing. The fallback must preserve target changes, exceptions, receiver behavior, and observable `arguments`/rest behavior. No speculative deoptimization system is required.
5. Keep the initial arity fixed at four. Expanding arities, stable spread-to-companion lowering, and other callable categories require separate measurements and design review after the initial path passes its gates.

Validation: monomorphic and alternating eligible targets with different results; alternating eligible/ineligible targets; function reassignment; replacement during argument evaluation; captures, bound calls, `arguments`, escaping or mutated rest, omitted/extra arguments, unsupported values, companion budget exhaustion, and standalone execution. Adapt `UnknownTarget_RetainsValueDispatch` to assert a real fallback arm and exercise it, rather than forbidding all additional dispatch paths.

Gate: eligible indirect calls have zero per-call allocation after wrapper setup; changing eligible targets remains correct; ineligible targets demonstrably execute the ordinary path. Record wrapper allocation, emitted code size, assembly size, compiler time, and fallback throughput. Require repeatable improvement over stage 4 with no unexplained fallback or scalar-control regression.

## Validation and performance workflow

For each stage:

1. Build Release and run focused dual-mode semantic tests plus compiler IL/allocation tests. Use `UnboxedNumberArrayReadTests`, `StableNumericHotPathTests`, and the relevant shared suites listed above. Include `ArgumentsMagicVariableTests`, `DefaultParameterTests`, `BoundMethodCallApplyBindTests`, `CrossModuleRestParamTests`, and `NumericStorageSafetyTests` as the changed surface expands.
2. Run allocation checks after warmup using strongly typed delegates and setup outside measurement. Use multiple N values to separate constant setup from loop allocation. Retain raw numeric-storage, boxed-storage, and fallback controls throughout all stages.
3. Run focused BenchmarkDotNet measurements and isolated cross-runtime cases with at least three launches; use five alternating baseline/candidate launches for a release decision. Build both variants before measurement and run benchmarks sequentially without concurrent tests, builds, or profiling. Compare identical benchmark sources and runtime configuration.
4. Use the repository's [local performance lab](../../benchmarks/local-perf/README.md) for paired baseline/candidate measurements and Windows/WSL checkpoints. Its default workload list and absolute regression floor are not sufficient for the sub-millisecond scalar controls: explicitly select this workload and investigate repeatable relative changes with the focused microbenchmarks.
5. Record means, deviations, paired changes, launch ranges, allocation/GC, revision, architecture, and toolchain. Inspect representative emitted IL; collect JIT disassembly or a CPU profile if attribution remains uncertain. Never accept a small timing gain within observed noise as proof of improvement.
6. Before merging a milestone, run the broader compiler/shared tests, standalone checks, and configured full performance gate on a suitable host. Classify pre-existing/environmental failures against the frozen baseline. Listener/IPC failures seen in earlier work must be reproduced or rerun on a working host; do not call a partial run fully green. Let CI cover additional supported architectures.

Useful commands, run from the repository root:

```powershell
dotnet build src/SharpTS/SharpTS.csproj -c Release

dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~UnboxedNumberArrayReadTests|FullyQualifiedName~StableNumericHotPathTests|FullyQualifiedName~NumericRestOptimizationTests|FullyQualifiedName~SpreadIntoRestParameterTests'

dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- `
  --filter '*NumericRestBenchmarks*'

# Available after stage 0 adds this benchmark class.
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- `
  --filter '*NumericArrayReadBenchmarks*'

./benchmarks/cross-runtime/run-benchmarks.ps1 -NoBuild `
  -Workloads language-hot-paths -Runtimes compiled,node `
  -Launches 3 -IsolatedWorkloads language-hot-paths `
  -OutputDirectory .perf-language-candidate

./benchmarks/cross-runtime/validate-snapshot.ps1 .perf-language-candidate/snapshot.json
```

These commands are starting selections, not substitutes for stage-specific tests or paired measurements. Configure longer warmup and fresh-process runs for final evidence, and persist the actual benchmark job settings in the report.

## Completion criteria

The tranche is complete when all six stages have their stated semantic, IL, allocation, and measurement evidence; the three original slow cases improve reproducibly; the existing scalar paths retain their behavior; and generic dispatch/array/iterator fallbacks remain covered. Each milestone may ship independently once its gates pass. Any excluded shape or deferred extension must be listed with its remaining measured cost, without relabeling an unfinished later stage as complete.

Update this plan with the delivered commit/PR, before/after evidence, validation results, and limitations for each stage. Do not promise a Node-parity ratio from the noisy investigation baseline.
