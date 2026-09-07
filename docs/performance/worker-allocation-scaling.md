# Worker allocation scaling results

The unchanged 20,000-record kernel allocates approximately **5.17 MB per job**,
down from **11.41 MB** (about **55% less**). Five paired Linux launches measured
70–75% lower compiled execution time across direct execution and 1/2/4 workers.
Interpreted message-only round trips fell from about 10 ms to 1.1–1.2 ms in all
five pairs. The GC default remains workstation GC.

See the [implementation plan](../plans/worker-allocation-scaling.md) and
[numeric-array boundary audit](../design/local-record-numeric-arrays.md).

## Changes and attribution

The kernel, record interface, labels, input size and checksum remain unchanged.
Workers stay alive across measurements; compilation, startup, readiness,
preflight, warmup and shutdown remain outside the timed region.

- Interface property reads now guard the existing compact carrier and its
  materialization/descriptor state, then load the matching field. Numeric
  consumers retain doubles; general fallbacks preserve dynamic values.
- Dense ordinary literals construct their final `$Array` storage directly,
  avoiding the temporary object array, intermediate list and copy.
- A conservative use analysis permits owned numeric buffers only for private
  literals inside fresh local records. Escapes, mutation, closures and unsupported
  bindings retain boxed storage. The emitter checks actual native value storage,
  rather than trusting a `number` annotation alone.
- Numeric array reads and lengths retain native numeric values, while preserving
  the existing special handling of queues, rest parameters and invalid keys.
- Interpreted workers schedule bounded message/stdin drains on their owning
  event loop when data arrives. An atomic flag coalesces wakeups; completion
  releases ownership and rechecks pending work to avoid lost notifications.

For this x64 kernel, the observed reduction is approximately **312 bytes per
record**: 144 from literal intermediates, 96 from four boxed elements, 48 from
two interface numeric reads, and about 24 from each loop-length read. These are
implementation-specific allocation explanations, not universal object sizes.
The independent checksum is `800178000`.

## Measurement method

Baseline: `88378dce04627da968dfde8243c4585b45ed3dfa`. Candidate: this uncommitted
implementation. Both use the same updated harness and unchanged kernel. The
baseline compiler/dependency closure was saved before edits. Linux compiled each
closure and the same sources inside a fresh ext4-backed WSL directory.

Windows x64 build 10.0.29639 and Ubuntu 26.04 on WSL used .NET SDK 10.0.400,
runtime 10.0.11, Node 22.23.2, and 28 available processors (Intel family 6,
model 183, stepping 1). Default compiled applications used `System.GC.Server=false`
and `System.GC.Concurrent=true`. No application GC defaults changed.

Each retained comparison has five independent launches. Baseline/candidate
order alternates between launches; Windows runs with multiple runtime modes also
rotate runtime order. Original workload cases stay grouped in their source order;
the selected adjacent controls run in separate processes. Processes
run sequentially, with no builds or tests from this task during measurement.
Other work on the shared Windows host could not be excluded. The original
allocation and message-latency runs used 1,000 ms warmup and sampling budgets;
adjacent diagnostic controls used 300 ms. The minimum sample count can extend
these budgets. BENCH rows retain per-launch means, minima, standard deviations,
sample counts and inner-batch sizes.

Tables report medians of launch means. Paired changes are medians of
`candidate / baseline - 1` for matching launches, not ratios of independently
selected medians. Node is a contextual control, not the baseline used to
attribute changes. These are local measurements, not CI thresholds.

The final boxed-local safety guard emitted identical canonical kernel IL to the
Linux candidate. Final Windows measurements rebuilt all retained workloads after
that guard. Earlier Windows measurements are labeled separately in the evidence.

## Linux steady-state results

| Execution | Baseline ms/job | Candidate ms/job | Paired change | Node ms/job |
| --- | ---: | ---: | ---: | ---: |
| Direct | 15.91 | 4.61 | −75.2% | 0.70 |
| 1 worker | 21.82 | 6.74 | −70.4% | 0.53 |
| 2 workers | 17.66 | 5.16 | −71.0% | 0.27 |
| 4 workers | 7.33 | 1.96 | −73.6% | 0.20 |

All five compiled pairs improved for every allocation case. There is still a
substantial gap to Node. Interpreted allocation changed by roughly −4%, −5%,
−9% and −13% respectively, with only three or four improved pairs per case;
these smaller changes have weaker attribution than the compiled result.

| Message round trip | Baseline interpreted ms | Candidate interpreted ms | Paired change |
| --- | ---: | ---: | ---: |
| 1 worker | 9.99 | 1.14 | −88.6% |
| 2 workers | 10.00 | 1.15 | −88.5% |
| 4 workers | 9.98 | 1.19 | −88.0% |

All five interpreted latency pairs improved at every worker count. Compiled
message-only latency stayed below 0.1 ms in both variants; its small absolute
changes do not support an additional worker-scheduler claim. Windows message
latency had much greater variation, and its average improvement is inconclusive.

## Allocation, GC and scaling

The final Windows allocation comparison also improved in all five pairs:

| Execution | Baseline ms/job | Candidate ms/job | Paired change |
| --- | ---: | ---: | ---: |
| Direct | 14.50 | 3.95 | −72.7% |
| 1 worker | 20.87 | 6.19 | −70.1% |
| 2 workers | 18.87 | 5.42 | −71.1% |
| 4 workers | 6.18 | 1.40 | −77.2% |

Absolute timings remain sensitive to the shared host. The large compiled
improvement repeats on both operating systems and is supported independently
by allocation and emitted-IL evidence.

The [process-wide diagnostic](../../benchmarks/cross-runtime/diagnostics/allocation-gc/README.md)
uses `GC.GetTotalAllocatedBytes(true)`, collection counts and total GC pause
duration across all process threads. The measured interval contains exactly 20
jobs after at least one second of warmup and worker readiness. A small fixed
marker/bookkeeping overhead is included. Whole-process allocation and peak
working set are recorded separately and must not be interpreted as per-job
steady-state measurements.

| Workers (0 = direct) | Baseline bytes/job | Candidate bytes/job | Baseline Gen2 | Candidate Gen2 |
| --- | ---: | ---: | ---: | ---: |
| 0 | 11,405,339 | 5,165,218 | 6 | 3 |
| 1 | 11,410,067 | 5,169,659 | 6 | 2 |
| 2 | 11,413,951 | 5,172,712 | 6 | 2 |
| 4 | 11,420,488 | 5,179,508 | 0 | 0 |

Collection counts cover the 20-job interval and are single-run observations.
Partitioning 20,000 records across four workers also changes each growing
reference list's allocation regime. A list growing from capacity 8,192 to 16,384
crosses the default x64 large-object-heap threshold; four 5,000-record partitions
avoid that jump. Thus the four-worker advantage includes a GC effect and cannot
be attributed entirely to parallel execution.

Serial controls make the same boundary visible without concurrent workers:

| Serial partitions | Records per partition | Total records | Baseline ms/job | Candidate ms/job |
| --- | ---: | ---: | ---: | ---: |
| 1 | 8,192 | 8,192 | 2.733 | 0.912 |
| 1 | 8,193 | 8,193 | 9.160 | 2.127 |
| 4 | 8,192 | 32,768 | 19.706 | 4.956 |
| 4 | 8,193 | 32,772 | 29.044 | 8.576 |

Every paired serial control improved in all five launches. These controls use
fixed records per partition, whereas the original worker cases use fixed total
records; their timings should not be compared as if they perform equal work.

Adjacent compiled controls at `n=10000` showed no material change: array methods
0.0701 → 0.0687 ms (paired −0.8%), objects 0.01662 → 0.01673 ms (paired −0.2%),
and stable numeric rest 0.00940 → 0.00943 ms (paired +0.4%).

For 100-message bursts, interpreted execution improved in all five Windows
pairs: 26.42 → 12.01 ms, paired −58.2%. Compiled bursts had a paired median
change of −1.8% with only three improved pairs and no consistent change.
Single-worker lifecycle measurements also stayed within noise: compiled
57.08 → 58.17 ms (paired +1.9%) and interpreted 28.47 → 28.57 ms
(paired +0.3%), with mixed signs across launches.

The separate `adaptive` profile retained approximately 5.17 MB/job. Its observed
pause totals were lower in this diagnostic, but peak process memory was often
larger (about 152–425 MB versus 96–143 MB for the default candidate). These
single-launch memory/pause observations do not justify changing the default.
Five separate adaptive candidate launches measured medians of 5.05, 5.83, 3.70
and 1.21 ms for direct/1/2/4 workers. These launches were not interleaved with
workstation GC, so their difference from the default-profile table is diagnostic,
not a paired profile comparison.

## Correctness and reproducibility

- Release full-suite run: 17,962 passed, 3 failed, 3 skipped. All three failures
  traced to bypassing existing queue/rest routing in numeric length emission.
  After the routing fix, the affected array/rest/numeric/compact-record/worker
  selection passed **2,331 tests**. The latest narrower guard/IL selection passed
  **75 tests**. The entire suite was not repeated after those fixes.
- All **43 cross-runtime workloads** smoke-compiled. The final retained workload
  binaries were rebuilt successfully. Snapshot contract and Node harness checks
  passed, including case listing/filtering, invalid budgets, independent oracles,
  and uneven worker partitions.
- A paired Test262 probe covered all 52 `language/expressions/array` files and six
  adjacent array-method cases at corpus revision
  `d5e73fc8d2c663554fb72e2380a8c2bc1a318a33`: compiled **58/58 passed**;
interpreted **50/58 passed** on both revisions. **No outcomes changed**.
- All **18 boundary cases** validated in compiled SharpTS, interpreted SharpTS
  and Node. These checksum runs used a minimal sampling budget and are excluded
  from the retained timing comparisons.
- Semantic and emitted-IL tests cover interface fallback/nullish behavior,
  dynamic annotations, descriptors, escape/alias identity, callbacks, holes,
  special numbers, invalid indices, worker FIFO, startup enqueue, coalescing,
  drain exceptions and lost-wakeup races. Existing worker tests cover lifecycle
  and stdin behavior. Microbenchmark smoke checks validated both interface and
  alias variants at all four sizes.

The candidate-only MemoryDiagnoser run completed all **24 cases** (three methods,
four sizes, two declarations), using an in-process Short job with one warmup and
three measured iterations at a 100 ms target. At 20,000 records, the full
interface and alias variants both allocated approximately **5.165 MB/op**. The
escaping construction control allocated **7.086 MB/op**, and traversal of those
prebuilt records allocated **0.960 MB/op**. These controls expose remaining costs
when arrays escape the private-function proof; they do not add up to the full
kernel. The short BDN run supports allocation attribution, not a separate claim
about stable elapsed time.

Useful commands from the repository root:

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --no-restore -m:1 `
  -p:UseSharedCompilation=false --filter 'FullyQualifiedName~Array|FullyQualifiedName~Rest|FullyQualifiedName~Numeric|FullyQualifiedName~CompactObjectRecordTests|FullyQualifiedName~Worker'
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release --no-restore -m:1 `
  -p:UseSharedCompilation=false --filter 'FullyQualifiedName~LocalRecordNumericArrayTests|FullyQualifiedName~CompactObjectRecordTests|FullyQualifiedName~UnboxedNumberArrayReadTests|FullyQualifiedName~ArrayQueue|FullyQualifiedName~StableNumericHotPathTests'
./benchmarks/cross-runtime/run-benchmarks.ps1 -Smoke -NoBuild
./benchmarks/cross-runtime/test-snapshot.ps1
node --experimental-strip-types --experimental-transform-types --no-warnings benchmarks/cross-runtime/test-worker-allocation.mjs
dotnet benchmarks/micro/SharpTS.Microbenchmarks/bin/Release/net10.0/SharpTS.Microbenchmarks.dll --smoke
dotnet benchmarks/micro/SharpTS.Microbenchmarks/bin/Release/net10.0/SharpTS.Microbenchmarks.dll `
  --filter '*WorkerAllocationBenchmarks*' --job Short --inProcess `
  --warmupCount 1 --iterationCount 3 --iterationTime 100 --artifacts .perf-worker-allocation-bdn
```

Use the [local performance protocol](../../benchmarks/local-perf/README.md) for
future paired runs; use the cross-runtime environment controls with `-NoSnapshot`
for custom budgets. Keep compiler closure, GC profile, source inputs, launch
order and runtime versions together. The public snapshot was not refreshed.

Retained evidence lives in [worker-allocation-evidence](worker-allocation-evidence/README.md):
per-launch timing rows and summaries, launch schedules, all-thread GC readings,
adaptive measurements, kernel probes, MemoryDiagnoser output and paired
conformance outcomes. The metadata includes compiler and kernel hashes.

## Remaining scope

No newly eligible numeric array can escape to an iterator, array builtin,
serialization, structured cloning or .NET interop. Those uses retain boxed
storage from construction. Consequently a new forced-transition benchmark is
deferred; the escaping-construction microbenchmark is the fallback control.
Construction and traversal are not an additive decomposition of the optimized
full kernel, because returning construction results deliberately crosses the
escape boundary.

Windows timing should be repeated on a quiet host before setting release
thresholds. Linux testing here covered the benchmark executions, not a full
Linux test/conformance suite. ARM/macOS, Native AOT and a broader GC profile
decision remain separate validation work. Worker compilation caching and
removing the benchmark's allocations are outside this change.
