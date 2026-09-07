**Worker allocation scaling implementation plan**

Status: implemented and measured, then integrated with main's overlapping optimizations. See the [results and retained evidence](../performance/worker-allocation-scaling.md) for historical Windows/Linux comparisons, integration details, completed validation, and explicit limits. The bounded escape proof now selects an additional specialization ahead of main's general numeric-literal path.

Reduce the time and allocation required by `worker-allocation-scaling.ts`, preserve JavaScript semantics, and explain worker scaling with repeatable measurements. Keep the original 20,000-record workload, its interface declaration, and persistent-worker behavior as the end-to-end regression case.

The investigation used revision `88378dce04627da968dfde8243c4585b45ed3dfa`, Windows x64, .NET 10.0.11, and Node 22.23.2. Three-launch medians were:

| Execution | Compiled ms/job | Node ms/job | Interpreter ms/job |
| --- | ---: | ---: | ---: |
| Direct | 18.797 | 0.643 | 102.776 |
| 1 worker | 23.928 | 0.426 | 108.838 |
| 2 workers | 20.071 | 0.235 | 93.415 |
| 4 workers | 7.431 | 0.146 | 87.215 |

Timing varied substantially, including with longer sampling and in Node. These numbers establish priority, not release budgets. A separate compiled-kernel probe measured approximately 11.4 MB allocated per job. An equivalent record type alias enabled guarded field reads and reduced that by approximately 0.96 MB; its observed timing improvement needs paired confirmation. Interpreted message-only round trips measured approximately 30 ms, and their delivery path uses a 10 ms polling timer.

Preserve this evidence summary in the plan; implementation must generate fresh evidence and must not depend on the investigation's ignored local artifacts. Use the existing [local performance workflow](../../benchmarks/local-perf/README.md) for paired baseline/candidate measurements.

| Change | Deliverable | Dependency |
| --- | --- | --- |
| 1 | Benchmark attribution, independent checksums, and reproducible case selection | None |
| 2 | Guarded compact-record reads through interface types | 1 |
| 3 | Direct construction of final storage for ordinary array literals | 1 |
| 4 | Numeric literal storage and unboxed numeric consumers | 2 and 3; array-boundary audit |
| 5 | Event-driven interpreted-worker message delivery | 1; independent of compiler changes |
| 6 | Integrated performance validation and documented results | 2–5 |

Land changes separately so each benefit and any regression can be attributed. Steps 2 and 3 come first because they address observed costs using existing runtime representations. Step 5 can be implemented independently. Step 4 is a larger representation change and must satisfy the existing [number-array design decision](../design/number-array-unboxing.md).

Implemented behavior:

- The original interface kernel is unchanged. Both original benchmark paths validate `800178000`. Separate serial/worker controls cover 8,192/8,193/20,000 records per partition; the harness supports validated timing budgets and selection before pool creation.
- Compatible interface reads select existing carriers by field names and guard carrier type, materialization, and descriptors. Numeric consumers retain doubles; raw consumers preserve unexpected dynamic values.
- Ordinary dense literals construct their final `$Array` directly through the existing private capacity/append helpers. Spreads, holes, and suspension retain their existing paths.
- A conservative local-record use analysis enables owned numeric buffers for the canonical nested literals. Escaping/mutated arrays remain boxed from construction. See the [boundary audit](../design/local-record-numeric-arrays.md). The escaping-construction microbenchmark is a fallback control; it is not an additive decomposition of the optimized full kernel. A new transition benchmark is deferred because this change introduces no new escaping numeric representation.
- Array length optimization preserves the existing routing for promoted queues, flattened rest, arguments, and other special receivers. Numeric index fallbacks retain fractional, NaN, negative, and large property keys.
- Interpreted workers coalesce enqueue notifications into bounded drains on the owning event loop. Message and stdin queues both wake delivery; scheduling ownership is released in `finally` and pending work is rechecked.

1. **Establish attribution and correctness measurements.**

   Primary files: [worker-allocation-scaling.ts](../../benchmarks/cross-runtime/scripts/worker-allocation-scaling.ts), [allocation-kernel.ts](../../benchmarks/cross-runtime/scripts/workers/allocation-kernel.ts), [bench.ts](../../benchmarks/cross-runtime/scripts/lib/bench.ts), [run-benchmarks.ps1](../../benchmarks/cross-runtime/run-benchmarks.ps1), and the cross-runtime README. Add an allocation-focused class to the microbenchmark project, following its existing compilation and typed-delegate infrastructure.

   Pass the independent expected checksum `800178000` to both `bench` and `benchAsync`. Retain worker preflight validation. For variable-size controls, calculate expectations independently outside timing, including nonzero starts and uneven partitions; do not use the implementation under test as its own oracle.

   Embed or load the canonical kernel into the microbenchmark module graph so it stays identical to the cross-runtime workload. Initialize modules and bind typed delegates before measurement. Measure the full kernel, construction alone, and traversal of prebuilt records separately. Include interface and equivalent type-alias controls. Record bytes per operation and Gen0/1/2 collections with MemoryDiagnoser; use a separate process-wide diagnostic for worker allocation and pause duration, because parent-thread allocation counters miss worker allocations.

   Add a serial partitioned control that calls the same kernel over the same ranges as the worker pool. Add boundary coverage around 8,192/8,193 records per partition and larger sustained inputs. Growing x64 reference lists cross the default large-object-heap threshold at this capacity transition. Report both total records and records per partition so changing GC behavior cannot be mistaken for pure parallel scaling.

   Make warmup and sampling budgets configurable with validated values and unchanged defaults. Add selection of worker count before pool creation and preflight, so filtering really isolates an individual case. Preserve existing BENCH output, default case identities, and snapshot compatibility. Put variable-size controls in separately identified cases with unambiguous input metadata. Rotate case/runtime order through a recorded schedule and run timed processes sequentially.

   Acceptance: original cases still compile and validate; selected cases do not create unrelated pools; listing cases performs no worker work; snapshot contract checks pass; baseline evidence records revision, runtime versions, GC configuration, inputs, launch order, variance, and measurement method. Invalid or unavailable memory readings are recorded as unavailable, not zero. Obtain stable paired measurements before using elapsed time as an acceptance gate.

2. **Extend compact-record property reads to interfaces.**

   Primary files: [ILEmitter.Properties.cs](../../src/SharpTS/Compilation/ILEmitter.Properties.cs), [JsonSerializationShape.cs](../../src/SharpTS/Compilation/JsonSerializationShape.cs), and existing compact-record feature/shape analysis. Use interface metadata from [TypeInfo.cs](../../src/SharpTS/TypeSystem/TypeInfo.cs); avoid changing assignability rules or replacing interfaces with records in the type checker.

   Add a bounded compiler-side shape-resolution helper for eligible interfaces, including supported inherited members and resolved generic substitutions. Reuse existing shape fingerprints and typed-record read emission. If an interface's shape is ambiguous, recursive beyond supported limits, optional/open in an unsupported way, or has incompatible index/call signatures, retain generic lookup.

   Guard the actual runtime carrier and property state, then load numeric slots without boxing and reference slots directly. Preserve generic fallback for class instances, dictionaries, proxies, accessors, and materialized/mutated records. Evaluate receivers exactly once and retain nullish/error behavior. A compatible interface can contain extra runtime properties or a different property order: do not assume declaration order establishes an exact carrier match.

   Extend [CompactObjectRecordTests.cs](../../tests/SharpTS.Tests/CompilerTests/CompactObjectRecordTests.cs) and relevant shared interface tests. Cover aliases, inherited/generic interfaces, extra/reordered properties, class implementations, widening to `any`, mutation/deletion, descriptors, and nullish values. Include exported/imported functions because workers compile a separate module graph.

   Acceptance: the unchanged interface kernel emits guarded compact field reads; supported numeric reads stay unboxed; fallbacks remain behaviorally correct in both execution modes; emitted IL verifies. The allocation gap to the type-alias control should close, with approximately 48 bytes per record as the investigation's explanatory target rather than a platform-independent constant. Measure direct and compiled-worker execution separately.

3. **Construct ordinary array literals directly in final storage.**

   Primary files: [ILEmitter.Properties.Literals.cs](../../src/SharpTS/Compilation/ILEmitter.Properties.Literals.cs), [RuntimeEmitter.Arrays.cs](../../src/SharpTS/Compilation/RuntimeEmitter.Arrays.cs), [RuntimeEmitter.TSArray.cs](../../src/SharpTS/Compilation/RuntimeEmitter.TSArray.cs), and runtime helper registration as needed.

   Audit existing capacity/append helpers added for rest arguments before introducing another constructor. Add a literal-specific boxed-storage path that allocates the final `$Array` at known capacity and fills its backing storage directly. Do not route literal initialization through observable or replaceable `Array.prototype.push`. Preserve the existing general `CreateArray` callers unless separately covered.

   Initially support fixed-length literals without spreads or holes, including mixed values. Preserve fresh identity for each evaluation, left-to-right element evaluation, one evaluation per expression, and exception behavior. Handle `await`/`yield` with valid spill/local lifetime rules or conservatively retain the existing path. Keep numeric elements boxed in this change so construction savings are attributable independently of numeric storage.

   Add targeted array-literal semantic/IL coverage and exercise existing rest, spread, sparse-array, descriptor, and standalone-assembly tests affected by helper reuse.

   Acceptance: the eligible four-element literal avoids the temporary `object[]` and intermediate list/copies; allocated bytes decrease with a clear object-allocation explanation; final array identity, contents, and logical length remain correct. The original workload improves without changing its source or allocation pattern.

4. **Keep eligible numeric literals and consumers unboxed.**

   Primary files: the literal and array emitters above, [ILEmitter.Properties.cs](../../src/SharpTS/Compilation/ILEmitter.Properties.cs), runtime feature detection, [NumericStorageSafetyTests.cs](../../tests/SharpTS.Tests/CompilerTests/NumericStorageSafetyTests.cs), and [UnboxedNumberArrayReadTests.cs](../../tests/SharpTS.Tests/CompilerTests/UnboxedNumberArrayReadTests.cs).

   Before enabling a new literal path, enumerate every boundary the array can reach: object/interface fields, aliases, parameters/returns, modules, callbacks, iterators, array built-ins, serialization, worker cloning, and .NET interop. Identify which consumers understand numeric storage and which require `EnsureBoxed` on the same array object. Inherited `List<object>` consumers require special scrutiny because base-list operations cannot be intercepted by overriding a JS array accessor.

   Reuse the existing `$Array` numeric store, conversion helper, and guarded numeric reads. Create eligible nonempty numeric literals directly in numeric storage; do not first box and then convert them. Preserve identity through every transition. Start with dense supported literals; retain the boxed path where the boundary audit cannot prove safe behavior. The benchmark's nested `values` arrays must be eligible for the change to meet this workload's objective.

   Preserve numeric results through arithmetic consumers rather than boxing at merge points. Remove the observed `records.length` box/ConvertToNumber round trip in numeric contexts while preserving dynamic fallback and mutation semantics. Reuse existing numeric-read fast paths instead of introducing competing ones.

   Test aliasing through object/interface fields and `any`, nonnumeric writes, holes/deletion, length changes, fractional/negative/large indices, out-of-bounds reads, NaN and negative zero, freeze/seal/descriptors, callbacks with mutation, interop, and structured cloning. Include a forced-deoptimization benchmark to quantify transition costs.

   Acceptance: eligible literals do not allocate four boxed element values; hot numeric reads and numeric length consumers remain unboxed; bytes per job decrease further; fallback/deoptimization preserves identity and behavior. Run relevant Test262 array cases and existing standalone/tree-shaking checks. If the boundary audit exposes unsupported consumers, repair them or keep those paths boxed and document the remaining scope; do not substitute a separately mutable `List<double>`.

5. **Deliver interpreted-worker messages when enqueued.**

   Primary files: [SharpTSWorker.cs](../../src/SharpTS/Runtime/Types/SharpTSWorker.cs), the interpreter's scheduling entry points where required, [WorkerThreadsTests.cs](../../tests/SharpTS.Tests/SharedTests/WorkerThreadsTests.cs), and [CliWorkerThreadsTests.cs](../../tests/SharpTS.Tests/IntegrationTests/CliWorkerThreadsTests.cs).

   Replace `WorkerMessageHandler`'s periodic timer with a drain scheduled on the owning interpreter loop. Publish the scheduler only when bootstrap is ready; buffer earlier messages and schedule their delivery at the existing readiness boundary. Schedule stdin delivery on enqueue as well, since the current polling loop pumps both queues.

   Coalesce scheduling with an atomic scheduled flag. Drain on the interpreter thread, clear the flag in `finally`, then recheck queues and reschedule to avoid losing a message arriving during flag reset. Preserve queue order, exception/messageerror handling, ref/unref, and teardown. Bound drain work or reschedule when necessary so sustained bursts do not starve timers or termination. Audit parent delivery and promise continuations for remaining timer-based latency after this change.

   Add deterministic race tests with barriers/hooks for startup enqueue, enqueue during drain completion, bursts, stdin EOF, callback failures, and terminate/unref races. Verify thread affinity and prompt shutdown without relying on millisecond wall-clock assertions in unit tests.

   Acceptance: eligible interpreted delivery uses no recurring polling timer, no messages or wakeups are lost, and the existing worker behavior passes in both modes. Measure message-only latency/throughput, lifecycle, and allocation scaling. Require a repeatable interpreted message-latency reduction; verify compiled worker performance remains within measurement noise. Do not require an arbitrary submillisecond threshold on shared CI hosts.

6. **Validate the combined result and record remaining costs.**

   Run focused tests and IL verification after each implementation change. Once integrated, run the Release test suite, cross-runtime smoke checks, snapshot-contract checks, and relevant conformance coverage. Execute each timed workload without concurrent builds/tests. Keep the test project's server-GC configuration separate from the compiled application's workstation-GC baseline; test-host throughput is not the application benchmark.

   Collect paired baseline/candidate evidence with at least five independent launches per retained cell on a quiet target host, using the same harness on both revisions. Validate Windows first, then the WSL/Linux environment supported by the local lab. If repeated controls still drift enough to obscure the change, report the timing result as inconclusive and retain allocation/IL evidence rather than claiming a speedup.

   Re-run the original interface workload at 1/2/4 workers, serial partition controls, boundary sizes, and forced-deoptimization cases. Include adjacent array/rest/object benchmarks and the existing worker message, throughput, and lifecycle workloads. Explain any repeatable regression outside measured noise before retaining the change.

   Record bytes per job, collection counts, pause duration, per-launch timings/variance, process-wide memory where valid, and observed scaling. Require the combined compiler changes to reduce allocation and demonstrate repeatable end-to-end improvement for the intended workload; no percentage speedup is promised from the noisy investigation. Compare Node for context as well as the frozen SharpTS baseline for attribution.

   Measure `adaptive` GC separately with valid latency and memory readings. Keep the current GC default unless a subsequent, broader profile decision justifies changing it. Worker startup caching, large-message transfers, precomputed labels, and removing the benchmark's record/array allocations are outside this implementation scope.

   Update this plan with implemented behavior, exact validation commands, result artifacts, platform limits, and any deferred boundary cases. Mark a step complete only when its implementation and acceptance evidence are both present.
