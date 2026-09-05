# Shared algorithm performance improvements

Measured September 4, 2026 against baseline `7da32f04` and the updated working
tree. These are local diagnostic results, not a replacement for the published
cross-runtime snapshot.

## Changes

- Preserve promoted string accumulators when reading `.length`. The generic
  string intrinsic previously ran first and materialized the entire builder on
  each scan-loop condition.
- Materialize one contiguous string for a read-only `for` loop's `charCodeAt`
  reads. This avoids repeatedly searching StringBuilder chunks. The promotion
  analysis excludes escaping/captured accumulators; loop analysis excludes
  writes and shadowing declarations. Mutating loops keep the existing path,
  and later scans get a fresh snapshot.
- Keep array `.length` as an unboxed number in synchronous generated IL.
  The existing runtime fallbacks and object-result registry convention remain
  available. This removes about 24 bytes per JSON scan iteration.
- Add independent expected checksums to the algorithm drivers, finite factorial
  inputs with a matching double C# baseline, a bounded arithmetic workload,
  synchronous/asynchronous callback controls, and string scaling cases.
  Add corresponding arithmetic and string scaling microbenchmarks.

The existing shared workloads retain their algorithm bodies. No GC settings
were changed. Callback controls expose the measurement floor; their timings
are not subtracted from workload timings.

## Results

Windows and Ubuntu 26.04 under WSL used .NET SDK 10.0.400 and Node 22.23.2.
Times below are milliseconds per operation: the median of three launch means
from the cross-runtime harness. Final Windows and WSL sweeps ran sequentially,
without concurrent test/build jobs. WSL is a second OS environment on the same
machine, not an independent hardware replication.

| Workload | Platform | Baseline compiled | Updated compiled | Updated Node |
|---|---|---:|---:|---:|
| Strings, 10,000 | Windows | 30.69704 | 0.03051 | 0.06876 |
| Strings, 10,000 | WSL | 30.51715 | 0.04593 | 0.06906 |
| JSON, 10,000 | Windows | 3.93091 | 4.03104 | 2.47746 |
| JSON, 10,000 | WSL | 1.85662 | 1.71868 | 2.26416 |

The string regression is resolved on both platforms. JSON elapsed time is
approximately unchanged on Windows (+2.5%) and improves 7.4% on WSL; this is
not evidence for a universal JSON latency improvement. The Windows baseline
Node JSON time was 3.12077 ms, demonstrating variability between sweeps.

BenchmarkDotNet MemoryDiagnoser reports JSON allocations at 10,000 elements
falling from **3,313.11 KiB to 3,078.73 KiB per operation**: 234.38 KiB (7.1%)
less. Only allocation figures are used from these runs because their timing
overlapped other validation work. Remaining JSON costs warrant separate
serialization/parsing and GC profiling before additional changes.

The new string scaling workload uses the same shared build-and-scan function:

| Elements | Windows compiled (ms) | WSL compiled (ms) |
|---:|---:|---:|
| 10,000 | 0.03212 | 0.05351 |
| 100,000 | 0.42378 | 0.62171 |
| 1,000,000 | 4.12742 | 6.11109 |

These measurements are consistent with approximately linear scaling.

## Validation and reproduction

- 1,022 targeted array, string, and JSON tests passed; none failed or skipped.
  Coverage includes emitted IL and allocation checks, UTF-16/empty strings,
  mutation, nested scans, repeated scans after append, and object consumers of
  array length.
- All 90 Windows benchmark cases completed for compiled SharpTS and Node,
  with three launches each and checksum validation. The generated snapshot
  passed schema validation.
- WSL baseline/candidate string and JSON comparisons and candidate scaling
  cases completed with three launches per runtime.
- Release builds, microbenchmark smoke validation, and `git diff --check`
  passed. This was targeted validation, not a full repository test run.

```powershell
dotnet test tests/SharpTS.Tests -c Release --no-build --no-restore -m:1 `
  --filter 'FullyQualifiedName~SharedTests.Array|FullyQualifiedName~SharedTests.String|FullyQualifiedName~CompilerTests.String|FullyQualifiedName~CompilerTests.StablePrimitiveString|FullyQualifiedName~CompilerTests.Json|FullyQualifiedName~CompilerTests.UnboxedNumberArray'

dotnet run -c Release --no-build `
  --project benchmarks/micro/SharpTS.Microbenchmarks -- --smoke

./benchmarks/cross-runtime/run-benchmarks.ps1 -NoBuild `
  -Workloads fibonacci,factorial,count-primes,strings,objects,closures,array-methods,map-set,json,typed-arrays,binary-trees,classes,async-promises,int-arrays,string-scaling,arithmetic-loop,callback-control `
  -Runtimes compiled,node -Launches 3 -OutputDirectory .perf-algorithms
```

Local raw evidence is retained in the ignored `.perf-investigation/` directory:
`windows-baseline/results.txt`, `windows-final/results.txt` and `snapshot.json`,
`wsl-final-baseline/results.txt`, `wsl-final-candidate/results.txt`,
`wsl-final-scaling/results.txt`, `json-before/results/`, `json-final/results/`,
`array-string-json-tests.txt`, and `micro-smoke.txt`. The summary above is tracked
so it remains available without those local artifacts.
