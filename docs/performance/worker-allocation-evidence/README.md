# Worker allocation measurement evidence

These files support the [implementation results](../worker-allocation-scaling.md).
They are diagnostic evidence, not a refresh of the public benchmark snapshot.

| File | Meaning |
| --- | --- |
| `metadata.json` | Baseline revision, candidate/compiler hashes, runtimes, GC profiles, budgets, ordering and limits. |
| `timing-rows.csv` | Every retained BENCH row, including per-launch mean/minimum/stdev, sample count, inner-batch size and sampled duration. |
| `timing-summary.json` | Medians of launch means, median paired changes, improved-pair counts and all per-launch means. |
| `windows-schedule.csv`, `linux-schedule.txt` | Actual baseline/candidate launch ordering. Linux Node controls ran after both SharpTS modes in each launch. |
| `gc-summary.csv` | Fixed 20-job all-thread phase counters; whole-process allocation and peak working set are separate columns. One diagnostic launch per cell. |
| `tracked-gc.csv` | Final direct/four-worker verification using the checked-in startup hook and workload. |
| `probe-final-*.txt` | Direct typed-delegate kernel probes across sizes. Each size has a two-second sampling interval, so operation counts differ between variants; do not compare raw collection counts as equal-work measurements. |
| `adaptive-timings.csv` | Five separately collected candidate-only adaptive-GC launches; not paired against workstation GC. |
| `memory-diagnoser.csv`, `memory-diagnoser.json` | Candidate-only 24-case BDN diagnostic. CSV GC counts are normalized per 1,000 operations; JSON retains actual collection/operation counts and numeric bytes/op. |
| `conformance.csv` | Per-file/mode baseline and candidate Test262 outcomes. All outcomes match. |

`windows` rows are preliminary measurements; `windows-final` rows use the final
compiler. Linux predates only the last boxed-local safety guard, which was
verified to leave the canonical kernel's emitted IL unchanged. The original
allocation and message-latency cases were grouped in source order. Selected
adjacent controls ran as isolated processes. Per-launch timing variance and the
shared-host limitation must be retained when interpreting the results.

Reproduction uses the benchmark sources and controls in the repository, the
[local paired protocol](../../../benchmarks/local-perf/README.md), and the
[all-thread allocation diagnostic](../../../benchmarks/cross-runtime/diagnostics/allocation-gc/README.md).
Do not divide whole-process allocation by the phase job count, or treat the
escaping construction/traversal controls as an additive decomposition of the
private-array full kernel.
