# Language hot paths implementation plan

Status: implemented. Measurement and validation results are recorded below.

## Objective and scope

Reduce compiled execution costs for indirect, spread, and dynamic-index numeric rest calls while preserving JavaScript semantics and the existing direct-call optimizations. Deliver independently reviewable changes with measured allocation and timing evidence.

Interpreter optimization, generator lowering, and number formatting are outside this implementation sequence. Revisit them after rest-call improvements have been measured.

## Investigation baseline

The investigation ran `language-hot-paths.ts` at revision `7da32f04`, using Release SharpTS, .NET SDK 10.0.400, and Node 22.23.2. Each runtime completed three launches across all 33 cases. At n=100,000, medians of per-launch means were:

| Case | Compiled ms | Node ms |
| --- | ---: | ---: |
| Indirect numeric rest | 24.140 | 0.0513 |
| Dynamic-index numeric rest | 19.528 | 0.0495 |
| Spread numeric rest | 22.091 | 0.3968 |
| Stable numeric rest | 0.0549 | 0.0501 |
| Flattened rest control | 0.0549 | 0.0509 |

There was substantial launch variation, including slowdowns in scalar controls. These results establish priority, not precise performance budgets or allocation attribution. Local evidence is in `.perf-language-investigation/results.txt` and `snapshot.json`; those ignored artifacts are not required to use this plan. Generate fresh evidence against the implementation baseline.

Source inspection identifies boxing and intermediate collections in ordinary rest packing. It does not establish how much time belongs to packing, dispatch, indexed reads, or GC; stage 1 must separate those costs.

## Delivery sequence

The implementation follows these five stages. Stages 3 and 4 reuse existing fixed-arity companions; stage 5 builds on the ordinary packing changes. The changes are submitted together for review.

### 1. Establish attribution benchmarks and semantic coverage

Files:

- `benchmarks/cross-runtime/scripts/language-hot-paths.ts`
- New `benchmarks/micro/SharpTS.Microbenchmarks/Benchmarks/NumericRestBenchmarks.cs`
- New `benchmarks/micro/SharpTS.Microbenchmarks/TypeScriptSources/NumericRest.ts`
- `tests/SharpTS.Tests/CompilerTests/StableNumericHotPathTests.cs`
- Existing shared spread/rest/arguments tests, extended where appropriate

Work:

- Add allocation-diagnosed microbenchmarks using typed delegates and initialization outside measurement, following existing microbenchmark infrastructure.
- Measure fixed parameters, direct scalar rest, ordinary rest packing with minimal callee work, indexed rest reads, indirect dispatch, and spread expansion separately. Keep matching checksums and arithmetic trees.
- Retain the existing immutable alias and constant-start dynamic-index cases as optimization targets. Add fallback controls with a runtime-selected callable and varying start indices. Inspect emitted calls to verify these controls actually exercise fallback paths.
- Add escaping and mutated rest-array cases so allocation elimination cannot silently change array identity or contents.
- Preserve fractional accumulator seeds. Add content-sensitive formatting validation only if editing that case; formatting is not an optimization target here.
- Run isolated cross-runtime cases and longer-warmup microbenchmarks. Record per-launch means, variance, allocated bytes, Gen0 collections, tool versions, and revision. Capture emitted IL; collect JIT disassembly where available to attribute dispatch costs.

Acceptance:

- All benchmark checksums pass and all new workloads compile.
- Evidence distinguishes argument construction from callee work and indirect dispatch.
- Measurement variance is small enough to assess the intended change; otherwise report the uncertainty and repeat under stable machine conditions.

### 2. Reduce ordinary rest packing allocations

Primary files:

- `src/SharpTS/Compilation/ExpressionEmitterBase.CallHelpers.cs` (`EmitRestParameterCall`)
- `src/SharpTS/Compilation/RuntimeEmitter.Arrays.cs` (`EmitCreateArray`)
- `src/SharpTS/Compilation/RuntimeEmitter.TSArray.cs`
- `src/SharpTS/Compilation/EmittedRuntime.cs` if a dedicated helper is needed
- `src/SharpTS/Compilation/RuntimeEmitter.TSFunction.cs` for indirect argument adjustment, after direct packing is measured

Work:

- Introduce a rest-specific construction path that fills final storage directly, avoiding the temporary `object[]` followed by collection copying. Prefer a narrow helper over changing the semantics of every `CreateArray` caller.
- Pre-size storage for statically known arities. Preserve a fresh observable rest array on every invocation, including empty rest arrays.
- Keep regular arguments in parameter-typed locals for non-suspending calls. Preserve conversion timing, left-to-right evaluation, and existing boxed spill handling across suspension.
- Apply equivalent reductions to indirect argument adjustment where safe; do not assume it uses the identical construction path.
- Keep the ordinary boxed element representation initially. Numeric storage specialization is a separate decision supported by profiling.

Acceptance:

- Measured allocated bytes per ordinary rest call decrease for representative nonzero arities.
- Allocation reduction is explained by emitted code, not just GC timing.
- Escaping arrays, missing/extra arguments, defaults, `arguments`, and calls with `await`/`yield` remain correct.
- Direct numeric rest remains allocation-free in its existing regression test.

### 3. Reuse numeric companions through proven-stable aliases

Primary files:

- `src/SharpTS/Compilation/StableNumericRestFunctionAnalyzer.cs`
- `src/SharpTS/Compilation/CompilationContext.Functions.cs`
- `src/SharpTS/Compilation/ExpressionEmitterBase.CallHelpers.cs`
- `src/SharpTS/Compilation/ILCompiler.Functions.cs`

Work:

- Extend binding analysis to identify local immutable aliases whose captured function value is a proven-stable declaration. Use lexical binding identity, not identifier spelling.
- Register eligible alias call arities during analysis so required companions exist before call emission.
- Route eligible numeric fixed-arity alias calls to existing companions, preserving the function value for other observable uses.
- Conservatively retain fallback for reassigned sources, ambiguous targets, imports not proven stable, optional calls, spreads, and observable receiver/argument behavior outside the existing proof.
- Test shadowing, alias chains, use before initialization, source reassignment after alias creation, and aliases passed elsewhere.

Acceptance:

- Eligible alias calls have no per-iteration argument-array allocation or generic invocation in emitted IL.
- Alias timing approaches the direct-rest control within measured noise or an explained residual overhead.
- Runtime-selected function controls still exercise and validate fallback dispatch.

### 4. Expose constant rest indices through bounded specialization

Primary files:

- `src/SharpTS/Compilation/StableNumericRestFunctionAnalyzer.cs`
- `src/SharpTS/Compilation/ILCompiler.Functions.cs`
- `src/SharpTS/Compilation/ExpressionEmitterBase.CallHelpers.cs`
- `src/SharpTS/Compilation/ILEmitter.Properties.cs`

Work:

- Begin with literal regular arguments such as `add4Dynamic(0, ...)`. Derive a private specialized body or analysis view; do not mutate the shared AST used by unspecialized calls.
- Fold only proven-safe numeric expressions needed for rest indices, then reuse existing rest eligibility and companion emission.
- Preserve binary64 evaluation order and reject unsupported or exceptional cases rather than introducing general algebraic reassociation.
- Key variants by binding, relevant literal arguments, and rest arity. Set explicit per-function/module variant limits before implementation and use the ordinary path when limits are reached.
- Keep genuinely dynamic, missing, fractional, negative, NaN, and out-of-range accesses semantically correct, including observable prototype behavior.

Acceptance:

- The constant-start benchmark uses scalar rest parameters and avoids per-iteration rest storage allocation.
- Varying-index controls remain correct and exercise the intended path.
- Record compilation time and assembly-size changes; variant-limit tests demonstrate bounded growth.

### 5. Fuse spread expansion with rest construction

Primary files:

- `src/SharpTS/Compilation/ExpressionEmitterBase.CallHelpers.cs` (spread argument helpers and rest packing)
- Runtime spread expansion helper definitions located through `EmitExpandCallArgs`
- Rest storage helpers introduced in stage 2

Work:

- Stream expanded values into final rest storage instead of materializing an expanded argument array and copying again.
- Handle spreads before regular parameters, multiple spreads, and mixed fixed/spread arguments in source order.
- Preserve iterator lookup, side effects, errors, and iterator closing wherever required by existing language semantics.
- Consider a dense numeric-array path only after the general fused path is measured. A `const` binding alone does not prove immutable contents or standard iteration; require an explicit proof or guard with fallback.
- Do not reuse caller-owned array storage when the callee can observe rest-array identity or mutation.

Acceptance:

- General spread calls show fewer allocations and a repeatable timing improvement.
- Custom iterators, holes, mutations during iteration, and throwing iterators match existing semantics.
- Specialized and general spread paths have independent benchmark coverage.

## Verification and release gates

For every implementation PR:

1. Run relevant semantic tests in supported interpreter and compiled modes, using the interpreter as a differential check where appropriate.
2. Add IL verification for each new lowering shape. Extend existing IL/allocation assertions for optimized calls without making tests depend on incidental instruction ordering.
3. Run focused allocation microbenchmarks and compare candidate versus frozen baseline on the same machine/tool versions.
4. Run the language hot-path cross-runtime suite with at least three launches and inspect all parameter sizes, scalar controls, and retained fallback controls.
5. Run the broader compiler/shared test suites before merge. Review compilation time and assembly size for specialization PRs.
6. Attach measured results to the PR. Do not accept a small timing gain within noise; require repeatable evidence or a deterministic allocation reduction. Do not promise a specific speedup from the initial noisy ratios.

Suggested cross-runtime command:

```powershell
./benchmarks/cross-runtime/run-benchmarks.ps1 `
  -Workloads language-hot-paths -Runtimes compiled,node `
  -Launches 3 -IsolatedWorkloads language-hot-paths `
  -OutputDirectory .perf-language-candidate
```

After stage 1 adds the proposed benchmark class:

```powershell
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --filter '*NumericRestBenchmarks*'
```

Completion means the general fallback path allocates less, proven-stable aliases and constant-index calls reuse scalar lowering, spread packing avoids redundant materialization, and semantic/IL/performance evidence supports each change. Any deferred specialization must be explicitly documented with its remaining benchmark gap.

## Implemented behavior

- Ordinary free-function rest calls construct the final fresh `$Array` directly, avoiding the intermediate `object[]` and list copies. Numeric regular parameters stay unboxed when suspension and conversion semantics permit. Indirect argument adjustment pre-sizes its rest list.
- A conservative binding analysis maps exact call nodes to stable declarations. Immutable local alias chains specialize after initialization; shadowing and unknown targets retain ordinary dispatch. Alias values remain materialized for observation.
- Constant regular arguments can resolve rest-index expressions without rewriting the shared AST. Only proven in-bounds reads become scalar arguments. Limits are 32 rest arguments, eight variants per function, and 64 variants per compilation. Existing literal-index direct-call companions remain available in nested call sites.
- Shared spread argument construction consumes each iterator before evaluating later arguments. Direct rest calls append into final private storage, then remove regular parameters and finalize length. Dynamic calls still require their existing `object[]` ABI. Dense storage has a guarded copy path; holes become actual `undefined` values, and custom iterator hooks and potentially mutated array prototypes retain observable lookup.
- Added nine allocation benchmarks, unknown-target/varying-index cross-runtime controls, IL/allocation assertions, and dual-mode semantic tests.

The implementation deliberately retains generic dispatch and indexed access for genuinely unknown targets and varying indices. It does not add speculative deoptimization, optimize interpreter execution, or change the separately emitted instance-method rest packing path. Call-site proofs do not propagate aliases across callable boundaries.

Investigation also reproduced an existing ordinary direct-call issue on the frozen baseline: after reassigning a function declaration, a direct call can still invoke its original body. Reassigned bindings are excluded from the new specialization, but repairing that existing dispatch behavior is outside this change.

## Validation results

Release builds succeeded with .NET SDK 10.0.400 / runtime 10.0.11. Cached dependencies were usable; NuGet vulnerability lookup emitted NU1900 warnings because the service index was unavailable.

- Focused rest, spread, arguments, feature detection, IL, and allocation tests: **239 passed**.
- Compiler suite excluding `StandaloneDllTests`: **991 passed**.
- Shared array, iterator, typed-array iteration, and generator tests: **1,502 passed**.
- These selections overlap; their counts should not be summed as distinct tests.
- A broader compiler/shared run was stopped after HTTP/listener failures and IPC/process timeouts. HTTP failures include `MockHttpServer.Start` throwing `HttpListenerException: The handle is invalid` during fixture construction, before guest code runs. The entire suite is therefore **not green or fully validated** in this environment; rerun it on a host with working listener/process fixtures before merge.
- The same updated cross-runtime source compiles to 361,984 bytes with the frozen baseline and 363,008 bytes with the candidate: **+1,024 bytes (0.28%)**.
- `git diff --check` passes.

Raw local test logs are in the ignored `.perf-rest-frozen` directory. The added tests retain reproducible semantic and allocation guards without depending on those artifacts.

## Allocation and throughput evidence

BenchmarkDotNet 0.14.0, Release, in-process ShortRun, three warmups and three measured iterations, N=10,000. Compilation, module initialization, and checksum validation occur outside measurement. The table compares the repeated frozen baseline against the candidate; each row is one invocation containing 10,000 calls. Memory figures include any setup performed inside that invocation, such as the observed alias wrapper.

| Case | Baseline mean, ms | Candidate mean, ms | Baseline bytes | Candidate bytes |
| --- | ---: | ---: | ---: | ---: |
| Fixed parameters | 0.006323 | 0.008990 | 0 | 0 |
| Direct scalar rest | 0.005714 | 0.005929 | 0 | 0 |
| Stable local alias | 4.549542 | 0.005980 | 4,560,130 | 96 |
| Constant index | 3.800799 | 0.006104 | 4,800,095 | 0 |
| Ordinary packing | 0.777217 | 0.431168 | 4,080,005 | 2,640,005 |
| Escaping rest array | 0.735337 | 0.350873 | 3,840,005 | 2,400,002 |
| Spread | 4.524958 | 3.464768 | 5,760,419 | 2,800,383 |
| Varying index | 4.094871 | 3.408721 | 5,280,102 | 3,440,062 |
| Unknown target | 5.295483 | 4.060208 | 4,560,133 | 4,560,131 |

The reliable improvements are eliminating per-call allocation for proven scalar cases and reducing ordinary packing by 35.3%, escaping arrays by 37.5%, spread by 51.4%, and varying-index packing by 34.8%. Unknown-target four-argument calls retain effectively identical allocation: dispatch, boxing, and indexed callee work remain future targets.

Gen0 collections per 1,000 benchmark invocations fell from 236.33 to 152.34 for packing, 221.68 to 138.67 for escaping arrays, 328.13 to 156.25 for spread, and 304.69 to 199.22 for varying indices. Alias and constant-index cases recorded no Gen0 collections on the candidate.

Treat the timing columns as observations, not precise speedup promises. The repeated baseline's unknown-target standard deviation was 1.269 ms and escaping-array deviation was 0.286 ms; candidate constant-index deviation was 0.000783 ms. Scalar controls also varied between runs. The dramatic scalarization improvement is supported independently by IL and allocation assertions; smaller throughput changes need a controlled host for precise estimates.

Local full BenchmarkDotNet reports, including error intervals, are under `.perf-rest-baseline`, `.perf-rest-baseline-repeat`, and `.perf-rest-candidate`. No profiler is required to reproduce the deterministic allocation guards.

An isolated follow-up of the fixed-parameter control measured **5.503 ± 0.030 μs baseline versus 5.470 ± 0.018 μs candidate** (mean ± standard deviation). JIT disassembly produced the same 170-byte optimized body apart from relocated addresses. This resolves the apparent slowdown of that control in the full in-process run.

Three alternating fresh-process CLI compilations of the same updated language workload measured 1.271–1.322 seconds baseline and 1.244–1.276 seconds candidate, including CLI startup and JIT. This small sample shows no compilation-time increase; it is not a compiler-only throughput measurement.

## Isolated cross-runtime results

All 13 cases at all three parameter sizes completed three launches with checksum validation: 117 baseline compiled measurements and 234 candidate compiled/Node measurements. Each case ran in a fresh process, containing its three parameter sizes. The candidate snapshot passed the repository's schema validator. Medians of launch means at N=100,000, in milliseconds:

| Case | Frozen baseline | Candidate | Node 22.23.2 |
| --- | ---: | ---: | ---: |
| Numeric compound | 0.0411 | 0.0394 | 0.0487 |
| Assignment control | 0.0401 | 0.0406 | 0.0450 |
| Direct scalar rest | 0.0553 | 0.0587 | 0.0540 |
| Flattened control | 0.0551 | 0.0566 | 0.0528 |
| Left-associated control | 0.1616 | 0.1619 | 0.1721 |
| Immutable alias | 22.4460 | 0.0565 | 0.0513 |
| Constant-index target | 21.5803 | 0.0579 | 0.0523 |
| Spread | 22.4518 | 17.7355 | 0.4058 |
| Unknown target | 22.0842 | 21.1848 | 0.0569 |
| Varying index | 21.1766 | 20.5150 | 0.3506 |
| Generator range | 0.1959 | 0.2179 | 0.9813 |
| Parse integers | 0.0405 | 0.0417 | 1.2320 |
| Format fixed | 3.4115 | 3.4876 | 9.0173 |

The alias and constant-index targets now approach scalar-control performance. Spread improves in both benchmark harnesses, with allocation reduction as the deterministic evidence. Unknown-target and varying-index throughput remain major gaps: their small timing changes overlap launch variation, despite lower packing allocation for varying indices. Further work should profile dynamic dispatch and boxed indexed reads before adding another specialization.

Raw candidate evidence is in `.perf-rest-cross/{results.txt,snapshot.json}`; matching frozen-baseline results and all-parameter summaries are in `.perf-rest-frozen/{baseline-cross-results.txt,cross-summary.csv}`. These artifacts are local and ignored; the commands and checked-in workloads reproduce them.

A follow-up of the generator control used three alternating baseline/candidate launches. Its N=100,000 median was 0.1972 ms baseline versus 0.2000 ms candidate, with overlapping launch ranges. The captured Tier0, Tier1-OSR, and Tier1 bodies matched after normalizing addresses (580, 288, and 343 bytes respectively). This did not reproduce the initial 11% control slowdown.
