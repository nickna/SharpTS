# Editor syntax capture benchmark

Run from the repository root with `pwsh -File benchmarks/editor-syntax/run.ps1`.
An optional `-PackageCache <directory>` uses an existing offline NuGet cache.
The script archives exact baseline `2a613147` under ignored `artifacts/`, without
editing baseline source, builds separate Release libraries/runners, and rejects
any AST fingerprint mismatch between baseline, current ordinary parsing and
current opt-in editor parsing. The runner is intentionally outside the solution.

Both deterministic fixtures cover generic types, declarations, classes, private
members, nested calls, parameter/default/body views, arrows, template strings,
computed property keys and UTF-16 strings. The second fixture has 25 modules;
this measures parsing their source sequence, without module resolution/checking.
Each sample includes lexing, document/line-index creation, parsing and normal
parser transforms. The script disables tiered compilation in measurement child
processes to avoid background compilation during short batches. After JIT warmup,
31 repeated batches report median/p90 time
and current-thread allocations per sequence. Complete public AST graphs are
fingerprinted outside timing, including tokens, type nodes, scalar values,
flags, child ordering and reference sharing. No properties are excluded.

Retained heap is a noisy full-GC live delta with one final source/AST sequence
alive. `syntaxEstimatedBytes` is the index estimate used for editor cache bounds,
and excludes the existing document, tokens and AST. This microbenchmark does
not estimate end-to-end editor request latency or cursor recovery cost.

The [2026-10-07 report](results-2026-10-07.json) has exact matching AST fingerprints
for all modes on .NET 10.0.12, Windows ARM64. Ordinary parse medians increase 2.5%
and 6.2% against `2a613147`, with about 0.31% more allocations. Enabled editor
capture costs 4.58×/4.93× current ordinary latency and 2.45×/2.44× allocations,
adding 0.190ms/7.663ms for the small/25-module sequences. It retains approximately
50KB/1.15MB more complete document/AST heap (3.19×/2.97× total retained heap).

Inspection identifies provenance collection, reachability, pruning, immutable
copies/sorts and frozen indexes as the enabled-only work. The benchmark does not
profile those phases separately. Additional allocations stay near 762 bytes per
record across both fixtures. These costs occur on cold editor parsing; checked
snapshots reuse the published index. Epic issue #1981 will measure end-to-end
interactive latency and memory rather than infer them from this microbenchmark.
