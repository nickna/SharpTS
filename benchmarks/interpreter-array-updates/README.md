# Interpreted Array indexed updates

This bounded probe measures interpreted numeric Array increment/decrement and
checks fractional-key, indexed-accessor, object-key and frozen-array fallbacks.
It verifies every sample's output. It does not establish a CI performance limit.

Build a Release compiler, then point the probe at its complete output directory:

```powershell
$env:DOTNET_PROCESSOR_COUNT = '2'
dotnet build src/SharpTS/SharpTS.csproj -c Release -m:1
$taskLibrary = (Resolve-Path src/SharpTS/bin/Release/net10.0).Path
dotnet build benchmarks/interpreter-array-updates/InterpreterArrayUpdates.csproj `
  -c Release --artifacts-path artifacts/array-update-benchmark `
  -p:LibraryDirectory="$taskLibrary" -m:1
dotnet artifacts/array-update-benchmark/bin/InterpreterArrayUpdates/release/InterpreterArrayUpdates.dll 20000 15
```

The arguments are loop iterations and measured samples. Each case has three
unreported warmups. Parsing and interpreter construction happen before timing;
the measured region is synchronous `Interpreter.Interpret`, including its setup
and console writes. Disposal and output validation follow timing. Allocations use
`GC.GetAllocatedBytesForCurrentThread`; these cases launch no asynchronous work.
Dense and fractional cases perform four updates per iteration, while the other
cases perform one. Compare a case between builds, not its absolute cost against
another case.

## Recorded decision for #3434

Two frozen Release source snapshots contained 1,866 identical input files except
`Interpreter.Helpers.cs`. Their only code difference was the guarded Number-valued
dense update path in `IncrementIndex`. Both snapshots built without warnings or
errors, with the same SDK, configuration and explicit MinVer override. The
unchanged helper was measured before applying the candidate. Source and assembly
hashes, all measured samples and exact guest sources are in
[measurements.json](measurements.json).

Windows x64 / .NET SDK 10.0.403 / runtime 10.0.14, with
`DOTNET_PROCESSOR_COUNT=2`, three pairs of fresh processes and 15 samples per case:

| Dense workload: 80,000 updates | Baseline median | Candidate median |
| --- | ---: | ---: |
| Pair 1 | 45.16 ms | 12.99 ms |
| Pair 2, reversed process order | 43.75 ms | 14.12 ms |
| Pair 3 | 49.27 ms | 13.05 ms |
| Allocated bytes, every pair | 102,567,208 | 23,205,448 |

The candidate reduced this workload's allocation by 77.4% and its observed median
time by 67.7–73.5%. The gain justifies retaining the small path. It accepts only a
present Number-valued data element under a finite integral Number key and still
uses the existing guarded dense write. It executes no guest numeric conversion;
objects, BigInts, accessors, holes, sparse storage, arguments and integrity
failures retain ordinary reference operations.

All fallback outputs remained correct. Their allocations stayed unchanged apart
from at most 288 bytes per complete sample; timings varied, including frozen-array
medians from 64.25 to 92.17 ms against 68.25 to 75.78 ms in the baseline. These
observations do not claim a fallback speedup or overall suite improvement.

Semantic controls also exposed two preexisting boundaries: canonical numeric
String-key updates throw ([#3473](https://github.com/nickna/SharpTS/issues/3473)),
and sloppy arguments do not map index writes to simple parameters
([#3476](https://github.com/nickna/SharpTS/issues/3476)). Their original failures
were retained separately. They provide no timing samples. The committed semantic
tests use an explicit strict/unmapped arguments control and retain the ordinary
Number/BigInt, accessor, descriptor-mutation, hole, integrity and async-reference
oracles.
