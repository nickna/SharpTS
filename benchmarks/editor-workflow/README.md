# Editor workflow benchmark

Optional #1981 replay, outside the solution and timing gates. Build the production Release
language server first, then run this independent `SharpTS.Tests` friend assembly against
those frozen DLLs. It adds no production instrumentation or server dependency.

```powershell
dotnet build src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj -c Release
./benchmarks/editor-workflow/run.ps1 -PackageCache C:/Users/you/.nuget/packages
# Prepare only, then run timings later with no overlapping builds/tests/editors:
./benchmarks/editor-workflow/run.ps1 -BuildOnly -LibraryDirectory /absolute/frozen/LS/outputs
./benchmarks/editor-workflow/run.ps1 -NoBuild -LibraryDirectory /absolute/frozen/LS/outputs
```

`-OutputDirectory` selects a disposable artifact directory. `-PackageCache` uses the existing
package cache and an empty local feed for the runner's restore. `-NoStdio` skips the separate
transport run. The script never builds production projects; do that explicitly before
measurement. Run on an otherwise idle machine, and retain source/binary versions with results.
The build has a 120-second process deadline and each measurement runner a 240-second
process-tree cleanup deadline. These detect hangs and are not latency acceptance thresholds.
Before measurements, copied runner LS/core hashes must match the selected library directory;
`-NoBuild` refuses stale copies rather than mixing internal counters with another stdio binary.

The service sequence uses actual hover, receiver completion, signature help, definition,
member references including declarations, lexical rename preparation and private rename
preparation APIs. Every returned validation is checked. The small fixture is one source/config;
the larger fixture has 25 sources, four configurations, project references and closed reverse
importers, matching the graph shape of the member-reference benchmark. A third small fixture
loads the real default libraries and uses `Array<number>` and `Date`. The first two fixtures
keep `noLib: true` / `types: []` to isolate source graph costs.
`sourceDocumentsInEntrySnapshot` counts published source documents in the entry component,
not all workspace sources or embedded default declaration modules, which have no SourceDocument.
The original raw report called this counter `checkedDocumentsIncludingLibraries`; the compact
report corrects that label without changing its measured value.

The original unannotated instance field `Box.value = 1` is deliberately retained. The current
checker publishes `any` for that field: it checks the initializer without republishing an
inferred instance field type. Both direct-service and actual-wire runs assert the exact
`value: any` hover. This is a checker limitation, not a CLR-provider or diagnostics race.

After three prewarm sequences, 11 cache-cold samples create fresh services in the warmed
process; 31 warm samples reuse identical captures/carets. The default-library fixture
uses three cold and seven warm samples to bound replay cost. Cold/warm result fingerprints
must agree, and unchanged warm requests must perform zero whole-component checks. Seven
changed-open-version sequences and seven dirty-dependency sequences are separate phases.
They report actual rechecking rather than claiming incremental compiler updates.
Fresh service construction is timed separately from its cache-cold query.

Completion/signature recovery measures the first cursor graph, 15 identical-caret repeats,
then eight distinct carets in the same buffer and seven edited buffer versions. Different carets/versions must produce fresh checks;
identical carets must reuse their graph. Completed retained cursor entries must stay within
four, inside the ordinary eight-entry/64 MiB estimated cache budget.

The rapid-edit case gates the existing `BeforeCheck` test seam, submits 24 distinct captured
versions, cancels obsolete waiting requests and releases shared work. Runner-only reflection
under the service's own gate observes admission, active-build slots and retained cursor
entries. It adds no public protocol method and performs no inference. Results record canceled
waiters, the blocked admission observation, settlement/slot cleanup and a fresh request's
latency. Cancellation of a waiter does not imply its shared build was canceled.
The fresh sample's process-wide check count includes shared builds draining after release;
it is not the number of checks owned exclusively by the fresh request.

The CLR case alternates actual core/server assembly bytes at a custom reference path and
requires distinct metadata generations. It reports unchanged-generation costs separately
from replacement/recheck costs. Capture, content hashing and physical currentness validation
are included; no restore or executable assembly loading is introduced.

Time and process-wide allocation windows include service acquisition, final validation and
consumer projection. Serialization/fingerprinting occurs afterward. Service payload sizes
describe compact benchmark DTOs, not LSP wire messages. Managed heap is observed after full
GC in an empty session, with active completed caches, and after invalidation with leases and
query results released. Differences remain process-wide observations affected by runtime
state; estimated graph bytes, allocation churn and retained heap are distinct quantities.
Active leases, in-flight work, CLR/runtime state and estimator error are outside the completed
cache estimate; it is not a strict process-memory bound.

`stdio.mjs` uses the same on-disk fixtures in three fresh full-mode processes each. It reports
spawn-to-initialize separately from first-sequence cost, then 15 unchanged sequences in one
process. It uses the standalone `--diagnostics all` preset; asynchronous diagnostics can
share or contend for analysis. Payload sizes include parsed JSON-RPC response fields and
exclude Content-Length framing and notifications. These runs observe no internal counters,
so their latencies are not presented as checker-count measurements.
Fingerprints preserve semantic fields and normalize fixture URIs plus only OmniSharp's random
per-process completion routing ID. Payload byte counts retain that routing data and JSON-RPC IDs.

These new features report absolute costs. Existing equivalent-path baseline comparisons
remain in [editor analysis](../editor-analysis/README.md), [parser syntax](../editor-syntax/README.md),
[member checking](../member-provenance/README.md) and [semantic metadata](../editor-semantics/README.md).
Use their exact archived baselines/options/fingerprints for percentage comparisons; no missing
historical hover/completion/signature implementation is treated as a performance baseline.

## Recorded final measurements

[Compact results](results-windows-arm64.json) were measured on 2026-10-07 local time
(2026-10-08 UTC), Windows ARM64, .NET 10.0.12 and Node 25.5.0, on an otherwise idle machine.
The source base was `a20b07d7259043dd06a33fae34a507b443c847d8` plus #1981 working-tree changes.
The final LS SHA-256 was `9dc0e3c3b4d401e833735179ee84185c460b9f2dcb2ae187f71f8e808627a40b`;
core SHA-256 was `a2ee2949aace31ab14706d2bf2352125ed94d9fed085cb76ec7b55d95909e45b`.
Replay source fingerprints and binary fingerprints accompany the measurements. Raw samples
remain in ignored `artifacts/editor-workflow-benchmark`.

The seven-feature service sequence reports absolute costs, including currentness checks:

| Fixture | Cold median / p90 ms | Warm median / p90 ms | Cold / warm allocated MiB | Cold / warm checks |
| --- | ---: | ---: | ---: | ---: |
| Small | 18.37 / 19.84 | 6.73 / 7.41 | 0.93 / 0.25 | 1 / 0 |
| 25 sources / 4 configs | 341.41 / 485.19 | 27.96 / 29.55 | 70.36 / 6.09 | 3 / 0 |
| Default libraries | 179.27 / 184.24 | 7.45 / 8.72 | 85.16 / 0.25 | 1 / 0 |

Changed source versions rechecked their components (medians 19.21 / 228.03 / 152.12 ms);
dirty multi-project dependencies took 210.69 ms and three checks. First recovered completion /
signature queries took 19.06 / 14.67 ms with two checks; identical carets took 1.28 / 1.17 ms
with zero checks and identical fingerprints. Changed carets rechecked once, changed versions
twice. Both cursor runs retained eight total entries, four cursor entries, below 246 KB estimated.

The 24-request cancellation burst observed the configured 16 admitted builds/two active slots,
settled all canceled waiters, and restored 16 admission slots/two build slots/zero in-flight work.
It retained eight completed entries. The separate 12-document/24-cursor retention run retained
seven entries/four cursor entries, estimated 60,516 B. Full-GC active-minus-invalidated heap
was 168,784 B there. For the multi-project service sequence it was 3,650,696 B, versus an
8,382,466 B cache estimate. Small/default-library heap deltas were near zero and noisy; they
do not mean their graphs use no memory. Neither these observations nor the 64 MiB estimated
completed-cache budget bounds total process memory, live leases or in-flight builds.

Actual stdio spawn-to-initialize medians were 402.14 / 400.84 / 399.42 ms for small / multi /
default libraries. First seven-request sequences took 443.76 / 768.55 / 894.14 ms after
initialization; warm sequences took 50.27 / 78.12 / 48.89 ms. Warm response payload totals
were 1,662–1,670 / 7,122–7,130 / 1,692–1,700 bytes. IDs account for the small payload variation.
Asynchronous all-mode diagnostics are included in this real process workflow.

## Equivalent baseline and regression investigation

The unchanged pre-existing definition → references → statements/full-diagnostics sequence
was replayed against exact `df4589b7` archived code and the final DLLs, then repeated in fresh
processes. The archived checker has only the documented `CheckModules` entry counter;
current counts use real `Statistics.Checks`. All semantic fingerprints matched.

| Fixture | Baseline → current cold ms | Baseline → current warm ms | Cold allocation change | Cold / warm checks before → after |
| --- | ---: | ---: | ---: | --- |
| Small | 20.43 → 15.10 | 15.10 → 3.23 | −30.1% | 4 → 1 / 3 → 0 |
| Multi-project | 206.00 → 259.38 | 111.73 → 14.30 | +15.3% | 6 → 3 / 5 → 0 |

The multi-project cold regression exceeds 10%: +25.9% (+53.38 ms) in the first pair,
and +36.9% (+69.05 ms; 186.98 → 256.03 ms) in the fresh-process repeat. Allocation churn
reproduced at 56.14 → 64.73 MB (+15.3%). Measured consumer phases locate the added cold cost:
definition 36.24 → 97.93 ms and references 129.50 → 160.19 ms; the repeat showed
31.89 → 91.63 ms and 119.39 → 153.92 ms. Small cold definition also increased
5.14 → 6.81 ms (repeat 5.06 → 8.10 ms), despite an overall cold improvement.

Code inspection attributes these cold paths to editor syntax capture, final member/scope/type
proof capture, immutable publication and disk/config resolution validation. The consumer
measurements do not isolate each capture/freeze phase, so this is an explanation from code,
not a measured phase profile. Fewer whole-component checks offset only part of that work.
The cold tradeoff is retained for the required trustworthy snapshots; this report does not
claim incremental checking. Warm multi-project time improved 87.2% (85.6% in the repeat),
with zero checks and about 91.6% lower allocation churn.

Warm diagnostics alone increased from the old unchecked cache hits (0.004 / 0.013 ms)
to 0.973 / 3.087 ms for small / multi. The repeat was 1.090 / 3.704 ms. The old cache did
not validate current disk/config dependencies; current output does. Direct currentness
measurement was 0.314 / 0.977 ms per document validation and 0.426 / 3.006 ms per workspace
validation. This validation cost is retained rather than publishing stale diagnostics.

Custom CLR references are a separate expensive case: seven unchanged requests with a
16,900,608 B core DLL reference took a median 370.34 ms with zero checks. Replacing that
path with alternating 294,912 B LS / core DLL bytes took median 55.00 ms / p90 551.06 ms;
all seven generations changed, each refreshed sequence rechecked once, and ordinary output
fingerprints stayed equal. The alternating sizes make this replacement distribution bimodal.
Repeated physical metadata capture/content hashing/currentness are included, without a
phase profile attributing all latency to hashing. This is an explicit performance limit and
a follow-up optimization target for users with large custom references.

To replay the exact historical comparison, use the existing archive/counter driver, then
run each prepared runner again for an independent process pair:

```powershell
./benchmarks/editor-analysis/run.ps1 -BaselineCommit df4589b7 -OutputDirectory artifacts/editor-workflow-benchmark/equivalent
dotnet artifacts/editor-workflow-benchmark/equivalent/bin/baseline/Release/net10.0/SharpTS.Tests.dll artifacts/editor-workflow-benchmark/equivalent baseline-repeat.json df4589b7
dotnet artifacts/editor-workflow-benchmark/equivalent/bin/current/Release/net10.0/SharpTS.Tests.dll artifacts/editor-workflow-benchmark/equivalent current-repeat.json CURRENT_SOURCE_BASE
node benchmarks/editor-workflow/summarize.mjs artifacts/editor-workflow-benchmark artifacts/editor-workflow-benchmark/equivalent benchmarks/editor-workflow/results-windows-arm64.json
```

The comparison driver explicitly builds its archived/current libraries before measuring.
For this recorded run the archive was already built, so only the two independent friend
assemblies were rebuilt against its DLLs and the frozen final current DLLs. No production
build, editor process or other test ran during any reported timing sample. These are
single-machine observations, not cross-machine latency gates or confidence intervals.
