# SharpTS GC profile decision

Decision date: 2026-08-24  
Tracking issue: [#1462](https://github.com/nickna/SharpTS/issues/1462)

## Decision

SharpTS keeps concurrent workstation GC as the default and exposes two explicit server-oriented
overrides:

| Profile | Runtime policy | Intended use |
| --- | --- | --- |
| `workstation` | `System.GC.Server=false`, concurrent GC | Default for small, interactive, one-shot, and unknown workloads. |
| `adaptive` | Server GC, concurrent GC, `System.GC.DynamicAdaptationMode=1` | Recommended for sustained allocation-heavy services after measuring memory and latency. |
| `throughput` | Server GC, concurrent GC, `System.GC.DynamicAdaptationMode=0` | Expert opt-in for deployments where fixed server heaps have demonstrated a net benefit. |

The CLI option is `--gc-profile workstation|adaptive|throughput`; MSBuild projects use
`SharpTSGcProfile`. The selected policy is deployment configuration only. It does not change IL,
JavaScript semantics, module bindings, or ESM behavior.

## Method

The checked-in harness compiled all 18 scripts in `benchmarks/cross-runtime/scripts` plus a tiny
cold-start probe at commit `4189deb66260242c2bb448af8d3cfd185bc3d390`. Each cell used three
independent processes and rotated profile order between launches. The largest standardized input
from every benchmark line was compared.

- Windows x64: .NET SDK 10.0.400/runtime 10.0.11 and Node 24.19.0.
- Ubuntu 24.04 x64: the same compiled assemblies in the checked-in Docker image, with .NET 10 and
  Node 24.19.0.
- Inner benchmark mean/minimum came from the existing cross-runtime protocol.
- Process elapsed time and peak working set came from `System.Diagnostics.Process` on Windows and
  GNU `time` inside the Ubuntu container.
- A separate quiet-host Windows JSON run used five launches per cell to confirm the acceptance
  target without the full matrix competing for resources.

The complete result table is preserved in
[full-corpus-windows-ubuntu.md](results/full-corpus-windows-ubuntu.md), its environment in
[full-corpus-metadata.json](results/full-corpus-metadata.json), and the focused confirmation in
[json-windows-confirmation.md](results/json-windows-confirmation.md).

## Evidence

The focused Windows JSON N=10,000 confirmation measured:

| Runtime/profile | Median mean | Relative to Node | Median process | Median peak RSS |
| --- | ---: | ---: | ---: | ---: |
| workstation | 3.8056 ms | 1.48x | 1117.0 ms | 62.2 MB |
| adaptive | 2.0046 ms | 0.78x | 1119.3 ms | 93.0 MB |
| throughput | 1.6351 ms | 0.64x | 1172.7 ms | 712.1 MB |
| Node | 2.5675 ms | baseline | 1220.4 ms | 77.8 MB |

Adaptive therefore clears both JSON gates (no more than 2.6 ms and no more than 1.15x Node). It
adds about 31 MB peak working set over workstation for this workload. Fixed server GC is slightly
faster here but adds roughly 650 MB, making it a poor general recommendation.

Cold-start medians in the same run were 69.89 ms / 20.3 MB for workstation, 82.22 ms / 24.7 MB for
adaptive, 74.01 ms / 22.9 MB for throughput, and 93.92 ms / 50.9 MB for Node. The differences are
small in absolute time, but adaptive's process memory is a real deployment tradeoff.

Existing BenchmarkDotNet evidence recorded on #1462 complements the process matrix: the full JSON
operation allocates about 3.314 MB/op and observed roughly 242 Gen2 collections per 1,000 full
operations (168 per 1,000 build-plus-stringify operations). The matrix intentionally measures the
runtime policy outcome; it does not replace allocation-reduction work.

## Why adaptive is not the default

The full corpus showed that server GC is workload-sensitive. Adaptive improved JSON by 20.5% on
Windows and 10.7% on Ubuntu, but it exceeded the 10% largest-input guardrail in several categories:

- array methods: +51.0% Windows, +100.8% Ubuntu;
- map/set operations and iteration: +29.2% to +94.2%;
- numeric array push/write: +13.0% to +19.2% in the regressing cells;
- Ubuntu typed arrays: +38.5%, regex: +27.6%, and int32 kernel: +17.1%;
- Windows Fibonacci: +16.7% (no allocation-driven reason to select server GC).

Sub-millisecond async cases also produced large percentages from very small absolute changes; they
reinforce that a universal server default would be unjustified. These are explicit tradeoffs of
the opt-in adaptive profile, not silent regressions in the default.

Fixed throughput GC commonly reached 600-766 MB peak RSS and was not consistently faster than
DATAS. It remains available because some long-lived, memory-provisioned services may measure a win,
but SharpTS does not recommend selecting it without deployment-specific evidence.

## Propagation and guardrails

The profile must agree across every artifact path:

- direct DLL runtimeconfig generation;
- SDK build and publish output;
- NuGet-packaged runtimeconfig files;
- SDK and built-in PEPacker single-file executable bundlers;
- `sharpts app` build/publish and `SharpTS.Gui.Sdk` host runtimeconfig.

Parser, emitted JSON, bundled JSON, package, packaged-SDK consumer, and GUI application tests cover
these boundaries. Invalid profile names fail during CLI/MSBuild validation rather than silently
falling back. Existing standalone, Native AOT, semantic, conformance, and IL-verification gates
remain unchanged because the profile does not participate in compilation semantics.
