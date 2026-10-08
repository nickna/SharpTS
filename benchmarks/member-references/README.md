# Source class member reference benchmark

Optional replay for #1980, outside the solution and ordinary tests. It uses the `SharpTS.Tests`
friend assembly name for internal service counters; it adds no server dependency.

```powershell
./benchmarks/member-references/run.ps1
# Offline restore using an already populated package cache:
./benchmarks/member-references/run.ps1 -PackageCache C:/Users/you/.nuget/packages
```

Each query includes declarations and discovers configured projects within initialized roots.
The small fixture has one source file and three references. The larger fixture has 25 source
files, four configurations, project references and closed reverse importers; its 25 references
must be deduplicated across separately checked projects. Both set `noLib: true` and `types: []`
and exclude CLR metadata. A complete result must remain ineligible for member rename.

After three prewarm queries, 11 cache-cold samples use fresh service instances and 31 warm
samples reuse one unchanged capture. JIT and OS file caches are warm. Samples include snapshot
acquisition, discovery, location projection and final physical-input currentness validation.
The runner refuses changed cold/warm result fingerprints or any warm checker invocation.
Captures come through `DocumentStore`, including the URI-to-physical-path spelling used by LSP.
Fingerprinting and payload serialization occur after the measured time/allocation window.
Unexpected source/config files in reused fixture directories cause refusal; use a fresh
`OutputDirectory` to isolate another run.

Reports contain median/p90 latency, process-wide allocations across asynchronous workers,
whole-graph check counts, relative-location payload bytes and estimated cache retention.
Payload bytes describe compact benchmark JSON with relative paths rather than an LSP envelope.
Cache estimates do not measure retained heap, peak working set or a process memory limit.
This measures service behavior; stdio transport and editor UI are covered separately.

The [recorded Windows ARM64/.NET 10.0.12 run](results-2026-10-07.json) used the exact
service source hashes in the report and returned matching cold/warm fingerprints:

| Fixture | Cold median / p90 | Warm median / p90 | Checks cold / warm | Estimated cached bytes |
| --- | ---: | ---: | ---: | ---: |
| One source, one configuration | 12.97 / 13.87 ms | 1.44 / 1.51 ms | 1 / 0 | 26,772 |
| 25 sources, four configurations | 258.62 / 269.04 ms | 8.52 / 9.60 ms | 3 / 0 | 6,891,530 |

The empty solution configuration contributes discovery without another anchor component check.
These are single-machine measurements, not a comparison to a previous member-reference service.
The report also contains allocation medians and relative-location payload sizes (220/2,278 bytes).
