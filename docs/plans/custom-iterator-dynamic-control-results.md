# Dynamic custom iterator implementation results

Implementation of [the iterator performance plan](custom-iterator-dynamic-control.md).

## Changes

- Capture the iterator's `next` value once during acquisition in compiled
  `for...of`, the generic iterator wrapper, the async-from-sync adapter, and the
  interpreter, including the `Math.sumPrecise` consumer. Later mutations remain
  visible to explicit property accesses and
  subsequent acquisitions. Calls retain the original receiver.
- Dispatch admitted zero-argument methods through the fixed-arity
  `MethodInvoker.Invoke(target, receiver)` overload. Cached function metadata
  excludes argument capture, parameter adjustment, and conversion from this path;
  other callables use existing dispatch with `Array.Empty<object>()`.
- Represent numeric `{ value, done }` results with the existing compact reference
  record infrastructure, including when results escape. Each call still returns
  a fresh ordinary object. Reads guard materialization and descriptor overlays
  before accessing fields; unknown shapes use ordinary property access.
- Prove numeric function-owned closure fields independently of stable iterator
  eligibility. The initial proof admits a single capturing callable, resolved
  ownership, unambiguous names, definite numeric initialization, and numeric
  writes. Ambiguous bindings, additional capturing callables, eval, suspension,
  and undefined-reachable values retain existing storage.
- Bind interpreted iterator methods once and avoid constructing intrinsic
  function metadata that a bound view immediately replaces with shared storage.
- Preserve lexical arrow receivers during compiled dictionary property lookup,
  and pad omitted parameters before a rest parameter with the same undefined
  policy as ordinary calls. Regression tests found both issues in the baseline.
  The imported `nextTick` wrapper also rejects the resulting undefined sentinel
  for a missing callback.

The dynamic benchmark retains its alias and `alias.next = alias.next` statement.
Its compiled loop still calls the generic iterator protocol. Numeric result
representation does not authorize stable iterator specialization or numeric
checksum accumulation; the consumer retains dynamic addition.

## Measurement method

Final baseline: `95923476d6dadca5e119b5192cc2122b7ad2d3d7` (current main at final
integration). It was built in an isolated checkout after unrelated performance
PRs landed. The initial investigation's `88378dce` Release binaries were also
frozen before implementation, but final comparisons use current main. Both compilers use the
same final benchmark sources, differing from the originals only by the expected
checksum passed to `bench`. No shared timing configuration or published snapshot
was changed. Comparisons invoke the pinned compiler binaries directly rather
than relying on a separately configured personal `perf-local.ps1` lab.

Environment: Windows x64, .NET SDK 10.0.400 / runtime 10.0.11, Node 22.23.2.
Full-workload comparisons use five process launches, alternating baseline and
candidate order, with the original imported timing driver. Each launch emits
both existing problem sizes and checks the checksum. Node is a reference using
the same sources. This task did not launch tests or builds concurrently with
timed samples; other .NET processes were observed on this shared host.

The new BenchmarkDotNet classes compile the original modules and call their
workload functions using cached delegates. The interpreter attribution cases
execute their original function ASTs without the timing driver. Direct `next`
and ordinary-call controls deliberately omit parts of the complete workload.
Allocation probes use a separate three-second warmup and report managed bytes
and Gen0 counts. Results from these routes must not be substituted for the
cross-runtime timing driver.

## Full-workload timings

Milliseconds below are medians of five launch means with the unchanged 100 ms
warmup and 300 ms sampling budget. The [per-launch means](custom-iterator-launch-means.csv)
are retained alongside this report.

| Workload | Values | Compiled baseline → candidate | Interpreter baseline → candidate | Node reference |
| --- | ---: | ---: | ---: | ---: |
| Dynamic | 10,000 | 4.878 → 1.423 | 42.211 → 25.233 | 0.0351 |
| Dynamic | 100,000 | 15.277 → 3.674 | 101.957 → 64.909 | 0.3195 |
| Stable | 10,000 | 0.0453 → 0.0495 | 45.163 → 30.317 | 0.0439 |
| Stable | 100,000 | 0.2203 → 0.2380 | 113.159 → 80.896 | 0.3862 |

For the dynamic 100,000-value case, the median of the five paired
candidate/baseline ratios is **0.237 compiled** and **0.643 interpreted**:
approximately 76% and 36% less elapsed time, respectively. Each launch improved
in both modes. The compiled dynamic path remains substantially slower than Node.

Stable compiled timing is inconclusive. Its paired ratio was 1.080 at 100,000
values. Three additional launches with `SHARPTS_BENCH_WARMUP_MS=3000` measured
baseline/candidate pairs of 0.1727/0.1913, 0.1638/0.1712, and 0.1743/0.3489 ms;
the last candidate launch had a 0.2277 ms within-launch standard deviation.
A separate warmed direct-delegate probe favored the candidate (0.1598 versus
0.1715 ms median), and normalized IL for the stable workload and its `next` body
is identical. A quiet-host timing gate is still needed to resolve small stable
path differences. No stable-path timing improvement is claimed here.

## Allocation and attribution

Managed bytes per complete 100,000-value call from three samples after a
three-second warmup:

| Route | Baseline bytes | Candidate bytes | Reduction |
| --- | ---: | ---: | ---: |
| Compiled dynamic, original workload delegate | 36,804,958 | 8,003,280 | 78.3% |
| Compiled stable, original workload delegate | 2,680 | 2,680 | Constant allocation |
| Interpreter dynamic, original function AST | 341,612,592 | 169,607,760 | 50.4% |
| Interpreter stable, original function AST | 341,610,800 | 169,607,608 | 50.4% |

Compiled dynamic allocation scales at approximately **368 → 80 B/value**.
Its Gen0 counts fell from 21–22 to 4–5 across ten calls. Interpreter Gen0 counts
fell from 198–199 to 98–99 across ten calls. The stable compiled probe measured
2,680 bytes at 1,000, 10,000, and 100,000 values in both versions.

The isolated baseline `next` body allocated 288 B/value; the candidate
BenchmarkDotNet body case allocated 32 B/value. This attributes the producer
reduction without removing result identity or comparing an incomplete producer
with a complete consumer.

All ten new BenchmarkDotNet ShortRun cases completed successfully (one launch,
three warmup and three measured iterations). Candidate results:

| Case | Values | Mean | Allocated per call |
| --- | ---: | ---: | ---: |
| Compiled dynamic | 1,000 | 35.99 µs | 80.80 KiB |
| Compiled dynamic | 10,000 | 332.74 µs | 783.96 KiB |
| Compiled dynamic | 100,000 | 3.290 ms | 7,815.31 KiB |
| Compiled stable | 1,000 / 10,000 / 100,000 | 7.03 / 21.21 / 177.06 µs | 2.64 / 2.64 / 2.66 KiB |
| Dynamic `next` body only | 100,000 | 285.8 µs | 3.05 MiB |
| Ordinary generic call control | 100,000 | 5.113 ms | 12.21 MiB |
| Interpreter dynamic | 100,000 | 61.49 ms | 161.75 MiB |
| Interpreter stable | 100,000 | 59.88 ms | 161.75 MiB |

ShortRun confidence intervals are wide, especially for interpreter cases. Use
these results primarily for allocation attribution, and the paired original
drivers for cross-runtime comparisons.

## Validation

- Full suite on the integrated implementation: **18,084 passed, 3 skipped,
  0 failed**. Four conservative xUnit collections were used via a temporary
  output configuration; the repository's runner configuration was restored.
- After the final conservative catch/class binding guard, a rebuilt focused run
  passed **71 tests**, covering the iterator/result regressions, stable and
  generic IL/allocation assertions, `nextTick`, standalone output, IL verification,
  and Native AOT architecture seams.
- Release builds and the embedded-source/module benchmark smoke check passed.
  All **10** BenchmarkDotNet cases executed and validated their checksums.
- Both original workloads passed checksum validation in every paired compiled,
  interpreter, and Node launch. `git diff --check` passed.

No Native AOT executable was published in this validation run. The standalone
managed output and existing Native AOT seam guards were checked.

```powershell
dotnet test tests/SharpTS.Tests/SharpTS.Tests.csproj -c Release
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --smoke
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --filter '*CustomIterator*' --job Short
```

Local raw evidence is retained in the ignored `.perf-iterator-implementation/`
directory: `paired-results.txt`, `compiled-alloc-*.txt`,
`interpreter-alloc-*.txt`, `stable-warmup-results.txt`, `micro-validated/`, and
`tests/`. Baseline binaries are preserved in `main-baseline-binaries/`; the
temporary source checkout was removed so benchmark project discovery remains
unambiguous.

## Remaining work

Interpreter environment reuse is deferred. The current change removes repeated
binding and discarded intrinsic metadata, while function calls, result objects,
and per-iteration lexical environments still allocate. Reusing environments
requires a separate lifetime proof covering retained closures, eval, and debugger
observation. The plan explicitly allows this evidence-based deferral.

Compiled result objects still allocate, and generic value access and checksum
addition still box numbers. Removing those costs requires further guarded
specialization that preserves result identity, string concatenation, coercion,
and mutation. The ordinary generic-call control is intentionally unchanged.
