# Materialized object destructuring implementation plan

Status: implemented and validated. See the [performance and validation results](../../benchmarks/local-perf/materialized-object-destructuring.md) for measured gains, completed gates, baseline failures, and platform limits.

Implemented stages 1–4 below. Guarded snapshots now support dictionary receivers and canonical materialized-carrier storage, including numeric shapes without compact metadata. The integer reduction retains its exactness proof; other numeric inputs use the original floating-point addition tree. Fixed numeric patterns that remain inside loops share a snapshot, and general dictionary dispatch uses one descriptor lookup. Added checksum controls, configurable warmup, typed-delegate microbenchmarks, representation/IL assertions, allocation checks, and semantic regressions.

Delivered benchmark fixtures in `acce19be`, guarded snapshots with compiler tests in `80747bf7`, and the independent dictionary-dispatch change in `47f09033`. The original benchmark's median improved about 134× on Windows and 128× on Linux, with no allocation growth as the iteration count increases. Stage 5 includes full core suites, the remaining Windows full-gate checks, Linux Native AOT compile/execute, five paired launches per platform, and allocation measurements; the results document records the existing failures and isolated retries explicitly.

Reduce compiled execution time for dictionary-backed and materialized compact-record destructuring. Deliver the invariant-loop optimization first, then improve repeated reads that cannot move outside a loop. Preserve JavaScript evaluation order, numeric behavior, property semantics, and cancellation.

The investigation at `af72038b83e9c88449b33ca42262f3f8dc156ea7` found that `object-destructure-materialized.ts` immediately allocates a dictionary. Each iteration falls back to two general property lookups and numeric conversions. Allocation remains 288 bytes per call at n = 0 through 100,000. With 1,500 ms warmup and three launches on Windows, median compiled times at n = 100,000 were 2.801 ms for the current body, 0.093 ms with reads manually hoisted, and 0.023 ms without dynamic mutation. These diagnostic controls establish opportunity, not guaranteed implementation results. Generate fresh paired baseline/candidate evidence.

**1. Establish durable benchmarks and correctness coverage.**

Primary files:

- `benchmarks/cross-runtime/scripts/object-destructure-materialized.ts`
- `benchmarks/cross-runtime/scripts/object-destructure.ts`
- New `benchmarks/cross-runtime/scripts/object-destructure-carrier-materialized.ts`
- `benchmarks/cross-runtime/scripts/lib/bench.ts` and the cross-runtime README
- New `benchmarks/micro/SharpTS.Microbenchmarks/Benchmarks/ObjectDestructuringBenchmarks.cs` and `TypeScriptSources/ObjectDestructuring.ts`
- `tests/SharpTS.Tests/CompilerTests/StableDestructuringLoadTests.cs`

Keep the existing benchmark name and loop body, add the expected result `n * 3`, and document its dictionary representation. Add an actual materialization fixture using a stable discarded array push of `{ x: 1, y: 2 }`, followed by retrieving the object and assigning `extra` through `any`. The investigation verified that this construction emits a compact-record constructor before the write. Assert the intended storage representation in compiler tests so both workloads remain meaningful.

Add controls for direct reads, manually hoisted reads, compact records, fractional values, varying receivers, and a property updated on each iteration. Give every measured case a checksum. Keep the actual materialization case in a separate workload to avoid changing shape analysis of existing cases. Add microbenchmarks using cached typed delegates, setup outside timing, and MemoryDiagnoser; include an idiomatic C# numeric-loop control.

Introduce a bounded, optional `SHARPTS_BENCH_WARMUP_MS` override with the current default retained. Document a 1,500 ms investigation setting, validate configuration, and record the setting with run evidence. Use identical harness code and settings on baseline and candidate, including new workloads; do not compare candidate-only fixtures with an older harness silently. Use an isolated case setting where earlier cases affect tiering.

Done when all fixtures compile, checksums pass, representation assertions pass, and fresh baseline timings/allocations are recorded. No compiler optimization is required in this stage.

**2. Hoist invariant dictionary reads into guarded numeric locals.**

Primary files: `src/SharpTS/Compilation/ILEmitter.Destructuring.Reduction.cs`, with supporting changes in `ILEmitter.Destructuring.cs` and `EmittedRuntime.cs` only as needed.

Refactor `StableObjectDestructureReduction` to describe property names, source/bound bindings, accumulator, and addition order separately from compact-record field metadata. Dictionary eligibility must not require an emitted compact carrier. Preserve existing compact-record specialization.

Initially retain the existing narrow loop structure: a zero-initialized integer counter, numeric parameter bound, increment by one, fixed numeric destructuring bindings, and a single accumulator expression. Reject defaults, rest, computed keys, nested patterns, calls, suspension, exception regions, and writes to the receiver/source/bound. Resolve lexical bindings correctly; name shadowing or binding collisions must not invalidate the proof. A mutation completed before the loop is allowed. Do not interpret `const` as proof of immutable properties.

Emit the following control flow:

1. Perform the existing cancellation and loop-entry checks before acquiring values; a zero-trip loop must not introduce reads or coercions.
2. For a dictionary receiver, call the existing `PDSHasPropertyDescriptors` guard once. Conservatively reject any attached descriptor table.
3. Read each required own key with `TryGetValue`; require boxed doubles and unbox into temporary double locals. Do not call general conversion while probing. All failed guards branch to the original loop before committing observable state.
4. Feed these locals into the existing safe-integer reduction when its term, bound, accumulator, negative-zero, and complete-result guards pass.
5. Otherwise, for supported loop bounds and validated numeric values, emit a hoisted double loop with the original left-to-right addition order. Unsupported cases take the original loop. Never replace repeated floating additions with multiplication or reassociate terms without the integer proof.

Keep the existing conservative program-wide descriptor eligibility restriction initially; accepting programs containing unrelated descriptor operations is a later widening, not necessary for this benchmark. Runtime descriptor guards still protect dictionary receivers. Preserve cancellation checks and accumulator flushing in every emitted loop path.

Done when qualifying dictionary loops perform no property lookup, descriptor query, or numeric conversion inside the loop, existing compact loops retain their optimized path, and the correctness matrix below passes. This is the first independently useful optimization milestone.

**3. Reuse canonical storage for materialized compact records.**

Primary files: `src/SharpTS/Compilation/RuntimeEmitter.CompactObjectRecord.cs`, `EmittedRuntime.cs`, and `ILEmitter.Destructuring.Reduction.cs`.

Emit a per-carrier nonmaterializing `TryGetMaterializedDictionary` helper, using the existing type-wide negative test and weak table. Register its method metadata in `EmittedRuntime`. It returns existing canonical storage only; it must neither call `EnsureMaterialized` nor allocate storage for an untouched record.

Extend guarded value acquisition from stage 2 to use this dictionary. Check descriptors on the original carrier identity, which is the descriptor-store key; checking the backing dictionary alone is insufficient. Never read original typed slots after materialization because later writes may exist only in canonical storage. Keep the direct-field path for unmaterialized records and the ordinary fallback for missing keys or nonnumeric values.

Done when the verified transition fixture uses the optimized loop and mutations to x/y made before entry are observed. Test materialized and untouched siblings of the same shape together. Acquiring an untouched receiver must not create a dictionary or add a per-call allocation.

**4. Reduce lookup overhead when reads must remain inside the loop.**

Primary files: `src/SharpTS/Compilation/ILEmitter.Destructuring.cs`, `RuntimeEmitter.Objects.GetPropertyBranches.cs`, and associated property-descriptor helpers if needed.

Extend the stable destructuring source binding with a cached typed dictionary receiver, including canonical storage for eligible materialized carriers. For a fully validated numeric, fixed-key pattern with no intervening guest execution, share one descriptor guard across its own-property reads. Reacquire values for every destructuring operation; varying receivers and property updates must remain observable.

Fall back without re-evaluating the source expression. Share guard state only where all intervening reads/conversions are proven side-effect-free. If a fallback invokes a getter, default expression, or coercion, invalidate cached assumptions before subsequent bindings. Leave unsupported patterns on the existing path.

As a separately reviewable change, consolidate the dictionary branch's getter and descriptor queries into one descriptor lookup. Preserve callable getters, getter explicitly set to undefined, setter-only accessors, live dictionary values for mirrored data descriptors, and prototype fallback on an own-property miss. Measure this change on varying-receiver and mutating controls to establish its benefit independently of invariant hoisting.

Done when controls that require repeated reads improve measurably, while property-order and mutation tests pass. Keep this stage separable from stages 2–3 if the wider dispatch change needs more validation.

**5. Validate semantics, emitted code, and performance before delivery.**

Extend existing compiler/shared tests rather than introducing wall-clock assertions in unit tests. Use compiled/interpreted parity and explicit expected results. Include:

| Area | Required cases |
| --- | --- |
| Representation | Dictionary from construction; actual materialized carrier; untouched/materialized siblings; shapes with no usable compact metadata |
| Mutation | Add unrelated property; update x/y before entry; update/delete inside the loop; mutation through an alias or callback; changing receivers |
| Property behavior | Missing own key; inherited data/getter; x getter changes y; setter-only accessor; proxy; getter source order and single source evaluation |
| Numeric behavior | Fractions; fractional/negative/NaN bounds; zero-trip loops; NaN/infinity values; negative-zero accumulator; safe-integer boundary and overflow rounding |
| Fallback effects | Numeric annotation with runtime nonnumeric value; observable coercion; default/rest/nested pattern rejection; lexical shadowing |
| Runtime behavior | Cancellation on optimized paths; accumulator flushing; standalone emitted execution and IL verification |

For allocation coverage, compare small and large iteration counts after warmup. Require no allocation growth proportional to n; allow fixed setup allocations and platform bookkeeping. For IL coverage, inspect the optimized loop region rather than forbidding fallback calls anywhere in the method. Verify fallback calls remain reachable for rejected inputs.

Run focused tests first, then the repository compiler checkpoint gate and full hermetic gate once the implementation is ready. Include the existing compact-record, descriptor, proxy, and shared destructuring tests when the shared runtime dispatcher changes. Use the local performance lab on Windows and WSL with matching harness settings; complete its configured Native AOT/standalone validation and let CI cover remaining supported architectures. Report an unavailable platform as unverified.

Measure five alternating baseline/candidate launches of the dictionary case, actual carrier-materialization case, compact/fractional/varying/direct controls, and unrelated-dynamic-mutation workload. Record means, medians, dispersion, allocations, runtime versions, revision, warmup, and relevant emitted IL. Extend the baseline measurement fixtures consistently rather than dropping a missing workload. Separate true setup cost from steady-state reads in microbenchmarks.

The target for the original case is at least a tenfold improvement against a fresh same-host baseline, supported by the approximately thirtyfold manual-hoist control. Treat this as an engineering target, not a promised absolute latency. Structural removal of repeated lookups and a statistically credible timing improvement are required. Investigate missed targets and material regressions using the local lab thresholds; for sub-0.05 ms controls establish a suitable measured noise floor rather than letting the default absolute threshold hide a regression. The competitive objective remains Node parity, especially for the guarded integer case.

Deliver cohesive commits in stage order, with correctness tests accompanying each optimization. A final review should include the before/after table, storage-path evidence, fallback coverage, and platform limitations. Avoid preserving typed slots across dynamic expandos or introducing a general loop-invariant-code-motion framework in this sequence; both are broader follow-ups.
