# Language hot-path implementation results

Measurement date: September 4–5, 2026. Baseline revision:
`af72038b83e9c88449b33ca42262f3f8dc156ea7`.

This implements the six stages in the
[approved plan](language-hot-paths-implementation.md). The original benchmark
workloads and arithmetic trees remain intact; two additional cases exercise
alternating eligible targets and mixed eligible/ineligible targets.

## Delivered changes

| Stage | Commit | Change |
| --- | --- | --- |
| 0 | `e7ba5250` | Permanent storage and dispatch controls, benchmark checksums, and fallback coverage |
| 1 | `58caccbd` | Guarded reads from boxed numeric arrays and plain lists, preserving ordinary property lookup for unsupported values |
| 2 | `34d04197` | Independent conversion flags and cached parameter metadata for ordinary function wrappers |
| 3 | `e30173e2` | Fresh numeric storage for ordinary rest arguments; materialization at existing `arguments` and enumeration boundaries |
| 4 | `c03e0e74` | Numeric-preserving spread construction, capacity reservation, and correct custom-iterator completion handling |
| 5 | This change | Optional four-double entry point on eligible callable values, with ordinary dispatch fallback |

The typed entry is attached to the actual callable method. Calls capture the
callee before evaluating arguments, evaluate each argument once, and use the
entry only when all four emitted argument values are native doubles. The
existing companion limits remain 32 rest arguments, eight variants per function,
and 64 variants per compilation; direct-call variants have priority.

## Allocation

Approximate bytes per inner iteration/call on .NET 10 x64:

| Workload | Original | Final | Reduction |
| --- | ---: | ---: | ---: |
| Four reads from reused boxed numeric storage or a plain list | 96 | 0 | 100% |
| Pre-created eligible indirect target | 456 | 0 | 100% |
| Pre-created ordinary dispatch returning a rest array | 360 | 296 | 18% |
| Escaping four-number rest array | 240 | 144 | 40% |
| Ordinary rest packing/length | 264 | 144 | 45% |
| Varying-index rest | 344 | 128 | 63% |
| Spread rest | 280 | 120 | 57% |
| Spread returning a rest array | 208 | 144 | 31% |
| Alternating eligible targets | 456 | 0 | 100% |
| Alternating eligible and mutating targets | 504 | 184 | 63% |

The numeric-storage read control remains allocation-free. Direct/fixed/constant
scalar controls also remain allocation-free. The alias and unknown-target
benchmarks have 96 bytes of fixed setup per outer invocation; alternating targets
have 192 bytes. The pre-created callable control establishes zero allocation in
the eligible indirect loop independently of that setup.

The stage-by-stage observations separate overlapping savings: boxed reads remove
96 bytes from the affected call; cached conversion metadata then removes 64 bytes
from ordinary rest-only dispatch; numeric construction reduces storage costs;
the final typed entry removes the remaining 296 bytes on eligible indirect calls.
These contributions must not be added across different workload shapes.

The permanent [microbenchmark observations](../../benchmarks/cross-runtime/results/language-hot-paths-micro.json)
include each stage's compiler hash, raw samples, allocation, and GC counts.
BenchmarkDotNet 0.14.0 ShortRun used one in-process launch, three warmups, and three
measurements. These runs establish allocation changes. Their timing differences
are diagnostic because machine throughput varied substantially between stages.
The microbenchmark project enables tiered compilation with QuickJit disabled.
The CLI comparison and capability probe use their default runtime JIT settings.

## Paired throughput

At N=100,000, the three original slow cases improve in every pair. Spread is
**10.63× faster**, unknown targets **138.61×**, and varying indices **12.03×** by
median paired ratio. Alternating eligible targets improve **100.35×**; mixing
eligible and mutating targets improves **3.94×** while preserving fallback.

Times below are medians of each launch's measured mean, in milliseconds.
Speedups summarize the five paired baseline/candidate ratios, with their range;
the time columns summarize each runtime independently.

| Workload | Original ms | Final ms | Node ms | Paired speedup (range) |
| --- | ---: | ---: | ---: | ---: |
| spread-numeric-rest | 17.086 | 1.597 | 0.396 | 10.63× (10.23–11.00) |
| unknown-target-numeric-rest | 21.964 | 0.157 | 0.050 | 138.61× (71.84–142.31) |
| varying-index-numeric-rest | 17.752 | 1.447 | 0.316 | 12.03× (11.86–12.34) |
| alternating-target-numeric-rest | 25.500 | 0.254 | 0.519 | 100.35× (86.80–151.96) |
| mixed-target-numeric-rest | 39.593 | 7.921 | 0.891 | 3.94× (3.16–7.01) |
| numeric-compound | 0.040 | 0.039 | 0.040 | 1.00× (0.98–1.02) |
| numeric-assignment-control | 0.039 | 0.040 | 0.040 | 1.00× (0.97–1.02) |
| stable-numeric-rest | 0.055 | 0.055 | 0.050 | 1.00× (0.99–1.10) |
| flattened-rest-control | 0.059 | 0.058 | 0.055 | 1.00× (0.56–1.05) |
| left-associated-accumulation | 0.156 | 0.156 | 0.156 | 1.00× (0.85–1.02) |
| indirect-numeric-rest | 0.055 | 0.056 | 0.050 | 0.99× (0.86–2.11) |
| dynamic-index-numeric-rest | 0.057 | 0.057 | 0.051 | 0.99× (0.96–1.16) |
| generator-range | 0.232 | 0.226 | 0.883 | 0.91× (0.55–3.34) |
| parse-integers | 0.039 | 0.039 | 1.139 | 1.00× (0.98–1.02) |
| format-fixed | 3.247 | 3.444 | 8.471 | 1.03× (0.89–1.80) |

Arithmetic and rest scalar controls have median paired ratios near 1.00.
Some launches vary sharply, particularly generator-range, whose median paired
ratio is 0.91× with a 0.55–3.34× range. Those controls need a controlled-host
rerun before setting tight timing budgets. The improvements do not establish general
Node parity. Several remaining allocation/dispatch-heavy shapes still trail Node.

All **675 observations** are retained in the
[raw measurements](../../benchmarks/cross-runtime/results/language-hot-paths-measurements.csv),
with [all-size summaries](../../benchmarks/cross-runtime/results/language-hot-paths-summary.csv)
and the [comparison manifest](../../benchmarks/cross-runtime/results/language-hot-paths-manifest.json).

The comparison uses the same checked-in TypeScript workload for both frozen
compilers and Node. Each case/runtime/launch gets a fresh process. Baseline and
candidate order alternates across five launches. The copied harness uses a
500 ms warmup and its original 300 ms sampling budget at N=1,000, 10,000, and
100,000. Compilation, builds, tests, and allocation probes run outside the timed
comparison. Existing checksum validation remains enabled.

## Typed-entry setup and fallback cost

Five fresh-process pairs compare stage 4 with the final closed-entry implementation,
using the probe's default .NET runtime settings. At N=100,000, eligible calls fall
from roughly 5.05 ms to 0.17 ms (31.34× median paired speedup, range 26.88–33.62×),
with allocation falling from 296 bytes/call to zero.
Pure mutating/ineligible calls retain approximately 368 bytes/call and comparable
throughput (0.99× median paired ratio, range 0.95–1.06×; roughly 10 ms per
100,000 calls). The
[raw capability results](../../benchmarks/cross-runtime/results/language-hot-paths-capability.json)
include both input sizes and every timing sample.

| Cost on the NumericRest fixture | Stage 4 | Final |
| --- | ---: | ---: |
| Total allocation per warmed eligible-wrapper construction | 1,032 bytes | 1,600 bytes |
| Total allocation per warmed ineligible-wrapper construction | 1,048 bytes | 1,056 bytes |
| Median eligible-wrapper construction | 4.17 µs | 6.50 µs |
| Emitted assembly | 270,336 bytes | 270,848 bytes |
| Total emitted method/constructor IL | 177,184 bytes | 177,565 bytes |
| Median CLI compilation, including startup | 1.025 s | 1.022 s |

Constructor totals include reflection temporaries and delegate setup. Ineligible
wrappers gain one reference field (8 bytes on this host), without allocating a
delegate. Eligible wrappers pay additional setup; callers must reuse them enough
to amortize it. The adapter binds to the wrapper already being constructed and
allocates no separate target object. Each exposed capability adds one small
adapter, bounded by the existing 64-companion compilation limit.

The first implementation used an open static delegate. The standalone probe
exposed an AVX/JIT-dependent slowdown despite zero allocation, suggesting a cost
in the open delegate's floating-point argument shuffle. A closed static adapter
removes the measured slowdown under default settings. The
[initial probe observations](../../benchmarks/cross-runtime/results/language-hot-paths-capability-default-tiering.json)
are retained. The final implementation changes no runtime JIT/ISA settings.
The binding uses the documented
[closed static delegate semantics](https://learn.microsoft.com/en-us/dotnet/fundamentals/runtime-libraries/system-delegate-createdelegate).

The [capability probe](../../benchmarks/cross-runtime/probes/RestCapabilityProbe/Program.cs)
constructs wrappers through compiled constructor delegates, keeping reflection
invocation outside the measurement. It separately measures pre-created eligible
and mutating/ineligible targets at two loop sizes. Constructor measurements use
warmed method/invoker metadata; CLI compile measurements include process startup.

## Verification and remaining limits

The Release build passed with zero warnings and errors. All **599 focused checks**
passed again against the final closed-entry compiler. Compiler hashes confirm
that the tests used the frozen measured candidate.

The complete test project ran to completion: **17,680 passed, 283 failed, and
3 skipped** (17,966 total). The full run used the initial open-static entry and
predates the final two added cases; the final refinement reran the focused suite.
It is not a green full-suite result. The
[validation record](../../benchmarks/cross-runtime/results/language-hot-paths-validation.json)
retains every failing test and its error excerpt:

- 232 failures report an invalid `HttpListener` handle.
- 17 report certificate-import access denied; one additional test expected a
  timeout but received that certificate error.
- 27 report socket/module execution timeouts, across compiled and interpreted modes.
- Two child-process IPC tests return no output, and one interpreted cluster test
  reports the HTTP handle error instead of its expected response.
- Three standalone probes fail on TLS authentication or subprocess exit timing.
  The two exit-timeout probes already printed their expected missing-runtime errors.

These failures concern host services and process behavior. No failure in the
completed run identifies a regression in the changed compiler-language paths.
Rerun the full suite on a host with working HTTP listeners, certificate key
storage, and IPC before treating it as a release gate.

Semantic and IL tests cover boxed and numeric storage, special numeric values,
holes, index conversion, descriptors and prototypes, array identity and escape,
mutation and boxing transitions, custom iteration, regular/default arguments,
`arguments`, `for…of`, closures, bound functions, suspension, target replacement
during argument evaluation, mixed targets, budget exhaustion, and standalone
output. Pre-created eligible targets alternate without per-call allocation;
the emitted caller also contains and exercises the generic fallback.

The initial typed capability is limited to detached synchronous calls with four
native-double arguments in numeric consumers. Receiver calls, optional calls,
spreads, incompatible functions, and unsupported argument representations retain
ordinary dispatch. Escaping rest arrays still allocate a fresh observable array.
Array consumers that require `List<object>` still materialize boxed storage.

This work preserves existing compiled CLR-slot coercions. It does not repair
pre-existing differences for foreign values assigned to statically numeric
slots, omitted default arguments before rest, or array iterator accessors
installed through `Object.defineProperty`. Generator lowering, general parser
and formatter work, and floating-point reassociation remain outside this change.

## Reproduction

Build and retain baseline/candidate compiler directories, including dependencies
and runtimeconfig files. Then run the paired workload:

```powershell
./benchmarks/cross-runtime/compare-language-hot-paths.ps1 `
  -BaselineCompiler <baseline-directory>/SharpTS.dll `
  -CandidateCompiler <candidate-directory>/SharpTS.dll `
  -OutputDirectory <new-output-directory> -Launches 5 -WarmupMs 500 -IncludeNode
```

The output includes compiler/source/assembly hashes, runtime versions, all raw
measurements, and launch settings. `-CaseNames` can select particular cases.
Use a new or empty output directory for each run. This comparison has its own
manifest schema and is separate from the stock cross-runtime snapshot format.

For allocation attribution:

```powershell
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- `
  --filter '*NumericArrayReadBenchmarks*' '*NumericRestBenchmarks*' '*NumericRestDispatchBenchmarks*' `
  --job short --inProcess --artifacts <new-artifact-directory>
```

For setup, code-size, and pure-fallback attribution, compile
`benchmarks/micro/SharpTS.Microbenchmarks/TypeScriptSources/NumericRest.ts` with
each frozen compiler, then pass its emitted DLL to:

```powershell
dotnet build benchmarks/cross-runtime/probes/RestCapabilityProbe -c Release
dotnet benchmarks/cross-runtime/probes/RestCapabilityProbe/bin/Release/net10.0/RestCapabilityProbe.dll <emitted-dll>
```

Run five fresh-process pairs with alternating compiler order. Keep compilation
and other activity outside the probe's measurement process.
