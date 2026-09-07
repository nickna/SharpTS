# Materialized object destructuring results

Measured September 4–5, 2026. Baseline: `af72038b83e9c88449b33ca42262f3f8dc156ea7`; candidate compiler: `47f090334bb1d5e63b0048a1f1511a271564ea44`. Validation is complete, with the baseline failures and platform limits documented below.

The original benchmark creates a dictionary immediately because it mutates an `any` alias. It does not measure a compact record transitioning to dictionary storage. The new `object-destructure-carrier-materialized` workload exercises that transition separately; compiler tests assert both representations.

The improvement comes from acquiring own numeric values before eligible invariant loops. Guards reject descriptors, missing properties, nonnumeric values, and unsupported bounds before observable state changes. Safe integer reductions retain their exactness checks; fractional reductions retain the original addition tree. Varying or mutated receivers acquire a fresh snapshot at each destructuring operation. General dictionary property dispatch also avoids a duplicate descriptor lookup.

## Measurement setup

- Intel Core i7-14700T; Windows 10.0.29639; .NET SDK 10.0.400/runtime 10.0.11; Node 22.23.2.
- Identical harness and workload copies compiled with the saved baseline compiler and candidate compiler. Only the workload size list was narrowed to `100000` for this investigation.
- `SHARPTS_BENCH_WARMUP_MS=1500`, normal runtime tiering, 300 ms sample budget, auto-batching, validated checksums. Five fresh alternating baseline/candidate launches; three Node launches for the two target cases. Measurements ran separately from this task's builds and test suites; other host activity was not controlled.
- Tables report the median of each launch's mean in milliseconds. Paired change is the median of `(candidate / baseline - 1) * 100` for matching launches, not the ratio of the independently selected medians.
- Raw launch means, minima, sample standard deviations, counts, and batch sizes are retained locally under `.perf-local/destructure-implementation/`.

## Windows, normal scheduling

| Case, n = 100,000 | Baseline ms | Candidate ms | Paired change |
| --- | ---: | ---: | ---: |
| Original dictionary destructuring | 3.2384 | 0.0242 | -99.19% |
| Actual materialized carrier | 8.3464 | 0.0246 | -99.69% |
| Materialized fractional values | 5.6562 | 0.1665 | -97.06% |
| Materialized varying receiver | 5.8593 | 1.7943 | -69.99% |
| Materialized property mutation in loop | 11.9480 | 7.5449 | -36.28% |
| Materialized direct property reads | 5.7402 | 4.6420 | -17.70% |
| Manually hoisted materialized reads | 0.1658 | 0.1650 | -0.46% |

The original case's median fell by about 134× and the actual transition case by about 339×. These are local measurements, not general guarantees. Node medians were 0.0696 ms and 0.1146 ms respectively.

Some compact/direct control results varied substantially between launches. A second five-pair run pinned all children to logical CPU 2. It showed approximately unchanged arithmetic controls and improvements in varying receivers, but also greatly increased the baseline's warmup sensitivity. Its original/carrier medians were 22.14/62.38 ms before and approximately 0.02/0.03 ms after. Those results are diagnostic and are excluded from the headline speedups.

## Linux, normal scheduling

WSL Ubuntu 26.04, .NET SDK 10.0.400/runtime 10.0.11, and Node 22.23.2; otherwise the same paired methodology and generated assemblies as Windows.

| Case, n = 100,000 | Baseline ms | Candidate ms | Paired change |
| --- | ---: | ---: | ---: |
| Original dictionary destructuring | 2.8534 | 0.0223 | -99.31% |
| Actual materialized carrier | 7.6630 | 0.0210 | -99.72% |
| Materialized fractional values | 3.0384 | 0.2028 | -93.16% |
| Materialized varying receiver | 2.9650 | 0.9263 | -66.67% |
| Materialized property mutation in loop | 6.2854 | 3.6102 | -46.37% |
| Materialized direct property reads | 3.1205 | 2.4852 | -10.54% |
| Manually hoisted materialized reads | 0.1353 | 0.1319 | -4.06% |
| Compact fused reduction | 0.0199 | 0.0201 | -0.02% |
| Compact split reduction | 0.1254 | 0.1303 | +1.70% |
| Compact fractional reduction | 0.1252 | 0.0804 | -37.05% |
| Compact varying receiver | 0.2590 | 0.2758 | +6.49% |
| Compact direct varying receiver | 0.3160 | 0.3207 | +1.51% |
| Unrelated dynamic mutation | 0.0207 | 0.0197 | -3.44% |

The original case's median improved about 128×; the carrier case about 366×. Node medians were 0.0369 ms and 0.0456 ms respectively. No Linux control crossed the lab's 10% relative regression threshold. The +6.49% compact varying result remains worth tracking on an idle, controlled host; these measurements do not establish that every small change is noise. The two roughly 0.02 ms arithmetic controls also remained stable by paired comparison, independently of the lab's default 0.05 ms absolute threshold.

## Allocations

BenchmarkDotNet 0.14.0 ShortRun (one launch, three warmup and three measured iterations), using cached typed delegates and MemoryDiagnoser:

| Case | Bytes/call, n = 1,000 | Bytes/call, n = 100,000 |
| --- | ---: | ---: |
| Dictionary | 288 | 288 |
| Materialized carrier | 534 | 516 |

Allocation does not grow with loop length. Separate per-thread allocation probes of the standalone carrier fixture measured exactly 496 bytes/call at both sizes, for both baseline and candidate. The original dictionary baseline also measured 288 bytes/call. The carrier's small amortized variation in BenchmarkDotNet is fixed overhead, not allocation per destructured property. ShortRun timing confidence intervals were wide on this host, so use the five-pair cross-runtime comparisons above for latency.

## Validation

The focused compiler, compact-record, destructuring, descriptor, and proxy suite passed (243 tests), followed by the additional descriptor-identity regression. IL tests find both optimized numeric loops free of property reads/conversions and confirm generic fallback and cancellation remain. Allocation tests compare 1,000 with 100,000 iterations and reject growth proportional to the loop count. Valid and invalid warmup settings were exercised against a standalone emitted assembly.

- Windows full hermetic core suite: 17,918 passed, 3 skipped, 2 failed. The interpreter generator timeout and compiled Promise output failure both passed when retried in isolation (four compiled/interpreted cases). The first sandboxed attempt was stopped after local HTTP/certificate/IPC permission failures; these did not recur with normal host permissions.
- WSL Ubuntu 26.04, x64, .NET 10.0.11: 17,920 passed, 3 failed. Restoring the tracked executable bit and Unix shebang in the copied sample resolved one failure; all three shebang tests then passed. The two remaining interpreter OS-memory tests also fail with the unchanged baseline: `freemem()` exceeds the reported `totalmem()` on this host. Interpreter memory reporting is outside this change.
- Linux Native AOT: published the managed runtime payload and native compiler, passed `samples/AotCompileGate/main.ts` (42), and used the native compiler to compile both target benchmark modules. Both emitted programs passed checksums at 10,000 and 100,000 iterations. The source snapshot and measurement assemblies reside on WSL's ext4 filesystem.
- The Windows solution and all selected cross-runtime fixtures built. All embedded microbenchmark sources smoke-compiled on Windows and Linux.
- The remaining Windows full-gate checks passed: 108 GUI conformance tests, all three conformance project builds, 10 NuGet release helper tests, GUI version staging, packaged SDK compile/publish/execute smoke, and AOT analyzer enforcement (zero warnings; inventory matches the baseline).

All four selected allocation benchmarks completed and their setup checksums passed, including the direct, hoisted, fractional, varying, and mutated controls. ARM and macOS were not exercised locally.

## Reproduction

Compile the same versions of these workloads with the baseline and candidate compiler: `object-destructure-materialized`, `object-destructure-carrier-materialized`, `object-destructure-materialized-controls`, `object-destructure`, and `object-destructure-unrelated-dynamic`. Set the warmup environment variable on both executions and alternate process order. Keep the materialization fixture in its own module so whole-program shape analysis stays representative.

Run the durable allocation benchmarks with:

```powershell
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --filter '*ObjectDestructuringBenchmarks*'
```

Future widening should be measured separately: descriptor-enabled programs, richer loop bodies, and more general invariant analysis remain outside this change.
