This optional runner isolates the checker cost of source member provenance. It is outside the solution and has no effect on normal builds.

Run from the repository root with .NET 10 and PowerShell:

```powershell
./benchmarks/member-provenance/run.ps1
```

The script archives the exact `f620d279` baseline into ignored `artifacts/member-provenance-benchmark`, without source instrumentation. It builds that compiler and the current compiler, then builds the same runner against each DLL directory. `-PackageCache <path>` supports an existing offline NuGet cache. `-OutputDirectory <path>` chooses another scratch directory; `-SkipLibraryBuilds` reuses already built libraries there.

Each check receives a fresh graph already parsed with editor syntax enabled. The small fixture covers instance/static/private members, accessors, literal indexing, calls, writes and private presence. The 25-module fixture adds inherited members, generic classes and imported generic receivers. Pure VFS resolution disables disk fallback and default declaration libraries. Preparation, lexing, parsing, resolution and fingerprinting occur outside the measured interval. Timed work includes constructing a checker, checking the graph, freezing lexical/member indexes and copying diagnostics. The baseline and capture-disabled current variant both freeze the existing lexical index; the enabled variant also freezes member provenance.

There are 30 warmups and 31 batched samples per variant. Tiered compilation is disabled for reproducibility. Every batch starts after a full GC with its fresh graphs alive. Results include median/p90 latency, per-thread allocated bytes and a noisy full-GC live heap delta from a prepared graph to a retained checked publication. The publication holds types, diagnostics and frozen indexes, without the mutable checker. The member estimate is the index's documented approximation, separate from actual heap measurements.

The script rejects differences in three fingerprints computed outside timing: diagnostic contents/positions; public structural expression/class types; and lexical declaration/use identity partitions. Existing numeric nominal class IDs are normalized to ordinals in deterministic graph-traversal order, including class IDs in access brands. Zero remains distinct, and all nonzero identity equivalence partitions remain in the fingerprint. IDs are neither omitted nor merged by spelling or source shape. Other fields, source ranges and declaration/use groupings remain in the fingerprints. Fixtures exclude parameter properties because their intentional rename-eligibility policy change is verified by focused tests. The runner measures cold checking of a warm process; it does not measure snapshot cache hits, editor latency, CLR metadata or parsing.

The recorded Windows ARM64/.NET 10.0.12 run is in [results-2026-10-07.json](results-2026-10-07.json). All three fingerprints match across the baseline and both current variants. Median ordinary checking changes by +2.55%/-4.04% for the small/multi-module fixtures; allocation changes are +0.207%/+0.230%. Neither ordinary case exceeds the 10% investigation threshold. This single-machine run does not establish a speedup or process-to-process variance.

| Fixture | Baseline median | Current, capture off | Current, capture on | Extra cold check | Extra allocated / retained bytes |
| --- | ---: | ---: | ---: | ---: | ---: |
| Small | 0.1328 ms | 0.1361 ms | 0.3224 ms | 0.1863 ms | 170,216 / 11,904 |
| 25 modules | 87.0108 ms | 83.4938 ms | 101.7982 ms | 18.3045 ms | 8,880,176 / 583,904 |

Enabled checking is 2.368×/1.219× the current capture-disabled time and allocates 2.513×/2.369× as many bytes. Those allocations are transient work as well as retained output; they are not heap growth. Code inspection identifies repeated declaration grouping, actual-lookup proof scopes and candidate combination, owner fact maps, and frozen copies/sorts as additional work. This is an attribution from code inspection, not measured phase profiling. The extra retained output is about 476/477 bytes per published symbol or occurrence across these two fixtures, consistent with collection/object overhead scaling with the captured records.

The frozen index reports 6 symbols/19 occurrences and 300 symbols/923 occurrences. Its estimates are 4,896/247,104 bytes, smaller than measured additional retained heap of 11,904/583,904 bytes. Estimates omit some runtime collection/object overhead. The snapshot cache uses an estimated memory budget, not a strict process heap limit. Source text, parser metadata and normal checking remain separate costs. Existing snapshot reuse avoids paying the cold check on every request; final epic validation in #1981 still needs end-to-end interactive measurements before drawing conclusions about editor latency.
