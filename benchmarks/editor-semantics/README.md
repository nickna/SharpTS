This optional runner measures the checker and publication cost of editor semantic metadata. It is outside the solution and does not affect ordinary builds. The earlier member-provenance benchmark is unchanged.

Run from the repository root with .NET 10 and PowerShell:

```powershell
./benchmarks/editor-semantics/run.ps1
```

The script archives the exact `1cfcc46a` baseline into ignored `artifacts/editor-semantics-benchmark` without instrumentation. It builds that compiler and the current compiler, then compiles the same runner against each DLL directory. `-PackageCache <path>` uses an existing offline NuGet cache. `-OutputDirectory <path>` changes the scratch directory. `-SkipLibraryBuilds` reuses both previously built compiler directories; the small runners are still rebuilt against those DLLs.

Four variants run on identical source graphs: baseline ordinary checking, current ordinary checking, current member provenance capture, and current editor metadata capture. Editor capture also enables member provenance, so its difference from member-only capture measures the added cold check and publication cost of types, declarations, scopes, receiver sets and invocation candidates/selections. Preparation creates a fresh graph parsed with editor syntax for every check, using pure VFS resolution with default declaration libraries disabled. Parsing, resolution, graph preparation and fingerprinting are outside the timed interval. Timed work constructs one checker, calls `CheckModules` once, copies diagnostics and freezes the requested indexes. No metadata-only semantic recheck is performed.

The small fixture covers source instance/static/private members, accessors, a private call, literal indexing, writes, private presence, generic calls, public overloads and a nested shadowing scope. The 25-module fixture adds imported generic receivers, inherited members and constructors, generic classes, and the same scope and overload work in each module. Each fixture must have zero diagnostics; the runner fails otherwise. All variants use the exact same prepared source texts. Parameter properties remain excluded because their separate rename policy is covered by focused tests.

Each variant has 30 warmups and 31 batched samples with tiered compilation disabled. Every batch starts after a full GC with its fresh graphs alive. Reports include median/p90 check latency, per-thread allocated bytes, frozen record counts, the indexes' approximate retained-byte estimates, and a noisy full-GC live heap delta from an already parsed graph to a retained publication with the mutable checker released. Allocations include transient work and must not be interpreted as retained heap growth. Index estimates omit some runtime object and collection overhead and are not process heap limits.

Editor mode also measures a separate immutable query-read batch against its final checked publication: exact-owner and cursor type lookups, receiver sets, invocation candidates/selections and visible bindings at a nested block. Query plans are prepared outside timing. The read measurement contains no lexing, parsing, resolution, inference or checker calls. It measures these core lookup APIs, without LSP transport, handler rendering, cancellation or snapshot acquisition.

The script rejects changes in diagnostic contents/positions, public structural expression/class types, and lexical declaration/use identity partitions across all four variants. Numeric nominal class IDs are normalized to deterministic per-graph ordinals, including access brands. Zero stays distinct, and the fingerprint preserves nonzero identity equivalence partitions rather than merging by class name or source shape. The inherited fingerprint self-check verifies remapping invariance and rejects merged, locationless and wrongly branded classes. These checks occur outside timing.

Raw reports and a combined `comparison.json` are written to the scratch directory. A single-machine measurement can identify a regression worth investigating, but it does not establish a general speedup or end-to-end editor latency. Final epic validation in #1981 still needs interactive measurements with snapshot reuse.

The recorded Windows ARM64/.NET 10.0.12 run is in [results-2026-10-07.json](results-2026-10-07.json), using SDK 10.0.401. All three fingerprints match across all four variants, with zero diagnostics. Ordinary median checking changed by -9.26%/-0.06% for the small/25-module fixtures; allocated bytes increased by 1.31%/1.15%. Neither ordinary median exceeds the 10% investigation threshold. The small baseline has a wider p90 than the current run, so the negative changes should not be treated as evidence of a speedup.

| Fixture | Baseline ordinary | Current ordinary | Current members | Current editor | Editor over members |
| --- | ---: | ---: | ---: | ---: | ---: |
| Small | 0.3091 ms | 0.2805 ms | 0.4714 ms | 1.1378 ms | +0.6664 ms |
| 25 modules | 88.3204 ms | 88.2708 ms | 104.7844 ms | 149.2828 ms | +44.4984 ms |

Editor capture takes 2.414×/1.425× the member-only cold check time. It adds 504,298/20,393,888 allocated bytes and 69,912/2,830,328 retained heap bytes over member-only capture. Those transient allocations include collecting and freezing scopes, receiver sets, invocation attempts and type/signature presentations. This is an attribution from code inspection rather than measured phase profiling. The retained increase is much smaller than allocation churn.

The small/25-module publications contain 226/9,253 editor facts, including 24/826 scopes and 8/372 selected invocations. The receiver query sites read 28/700 member entries, and the nested scope sites read 13/447 visible bindings. The editor index estimates 40,616/1,638,748 bytes, below the extra measured retained heap over member-only capture. The member index is separate and estimates another 6,318/282,654 bytes. These approximate estimates do not include every runtime collection, object, string and identity-map overhead; cache budgets remain estimates.

| Immutable read batch | Queries | Median / p90 | Allocated bytes per batch |
| --- | ---: | ---: | ---: |
| Small | 21 | 0.00975 / 0.00995 ms | 8,064 |
| 25 modules | 525 | 2.45651 / 2.49565 ms | 234,624 |

These batches combine exact-owner dictionary lookups with cursor occurrence and visible-scope scans and returned lists. Their scaling should not be generalized to every individual query API. Snapshot reuse avoids repeating the cold check; these measurements do not include LSP handlers, transport, rendering or end-to-end interactive behavior.
