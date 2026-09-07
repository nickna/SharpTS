# Dynamic custom iterator implementation plan

Status: implemented, with interpreter environment reuse deferred as allowed in
stage 6. See the [implementation results](custom-iterator-dynamic-control-results.md)
for measurements, validation, and remaining costs.

Improve generic custom iterator performance in compiled and interpreted SharpTS while preserving observable JavaScript behavior. Keep `custom-iterator-dynamic-control.ts` as a generic protocol workload, including its alias and `alias.next = alias.next` assignment. Deliver six independently reviewable stages, with correctness and allocation evidence accompanying each optimization.

**Baseline and success criteria**

The investigation used revision `88378dce04627da968dfde8243c4585b45ed3dfa`, Windows x64, .NET SDK 10.0.400 / runtime 10.0.11, and Node 22.23.2. Three launches of the original workloads produced these medians at 100,000 values:

| Workload | Compiled ms | Interpreter ms | Node ms |
| --- | ---: | ---: | ---: |
| Dynamic custom iterator | 16.4536 | 102.4366 | 0.3710 |
| Stable custom iterator | 0.2198 | 100.1824 | 0.3702 |

A separate three-second-warmed delegate probe of the emitted workload functions measured approximately 368 B/value for the dynamic path and a constant 2,680 B/call for the stable path. Direct invocation of only the dynamic `next` body allocated 288 B/value. That attribution probe excludes the consumer and protocol dispatch; it is not a proposed implementation speedup.

The original launch timings varied substantially. Re-establish a paired baseline before implementation; do not use the table as a strict timing threshold. The existing ignored `.perf-iterator-investigation/` directory contains raw measurements, IL, and a correctness reproducer, but the plan does not depend on those files being present in another checkout.

Success means correct iterator behavior, a measured reduction in dynamic-path allocations and execution time, and preservation of the stable path's constant allocation behavior. Node parity is the longer-term competitive objective; this plan does not claim that any particular stage will achieve it.

**1. Establish durable benchmark coverage and a frozen baseline**

Primary files:

- `benchmarks/cross-runtime/scripts/custom-iterator-dynamic-control.ts`
- `benchmarks/cross-runtime/scripts/custom-iterator.ts`
- New `benchmarks/micro/SharpTS.Microbenchmarks/Benchmarks/CustomIteratorBenchmarks.cs`
- New `benchmarks/micro/SharpTS.Microbenchmarks/Infrastructure/CustomIteratorModuleBenchmark.cs`
- `benchmarks/micro/SharpTS.Microbenchmarks/SharpTS.Microbenchmarks.csproj`
- `tests/SharpTS.Tests/CompilerTests/StableCustomIteratorTests.cs`

Work:

- Supply `n * (n - 1) / 2` as the expected result to both existing `bench` calls. Leave the workload bodies, alias, protocol write, sizes, and timing configuration intact.
- Embed the original workload sources for the microbenchmarks and preserve their imported-module compilation context, following the existing `JsonModuleBenchmark` infrastructure. Resolve workload delegates once and perform module initialization and validation outside measurement.
- Measure stable and dynamic iteration at 1,000, 10,000, and 100,000 values using `MemoryDiagnoser`. Include a separately labeled direct-`next` attribution case and a generic callable with minimal body work to distinguish producer allocation from invocation overhead. Keep reduced probes out of the cross-runtime performance claims.
- Assert that the dynamic case still uses generic protocol dispatch; update this assertion as helper names change. Assert that aliasing alone does not qualify it for the stable custom iterator path.
- Freeze the implementation baseline revision. Record runtime versions, per-launch samples, bytes per operation, Gen0 collections, and representative emitted IL. Measure interpreter allocation separately before changing its allocation behavior.
- Examine warmup convergence using the same workload and invocation route. Any shared-harness change is a separate, versioned methodology change applied to both baseline and candidate.

Acceptance: checksum validation passes; benchmark discovery and smoke compilation pass; baseline results distinguish complete-workload measurements from attribution probes; the dynamic control remains generic.

**2. Capture the iterator's next callable once**

Primary files:

- `src/SharpTS/Compilation/ILEmitter.Statements.cs`
- `src/SharpTS/Compilation/RuntimeEmitter.Iterator.cs`
- `src/SharpTS/Compilation/EmittedRuntime.cs`
- `src/SharpTS/Execution/Interpreter.Statements.cs`
- `tests/SharpTS.Tests/SharedTests/IteratorProtocolTests.cs`

Work:

- Model acquisition as an iterator record containing the receiver, captured `next` value, and completion state where required. Use IL locals in a compiled `for...of`, fields in `$IteratorWrapper`, and persistent state in the interpreted enumerator. No per-step record allocation is needed.
- Read `next` once after invoking `[Symbol.iterator]`, through ordinary property access with getter/proxy semantics. Invoke the captured value with the original iterator as receiver for the remainder of that iteration.
- Capture the value without moving callable validation ahead of its specified call boundary. Keep checks for non-object iterator/result values and completion handling consistent with the protocol.
- Convert synchronous acquisition sites and wrappers that currently repeat lookup. Audit other callers of `InvokeIteratorNext` and its sent-value variant, including `yield*`, async adapters, and spreads, before changing a shared signature. Preserve sent-value forwarding; do not globally cache explicit `obj.next()` property accesses.
- Reuse the captured callable/receiver in the interpreter so each step no longer calls `Bind` anew. Keep lexical-arrow `this` distinct from ordinary method receivers.

Regression cases in both execution modes:

- `next` getter runs once, with the correct receiver; a getter that throws still throws.
- A replacement before acquisition is honored; replacement/deletion during iteration does not replace the captured callable. The reproduced mid-loop replacement must sum to `3`, not `0`.
- Reacquiring the same iterable reads its then-current `next` again.
- Explicit repeated `obj.next()` calls continue observing property mutations.
- Non-callable `next`, primitive results, done/value getter order, and abrupt completions behave correctly. Read `value` only when `done` is false.
- `return` remains looked up at close time. Preserve close behavior for break, body throw, return, continue, and failures during stepping.

Acceptance: the mutation regression passes in both modes; lookup occurs once per acquisition; relevant iterator, generator, and async adapter tests pass. Measure performance separately from the correctness benefit.

Protocol reference: [GetIteratorDirect](https://tc39.es/ecma262/multipage/abstract-operations.html#sec-getiteratordirect) and [IteratorNext](https://tc39.es/ecma262/multipage/abstract-operations.html#sec-iteratornext).

**3. Remove argument arrays from zero-argument method dispatch**

Primary files:

- `src/SharpTS/Compilation/RuntimeEmitter.TSFunction.cs`
- `src/SharpTS/Compilation/RuntimeEmitter.Objects.Invocation.cs`
- `src/SharpTS/Compilation/RuntimeEmitter.Iterator.cs`
- `src/SharpTS/Compilation/EmittedRuntime.cs`
- New `tests/SharpTS.Tests/CompilerTests/ZeroArgumentMethodDispatchTests.cs`

Work:

- Add a receiver-aware zero-argument helper, such as `InvokeMethodValue0(receiver, callable)`, and route captured iterator calls through it.
- For supported emitted signatures, use the existing cached `MethodInvoker` with a fixed-arity overload or a cached typed delegate. Avoid both `new object[0]` and the second array used to prepend `this`. Do not introduce reflection lookup or delegate construction per call.
- Cache eligibility metadata when constructing the callable. Guard receiver convention, defaults, rest parameters, `arguments`, and conversion requirements. Route unsupported callable kinds and signatures through existing general dispatch, using a shared empty argument array where safe.
- Preserve ordinary/lexical `this`, bound functions, proxy apply traps, exception identity, and reentrant calls. Avoid shared mutable argument buffers.

Acceptance: allocation coverage with a minimal nonallocating callee demonstrates no per-call argument-array allocation for eligible signatures; iterator IL uses the helper; fallback call shapes remain correct. The removed arrays represent an estimated 56 B/value on the measured x64 runtime, but confirm the actual delta rather than treating that estimate as portable.

**4. Compact ordinary iterator result objects**

Primary files:

- `src/SharpTS/Compilation/RuntimeFeatureDetector.cs`
- `src/SharpTS/Compilation/RuntimeFeatureSet.cs`
- `src/SharpTS/Compilation/RuntimeEmitter.CompactObjectRecord.cs`
- `src/SharpTS/Compilation/ILEmitter.Properties.Literals.cs`
- `src/SharpTS/Compilation/RuntimeEmitter.Iterator.cs`
- `tests/SharpTS.Tests/CompilerTests/CompactObjectRecordTests.cs`
- `tests/SharpTS.Tests/SharedTests/IteratorProtocolTests.cs`

Work:

- Register eligible ordinary result-literal shapes before runtime type emission, independently of stable iterator binding eligibility. Start with the existing `{ value: number, done: boolean }` shape and reuse compact-record infrastructure.
- Emit a fresh reference object with typed value/done storage and the existing materialization mechanism. Generic observable results retain object identity; the stable iterator's private value-type result remains a separate optimization.
- Add guarded direct-field access in result extraction for recognized, unmaterialized records. Preserve normal property reads for all other results, including getters, proxies, prototype changes, and materialized records. Re-evaluate guards for each result.
- Preserve general JavaScript addition in the consumer at this stage. A numeric result representation does not establish that every future result of a mutable protocol has that shape.

Regression cases: retain multiple results and compare identity; mutate fields; inspect keys/descriptors; delete/redefine properties; use inherited properties and accessors; alternate ordinary and compact result shapes; replace the iterator before acquisition with one returning strings or objects.

Acceptance: the original dynamic workload creates no result dictionary on its admitted path, direct-`next` bytes/value decrease materially, and full-workload allocation corroborates the reduction. Unknown-result fallback, compact-record semantics, and stable iterator allocation tests pass.

**5. Keep proven numeric captures unboxed on the dynamic path**

Primary files:

- `src/SharpTS/Compilation/StableCustomIteratorAnalyzer.cs`
- New `src/SharpTS/Compilation/StableNumericFunctionCaptureAnalyzer.cs`
- `src/SharpTS/Compilation/ILCompiler.cs`
- `src/SharpTS/Compilation/CompilationContext.Closures.cs`
- `src/SharpTS/TypeSystem/TypeMap.cs`
- New `tests/SharpTS.Tests/CompilerTests/StableNumericFunctionCaptureTests.cs`

Work:

- Extract the shared-function-field proof from stable iterator analysis and run it after closure analysis, before display-class emission. Use resolved binding identity rather than identifier spelling, and cover both ordinary and module compilation entry points.
- Initially admit definitely initialized numeric bindings with a complete numeric-write proof and supported synchronous closure access. Retain conservative fallback for eval, uncertain initialization, undefined-reachable values, unsupported suspension, and unproven writers.
- Preserve live shared storage. `StableNumericLoopCaptureAnalyzer` handles a different snapshot case; reuse its proof utilities where appropriate without converting mutable captures into snapshots.
- Apply the representation proof to `current` and `n` even when the containing iterator is aliased or its callable can be replaced. Check every reader/writer and emit compatible field access throughout the closure graph.
- Leave checksum accumulation dynamic initially. If profiling still identifies it as significant, add a separate guarded numeric addition change with a fallback that preserves string concatenation and conversion side effects. Do not infer numeric runtime values solely from TypeScript annotations.

Acceptance: emitted dynamic iterator closures contain double fields for the admitted bindings and no boxing on `current` updates. Tests cover shadowing, live updates, retained closures, multiple readers/writers, undefined inputs, NaN/infinity, and fallback cases. Full-workload bytes/value and runtime improve without admitting the dynamic iterator to stable protocol specialization.

**6. Reduce remaining interpreter allocation and validate the combined result**

Primary files:

- `src/SharpTS/Runtime/Types/SharpTSFunction.cs`
- `src/SharpTS/Execution/Interpreter.Statements.cs`
- `src/SharpTS/Runtime/RuntimeEnvironment.cs`
- `tests/SharpTS.Tests/SharedTests/IteratorProtocolTests.cs`
- `tests/SharpTS.Tests/SharedTests/ForLoopPerIterationBindingTests.cs`
- `benchmarks/micro/SharpTS.Microbenchmarks/Benchmarks/CustomIteratorBenchmarks.cs`

Work:

- Re-profile the interpreter after stage 2; distinguish the binding cost already removed from remaining function-call environments, result objects, and loop environments.
- Where binding remains necessary, avoid constructing intrinsic function metadata that is immediately discarded when property storage is shared. Test shared function properties and receiver behavior explicitly.
- Optimize per-value loop storage only if profiling supports it and lexical analysis proves the binding cannot escape through closures, eval, or debugger observation. Preserve fresh environments when required and restore outer bindings on all exits. If this proof requires broader infrastructure, record that portion as deferred with evidence rather than applying blanket environment reuse.
- Re-run the original stable/dynamic workloads across compiled, interpreter, and Node. Run paired frozen-baseline/candidate measurements using `scripts/perf-local.ps1`; use at least three alternating launches and five for the final report when practical.
- Check affected neighboring paths: custom iterator consumers, generator delegation, async-from-sync iteration, compact objects, ordinary zero-argument calls, and closure captures. Run focused tests after each stage and the full test suite for the final combined change.

Acceptance: both runtime modes pass the semantic matrix, targeted allocation reductions are measured, the stable path keeps constant allocations, and relevant neighboring cases show no reproducible regression beyond the lab's configured noise thresholds. Record remaining costs and uncertainty. Do not refresh published snapshots as an incidental part of implementation.

**Validation commands**

These commands are intended for implementation checkpoints; they have not been run as part of creating this plan. Expand the test filter with new classes as stages land.

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --filter "FullyQualifiedName~IteratorProtocolTests|FullyQualifiedName~StableCustomIteratorTests|FullyQualifiedName~CompactObjectRecordTests|FullyQualifiedName~ForLoopPerIterationBindingTests"

dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --smoke
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --filter '*CustomIterator*'

./benchmarks/cross-runtime/run-benchmarks.ps1 -Smoke -Workloads custom-iterator-dynamic-control,custom-iterator
./benchmarks/cross-runtime/run-benchmarks.ps1 -Workloads custom-iterator-dynamic-control,custom-iterator -Runtimes compiled,interpreter,node -Launches 3 -OutputDirectory .perf-custom-iterator-candidate

# Requires the frozen baseline checkout configured by benchmarks/local-perf/README.md.
./scripts/perf-local.ps1 -Action measure -Platforms windows -Workloads custom-iterator-dynamic-control,custom-iterator -Runs 5 -Enforce

dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release
```

Use IL verification for changed emitted signatures and control flow. For shared runtime helper changes, include the repository's applicable standalone and Native AOT validation in the final release gate; no new runtime code generation may become necessary merely to execute a compiled program.

**Delivery and dependencies**

Stage 1 establishes measurements for every later stage. Stage 2 establishes correct iterator acquisition; stage 3 builds on its captured-callable path. Stages 4 and 5 target independent representation costs and should remain separate changes for attribution. Stage 6 compares the combined result and decides whether additional interpreter scope work is justified.

Each stage should include its implementation, regression tests, before/after allocation evidence, representative IL, and focused timing results. Reassess priorities from the new measurements rather than adding speculative optimizations. The final report should separate measured speedups from estimates and list any deferred work explicitly.
