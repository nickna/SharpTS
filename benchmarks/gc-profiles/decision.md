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
cold-start probe at commit `5c5333d53baa046fac6013188ebbb32c32851928`. Each cell used three
independent processes and rotated profile order between launches. The largest standardized input
from every benchmark line was compared. The harness rejected a dirty source tree before building,
and the metadata records the clean commit and complete host/container runtime inventory.

- Windows x64: .NET SDK 10.0.400/runtime 10.0.11 and Node 24.19.0.
- Ubuntu 24.04 x64: the same compiled assemblies in the digest-pinned Docker image, with .NET 10
  and Node 24.19.0. Workloads ran as the non-root `app` user with read-only source mounts.
- Inner benchmark mean/minimum came from the existing cross-runtime protocol.
- Process elapsed time and peak working set came from `System.Diagnostics.Process` on Windows and
  GNU `time` inside the Ubuntu container.
- A separate quiet-host Windows JSON run used five launches per cell to confirm the acceptance
  target without the full matrix competing for resources.

The complete result table is preserved in
[full-corpus-windows-ubuntu.md](results/full-corpus-windows-ubuntu.md), its environment in
[full-corpus-metadata.json](results/full-corpus-metadata.json), and the focused confirmation in
[json-windows-confirmation.md](results/json-windows-confirmation.md).

The process matrix records mean, minimum, standard deviation, process elapsed time, and peak RSS
for every launch. Allocation and generation counts come from the faithful BenchmarkDotNet phase
diagnostic cited below; the cross-runtime `BENCH` protocol does not expose managed GC counters to
Node and therefore does not pretend its sixth timing field is allocation data.

## Evidence

The focused Windows JSON N=10,000 confirmation measured:

| Runtime/profile | Median mean | Relative to Node | Median process | Median peak RSS |
| --- | ---: | ---: | ---: | ---: |
| workstation | 3.8156 ms | 1.39x | 1133.8 ms | 62.3 MB |
| adaptive | 2.1110 ms | 0.77x | 1142.7 ms | 95.1 MB |
| throughput | 1.6760 ms | 0.61x | 1180.8 ms | 727.1 MB |
| Node | 2.7534 ms | baseline | 1242.1 ms | 96.4 MB |

Adaptive therefore clears both JSON gates (no more than 2.6 ms and no more than 1.15x Node). It
adds about 33 MB peak working set over workstation for this workload. Fixed server GC is slightly
faster here but adds roughly 665 MB, making it a poor general recommendation.

Cold-start medians in the same run were 84.62 ms / 21.4 MB for workstation, 90.41 ms / 24.1 MB for
adaptive, 85.04 ms / 23.3 MB for throughput, and 97.57 ms / 50.8 MB for Node. The differences are
small in absolute time, but adaptive's process memory is a real deployment tradeoff.

Existing BenchmarkDotNet evidence recorded on #1462 complements the process matrix: the full JSON
operation allocates about 3.314 MB/op and observed roughly 242 Gen2 collections per 1,000 full
operations (168 per 1,000 build-plus-stringify operations). The matrix intentionally measures the
runtime policy outcome; it does not replace allocation-reduction work.

## Why adaptive is not the default

The full corpus showed that server GC is workload-sensitive. Adaptive improved JSON by 37.8% on
Windows and 9.7% on Ubuntu, but it exceeded the 10% largest-input guardrail in several categories:

- array methods: +175.2% Windows, +113.6% Ubuntu;
- map/set operations and iteration: +45.0% to +114.5%;
- numeric array push/write: +15.0% to +34.5%;
- typed arrays: +37.1% Windows, +54.6% Ubuntu; regex: about +36% on both;
- class construction: +50.3% Windows, +26.9% Ubuntu; Ubuntu accumulation: +22.8%.

Sub-millisecond async cases also produced large percentages from very small absolute changes; they
reinforce that a universal server default would be unjustified. These are explicit tradeoffs of
the opt-in adaptive profile, not silent regressions in the default.

Fixed throughput GC commonly reached 616-759 MB peak RSS and was not consistently faster than
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
