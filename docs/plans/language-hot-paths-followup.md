# Language hot paths: remaining performance gaps

Investigation date: 2026-09-04 (America/Los_Angeles). Revision: `af72038b83e9c88449b33ca42262f3f8dc156ea7`. This is an investigation and recommendation, not an implementation. Scope: compiled SharpTS versus Node; interpreter performance was not measured.

The earlier [rest-call plan](language-hot-paths.md) is already implemented. Direct rest calls, stable aliases, and constant-start index calls already use scalar companions. The next priorities are boxed array reads, redundant argument conversion, and the remaining allocation in ordinary rest/spread calls.

The proposed delivery sequence and acceptance gates are in the [implementation plan](language-hot-paths-implementation.md).

## Fresh cross-runtime evidence

Release SharpTS, .NET SDK 10.0.400 / runtime 10.0.11, Node 22.23.2, Windows x64. Each case ran in a fresh process with its three input sizes. Three launches per runtime produced 234 measurements across 13 cases. The runner completed successfully, the existing checksums and rounding guard passed, and the snapshot passed schema validation.

Median of three launch means, milliseconds per workload invocation at N=100,000:

| Case | Compiled | Node |
| --- | ---: | ---: |
| Numeric compound | 0.0440 | 0.0475 |
| Numeric assignment control | 0.0404 | 0.0482 |
| Direct numeric rest | 0.0645 | 0.1239 |
| Flattened rest control | 0.0586 | 0.1302 |
| Left-associated accumulation | 0.1908 | 0.3770 |
| Stable local alias | 0.0609 | 0.0606 |
| Constant-start rest indexing | 0.0624 | 0.1074 |
| Spread rest | **20.4722** | **0.9156** |
| Unknown-target rest | **25.8904** | **0.1066** |
| Varying-index rest | **21.4880** | **0.9246** |
| Generator range | 0.2346 | 1.9176 |
| Parse integers | 0.0442 | 1.4224 |
| Format fixed | 4.2615 | 9.3442 |

These establish large gaps, not precise speedup budgets. Launch means varied considerably: unknown-target compiled ranged from 22.41 to 65.86 ms; varying-index compiled from 20.71 to 59.05 ms. Even the flattened control varied from 0.0567 to 0.1854 ms compiled and 0.0574 to 0.3352 ms on Node. Do not interpret smaller differences as regressions or improvements. Builds and generated-code inspection occurred during portions of the cross-runtime run; other host activity was not controlled. The subsequent allocation probes ran separately from this suite.

## Attribution beyond the aggregate timings

A local probe compiled the existing `NumericRest.ts` workload plus additional controls. It invoked compiled methods through typed delegates, warmed each case for one second, and measured seven batches of 50 invocations. Compilation, reflection-based delegate setup, initialization, and checksum checks were outside measurement. Three fresh processes were run sequentially. Allocations use `GC.GetAllocatedBytesForCurrentThread`; this was a custom diagnostic harness, not a BenchmarkDotNet run or CPU sampling profile.

Each invocation contains 10,000 iterations. Times are medians of launch means; bytes are approximate allocation per invocation, including setup performed inside the workload.

| Probe | Time, ms | Allocated bytes |
| --- | ---: | ---: |
| Fixed parameters | 0.00564 | 0 |
| Direct scalar rest | 0.00590 | 0 |
| Stable alias | 0.00745 | 96 total |
| Constant-start rest | 0.00597 | 0 |
| Ordinary rest packing plus mutation/length | 0.32190 | 2,640,000 |
| Escaping rest array plus length | 0.26867 | 2,400,000 |
| Spread plus four indexed reads | 1.97060 | 2,800,380 |
| Spread plus returned array length | 0.35504 | 2,080,312 |
| Unknown target plus four indexed reads | 2.93756 | 4,560,278 |
| Unknown target plus returned array length | 0.81525 | 3,600,096 |
| Varying-index rest | 2.08279 | 3,440,083 |
| Four constant-index reads, reused boxed array | 1.59688 | 960,239 |
| Four constant-index reads, reused numeric storage | 0.04042 | 176 total |
| Four varying-index reads, reused boxed array | 1.54262 | 960,272 |
| Four varying-index reads, reused numeric storage | 0.05968 | 264 total |

The reused arrays are created once per invocation, outside the inner loop. Boxed arrays come from an escaping rest function. Numeric storage is explicitly established with `const values: number[] = []` followed by numeric `push` calls; emitted IL confirms `MarkNumeric`/`PushDouble`. A nonempty `number[]` literal did not establish numeric storage in this probe: it emitted boxed elements and `CreateArray`.

Four boxed reads allocate about **96 bytes per inner iteration**, independent of rest-array construction. The matching numeric-storage controls have no per-iteration allocation. This implicates the read path as a substantial shared cost. The controls change storage representation, so their timing ratio is not a promised speedup for a proposed boxed-read optimization. Differences between the packing, indexing, and dispatch workloads are also not an additive CPU-time breakdown: their return shapes and callee work differ, and timings remain noisy.

## Recommended implementation order

### 1. Add a guarded fast path for dense boxed numeric reads

Relevant code:

- `src/SharpTS/Compilation/ILEmitter.Properties.cs`, `TryEmitNumberArrayGetIndexAsDouble` (line 1542).
- `src/SharpTS/Compilation/RuntimeEmitter.TSArray.cs`, `CanGetDouble`/`GetDouble` (lines 865–910).
- `src/SharpTS/Compilation/RuntimeEmitter.Objects.Index.cs`, `$Array` and list index handlers (lines 522 and 690).

The compiler already emits a numeric-consumer fast path, but `CanGetDouble` requires numeric storage. Rest construction deliberately creates boxed storage, so it fails that guard. Each of the four reads then boxes its index and calls general `GetIndex`. On a present ordinary element, that helper still performs numeric conversion, two numeric-key-to-string conversions, and separate property-descriptor-store lookups before reading storage. The ordinary rest callee has 367 bytes of IL; its scalar companion has 18 bytes.

Extend the guarded path to accept a present dense boxed `double`, then unbox it directly. Preserve exact integral-index checks, bounds, holes, actual element type, and descriptor semantics. Start under the existing conservative feature restrictions; a later per-object guard can use the existing `PDSHasPropertyDescriptors` helper where whole-program restrictions are too broad. Handle plain `List<object>` as well as `$Array`, since indirect rest adjustment supplies the former. Keep general property lookup for unsupported cases.

This should be the first broad optimization: it addresses all three slow cases and ordinary array consumers. A useful deterministic acceptance criterion is elimination of the approximately 96 bytes per iteration in the reused boxed-array controls. Do not just loosen `CanGetDouble` and assume every slot contains a double: holes, descriptors, and values written through `any` must retain their behavior.

### 2. Remove no-op conversion scans from known rest dispatch

Relevant code: `src/SharpTS/Compilation/RuntimeEmitter.TSFunction.cs`, `Invoke` (line 638), `EmitComputeNeedsArgConversion` (line 1357), `AdjustArgs` (line 1663), and the conversion helpers (lines 1973 and 2123).

`MethodInvoker` and rest-shape metadata are already cached. However, `_needsArgConversion` is set for any `List<object>` parameter. For `restAdd4`, `AdjustArgs` has already constructed the required list, yet both `ConvertArgsForUnionTypes` and `CoercePrimitiveArgs` execute. Each calls `GetParameters`; neither changes the argument in this case.

A separate diagnostic constructed two wrappers for that exact callee and cleared `_needsArgConversion` on one wrapper through reflection. It reused both wrappers, alternated measurement order across three rounds, and checked matching results. Allocation fell from approximately **456 to 392 bytes per call**, consistent with removing two one-element `ParameterInfo[]` arrays: **64 bytes, or 14%**. Median time for 10,000 calls was 1.967 versus 1.880 ms, but one round reversed direction, so the throughput effect is not established.

For production, derive a conversion plan at wrapper construction, distinguish union from primitive conversion, and exclude a trailing rest slot whose representation is guaranteed by `AdjustArgs`. Cache any remaining parameter/converter metadata. Preserve coercion for regular numeric/string/union parameters and borrowed array-method receivers; globally disabling conversions would be incorrect. The reflective toggle is attribution only, not a production patch.

### 3. Preserve numeric storage through ordinary rest construction

Relevant code: `src/SharpTS/Compilation/ExpressionEmitterBase.CallHelpers.cs`, `EmitRestParameterCall` (line 915), and `RuntimeEmitter.TSArray.cs`, `EmitTSArrayRestConstruction` (line 1128).

Direct non-spread rest packing already avoids the old intermediate array/list copies. It still boxes each numeric rest argument and constructs a boxed `$Array`. Add an exact-capacity numeric rest constructor/fill path when supplied values are proven numeric, preserving fresh array identity and allowing the existing boxing transition when required by mutation or escape. Keep `arguments`, missing values, suspension, and evaluation order correct. For indirect calls, assess whether numeric storage can cross the ABI without subsequent list access immediately boxing it again.

This benefits the varying-index case even when its index cannot be specialized. Avoid solving only `i % 2` with more compiler variants before improving the ordinary path. Existing specialization limits are 32 rest arguments, eight variants per function, and 64 variants per compilation; preserve bounded code growth.

### 4. Reduce residual spread and unknown-call overhead

For spread, `EmitExpandedRestStorage` (line 1004) and `RuntimeEmitter.Iterator.cs` (line 1206) already fuse expansion into the destination. The remaining work is to preserve numeric storage, reserve the actual expanded capacity when safely known, and avoid the unconditional `Elements` access that boxes a numeric source. In this workload, source argument count is two but expanded length is four, causing backing-storage growth. A guarded stable-array spread could eventually call a scalar companion, but `const tail` alone does not guarantee unchanged contents or iterator behavior.

For unknown calls, after steps 1–2, consider a cached typed arity-specific entry point on eligible `$TSFunction` values. This can avoid the caller `object[]`, argument boxes, adjustment array/list, and boxed numeric return. Retain generic dispatch for incompatible targets, receivers, closures, and observable `arguments`/rest behavior. This is a larger ABI change than removing the conversion scans and should be measured separately.

## Benchmark interpretation and validation

- Keep the direct, alias, constant-start, and flattened controls: current results do not justify further work on scalar addition.
- `left-associated-accumulation` intentionally has a longer loop-carried dependency chain. Reassociating floating-point additions can change results; it is not an equivalent rest-call control.
- `parse-integers` is folded by `TryEmitStableIntegerCounterParseInt` in `ExpressionEmitterBase.NumberConversions.cs` (line 83). It measures that optimization here, not general parser throughput. Use the existing `ParseIntDecimalBenchmarks` for parser attribution.
- Generators and `toFixed` are already faster than Node in this run; neither is the first priority for this suite. Keep the midpoint-rounding guard. `format-fixed` currently lacks an expected total-length checksum; add one alongside broader content-sensitive checks if changing its implementation.
- Preserve unknown-target coverage with runtime-selected targets and add alternating-target coverage before introducing call-site specialization. The present higher-order control passes the same function every time, so a runtime that specializes a stable observed target has a legitimate advantage.
- Move useful reused-array and no-op-conversion probes into the existing allocation-diagnosed microbenchmark suite before implementation. Use longer warmup, fresh-process allocation/timing runs, and interleaved baseline/candidate measurements on a controlled host. Retain launch ranges rather than reporting only ratios.
- Validate emitted IL and allocations as well as semantics: boxed/numeric arrays, sparse holes, out-of-range/fractional/negative/NaN indices, accessors and prototypes, custom iterators, mutation during iteration, escaping/rest identity, defaults, `this`, `arguments`, and calls crossing suspension. No production code was changed, so a full semantic test suite was not run for this investigation.

## Reproduction and local artifacts

```powershell
./benchmarks/cross-runtime/run-benchmarks.ps1 `
  -Workloads language-hot-paths -Runtimes compiled,node `
  -Launches 3 -IsolatedWorkloads language-hot-paths `
  -OutputDirectory .perf-language-review
```

Local, ignored evidence is retained under `.perf-language-review`:

- `results.txt`, `snapshot.json`, `summary.csv`: raw cross-runtime results, metadata, and all-size summaries.
- `probe/Additional.ts`, `probe/Attribution.ts`, `probe/Program.cs`, `probe/Probe.csproj`: diagnostic source and harness.
- `probe/results-{1,2,3}.csv`, `probe/summary.csv`, `probe/dispatch-controls.csv`: allocation and timing observations.
- `probe/probe-il.txt`: emitted instructions and resolved method calls.

To rerun the local probe after building Release SharpTS:

```powershell
Get-Content benchmarks/micro/SharpTS.Microbenchmarks/TypeScriptSources/NumericRest.ts, `
  .perf-language-review/probe/Additional.ts |
  Set-Content .perf-language-review/probe/Attribution.ts
dotnet src/SharpTS/bin/Release/net10.0/SharpTS.dll --compile `
  .perf-language-review/probe/Attribution.ts -o .perf-language-review/probe/Attribution.dll
dotnet build .perf-language-review/probe/Probe.csproj -c Release -p:NuGetAudit=false
dotnet .perf-language-review/probe/bin/Release/net10.0/Probe.dll `
  .perf-language-review/probe/Attribution.dll
dotnet .perf-language-review/probe/bin/Release/net10.0/Probe.dll `
  .perf-language-review/probe/Attribution.dll --dispatch-controls
```

The ignored probe files are available in this workspace, not in a fresh checkout. The checked-in workload and microbenchmark suite remain the starting point for permanent regression coverage.
