# Cross-Runtime Benchmarks (`benchmarks/cross-runtime/`)

**External / competitive benchmarks.** This suite answers one question: *how does
SharpTS compare to other JavaScript/TypeScript runtimes?* The bar is to **meet or
exceed Node.js**.

It runs the same TypeScript workloads across four runtimes and produces a
side-by-side table plus a versioned JSON snapshot:

- SharpTS **interpreter** (`dotnet run -- script.ts`)
- SharpTS **compiled** (`dotnet run -- --compile script.ts` → run the DLL)
- **Node.js** (with `--experimental-strip-types` on Node < 23)
- **Bun** (if installed)

Published measurements are **in-process workload timings**. They include one
invocation of the workload function and exclude process startup, script loading,
SharpTS compilation, warmup, batch calibration, and optional correctness
validation. Startup and compilation are important but are not mixed into the
workload metric.

> For the *internal* benchmarks — SharpTS-compiled vs the idiomatic-C#
> performance ceiling, with allocation/GC profiling — see
> [`../micro/SharpTS.Microbenchmarks`](../micro/SharpTS.Microbenchmarks). The two suites are
> complementary and measure different things; see "Why two suites?" below.

## Layout

| Path | Purpose |
|------|---------|
| `run-benchmarks.ps1` | Builds SharpTS (Release), runs every `scripts/*.ts` on all runtimes, writes `results.txt` and `snapshot.json`. |
| `format-results.ps1` | Renders the median of retained per-launch means as a Markdown table (used for the CI job summary). |
| `Snapshot.psm1` / `export-snapshot.ps1` | Parse raw diagnostics and deterministically export the public schema-v1 snapshot. |
| `snapshot-v1.schema.json` | Checked-in, versioned JSON Schema for the public snapshot contract. |
| `snapshots/latest.json` | Canonical reviewed snapshot consumed from a pinned SharpTS checkout. |
| `validate-snapshot.ps1` / `test-snapshot.ps1` | Validate a snapshot and exercise exporter/contract failures without running timed work. |
| `scripts/*.ts` | One workload per file (fibonacci, sort, json, regex, async/promises, …). |
| `scripts/lib/bench.ts` | Shared synchronous and asynchronous timing harnesses (auto-batching, warmup, mean/min/stdev). |
| `scripts/lib/algorithms.ts` | Algorithm bodies **shared byte-identical** with the microbenchmark suite (embedded there as a resource). |
| `scripts/worker-scaling.ts` | Fixed-total-work CPU comparison using direct execution and persistent pools of 1, 2, 4, 8, and 16 workers. |
| `scripts/worker-message-latency.ts` | Persistent-pool fan-out/fan-in message round trips with 1, 2, and 4 workers and negligible worker-side computation. |
| `scripts/worker-message-throughput.ts` | Persistent-worker bursts of 1, 100, and 1,000 small messages, exposing queue-drain and callback-scheduling throughput. |
| `scripts/worker-message-port-throughput.ts` | Bidirectional bursts over a `MessagePort` transferred to a persistent worker, isolating port bridge and callback scheduling throughput. |
| `scripts/worker-lifecycle.ts` | Repeated create, ready, and terminate cycles for groups of 1, 2, and 4 workers. |
| `scripts/worker-allocation-scaling.ts` | Fixed-total-work allocation-heavy comparison using direct execution and persistent pools of 1, 2, and 4 workers. |
| `scripts/worker-atomics-scaling.ts` | Fixed-total-work `Atomics.add` comparison over shared counters, separating padded disjoint updates from same-location contention with 1, 2, and 4 workers. |
| `scripts/workers/*.ts` | Worker entry points and worker-specific shared kernels; these are dependencies, not standalone workloads. |

The schema-v1 file remains the versioned contract for this one suite. The
[schema-v2 public snapshot](../snapshots/README.md) embeds it as an independent
run beside compiler-micro and GUI evidence, preserving this environment and
methodology rather than flattening unlike measurements together.

`array-queue.ts` measures full shift/drain and unshift/build workloads at 1,000,
2,500, 5,000, and 10,000 elements, plus a fixed-width alternating push/shift queue.
Every case validates its checksum outside the timed region. The intermediate
sizes expose scaling changes; the alternating case exercises reuse across many
queue turnovers. Keep these operations intact when comparing implementations.

## Running

PowerShell 7 or later (`pwsh`) is required for the benchmark PowerShell tools.

```powershell
# Run everything; results land in $TEMP/bench-results/{results.txt,snapshot.json}.
# Three launches per runtime/case are collected by default.
./benchmarks/cross-runtime/run-benchmarks.ps1

# Repeat only numeric-loop workloads, excluding the advisory Bun result
./benchmarks/cross-runtime/run-benchmarks.ps1 `
  -Workloads int-arrays,brainfuck,accumulate `
  -Runtimes compiled,node `
  -Launches 3 `
  -OutputDirectory .perf-cross-runtime

# Render the table from a results file
./benchmarks/cross-runtime/format-results.ps1 -ResultsFile $env:TEMP/bench-results/results.txt

# Validate a generated or checked-in public snapshot
./benchmarks/cross-runtime/validate-snapshot.ps1 $env:TEMP/bench-results/snapshot.json
```

To discover and compile every workload without running the timed loops (the
same inexpensive guard used by CI):

```powershell
./benchmarks/cross-runtime/run-benchmarks.ps1 -Smoke
```

`-Workloads` accepts script basenames and `-Runtimes` accepts `interpreter`,
`compiled`, `node`, and `bun`. Override the output directory with
`-OutputDirectory` or `$env:OUTPUT_DIR`. Node and Bun are detected only when
selected; Bun is skipped if not on `PATH`. `-Launches` defaults to 3; use an
explicit lower value only for quick local diagnostics, not published evidence.

The `exceptions` workload runs each registered case in a fresh process by
default so tiering, exception counters, and earlier cases cannot contaminate a
later result. `-IsolatedWorkloads` accepts a comma-separated set of additional
workload basenames (or an empty array to disable isolation).

`-RepositoryRoot` is intended for the paired local performance harness. It lets
the current runner compile and execute a frozen source worktree, so the baseline
does not need to be modified when harness features are added later.
`-NodeExecutable` selects a specific Node binary when more than one version is
installed. `-NoSnapshot` retains only `results.txt`; the paired local harness
uses it because a historical baseline may predate the schema-v1 sampling fields.

For repeatable candidate-vs-baseline runs on native Windows and WSL, see
[`../local-perf/README.md`](../local-perf/README.md).

## How timing works

Set `SHARPTS_BENCH_WARMUP_MS=1500` for focused steady-state investigations.
The optional override accepts integer milliseconds from 0 to 10000; the default
remains 100 ms. It changes warmup only, not the sampling budget or slow-call
sampling threshold. Zero skips timed warmup; correctness checks, the discarded
cold and routing probes, and batch calibration still run. Use the same setting
and workload sources for every runtime and baseline/candidate build, and retain
the setting alongside raw results.

`object-destructure-materialized` exercises dictionary storage from construction.
`object-destructure-carrier-materialized` exercises a compact record that is
subsequently materialized. `object-destructure-materialized-controls` adds direct,
manually hoisted, fractional, varying-receiver, and per-iteration mutation controls.

The shared algorithm drivers supply independent expected checksums to catch
miscompilations before and after sampling. Factorial uses finite inputs
10, 20, and 100; `arithmetic-loop` provides a bounded accumulator at larger
iteration counts. Its body is also shared with the microbenchmark suite.

`callback-control` reports synchronous and asynchronous empty-callback costs.
Treat these as diagnostic floors, not quantities to subtract from other cases:
inlining and tiering depend on the callback body. `string-scaling` extends the
unchanged shared string workload through one million iterations. For compiler
scaling investigations, select `-Runtimes compiled,node`; the largest inputs
are intentionally expensive in the interpreter.

```powershell
./benchmarks/cross-runtime/run-benchmarks.ps1 `
  -Workloads strings,string-scaling,json,arithmetic-loop,callback-control `
  -Runtimes compiled,node -Launches 3 -OutputDirectory .perf-algorithms
```

Use paired baseline/candidate measurements for performance acceptance. Ordinary
CI should verify checksums, compilation, and generated-code regression checks;
wall-clock ratios belong in the local performance workflow, not noisy CI gates.

Each workload calls `bench(name, param, fn, expected?)` from
`scripts/lib/bench.ts`, which:

1. When `expected` is supplied, validates one invocation before probing. A
   second validation after sampling checks the optimized steady-state result;
   neither validation is timed.
2. Discards a cold probe, gives every runtime the configured time-bounded
   warmup (100 ms by default), and discards a post-warmup routing probe. Slow and fast cases therefore
   both report steady-state samples rather than using different cold-start rules.
3. A post-warmup call at or above 100 ms is sampled one call at a time. Faster
   calls are auto-batched until a sample spans ≥ 1 ms, lifting them above timer
   and call-overhead noise, and are then sampled to a shared time budget.
4. Emits one line per case with seven decimal places in milliseconds (0.1 ns),
   consumed by the PowerShell formatter and exporter:

   ```text
   BENCH:<name>:<param>:<meanMs>:<minMs>:<stdevMs>:<samples>:<inner>:<sampledMs>
   ```

`performance.now()` (sub-microsecond, monotonic) is used everywhere so the
methodology is identical across runtimes. A `guard` accumulator defeats
dead-code elimination in both SharpTS modes and the JS engines.

`language-hot-paths.ts` keeps `flattened-rest-control` grouped as
`sum + (i + 1 + 2 + 3)`, which exactly matches the evaluation tree of
`sum + add4(i, 1, 2, 3)` without its call/rest mechanics. The former ungrouped
body remains as `left-associated-accumulation`, an intentionally different
loop-carried dependency-chain probe. Direct fixed-arity rest specialization is
reported beside immutable-alias and constant-index specialization targets,
spread calls, and unknown-target/varying-index fallback controls. The
`selected-numeric-rest` control alternates the callee on each iteration to
exercise changing runtime targets alongside the parameter-bound unknown target.
These rest cases share a fractional accumulator seed, keeping them in
Number/double representation from the first iteration instead of letting an
optimizing JavaScript engine start with tagged-small-integer arithmetic and
deopt only at larger parameters. Other integer-oriented probes intentionally
retain their natural representation behavior.

`object-spread.ts` validates checksums for stable single-source and overwrite
spreads, plus a mutation case passed to an `any` consumer. Controls compare a
direct literal passed to the same consumer, the spread with its mutations
inlined, and results retained in an array before consumption. The retained case
also measures array allocation and traversal. The historical `escape` case name
denotes a function boundary, not required retention: SharpTS can now specialize
stable, bounded numeric-only consumers and preserve local object promotion.
Truly retained results still materialize, while independent spread sources can
remain in typed storage. Use the inline and retained controls to distinguish
these cases, and the internal `ObjectLiteralsBenchmarks` spread cases for
allocation/GC evidence.

The `worker-scaling` workload uses the same parent, worker, and CPU-kernel
TypeScript verbatim in every runtime. It keeps the total amount of CPU work
fixed while varying the persistent pool size from 1 to 16 workers. Pool startup,
worker readiness, shutdown, and a deterministic checksum validation occur
outside the timed region, so the reported cases measure steady-state dispatch,
messaging, scheduling, and parallel execution rather than startup or compilation.
The direct case is the serial in-process compute baseline; compare each runtime's
2- and 4-worker times with its 1-worker time to calculate parallel speedup and
efficiency.

The `num-arrays` workload separates indexed numeric-array cost into three cases:
`num-write` grows an escaped array and then checksums it, `num-overwrite`
prepopulates an array outside the timed region and measures overwrite plus
checksum, and `num-read` measures the checksum pass alone. This makes allocation
and capacity growth distinguishable from element-store and element-load cost.

`worker-allocation-scaling` applies the same fixed-total-work design to short-lived object,
string, and array graphs. It exposes allocation throughput, shared-runtime GC interference, and
whether adding workers continues to help once managed-heap traffic replaces an allocation-free
numeric kernel.

`worker-atomics-scaling` keeps a `SharedArrayBuffer` and persistent workers alive while timing a
fixed total of `Atomics.add` operations. Padded disjoint counters measure the shared-memory and
atomic-intrinsic path without cache-line contention; the contended case makes every worker update
the same counter. Both cases validate the exact final count on every invocation, so a faster result
cannot hide lost updates.

The `worker-message-latency` workload isolates the other side of worker performance: one small
structured message is sent to every worker in a persistent pool and the timed invocation completes
after every reply arrives. Worker creation, readiness, validation, and shutdown remain outside the
timed region. Comparing its 1-, 2-, and 4-worker cases exposes dispatch, structured-clone,
cross-thread wake-up, event-loop drain, and promise-settlement overhead without enough worker-side
CPU work to hide that latency.

The `worker-message-throughput` workload complements latency by allowing many worker-to-parent
messages to be outstanding at once. Its timed region includes posting the burst request, cloning
every reply, cross-thread scheduling, draining the parent queue, and settling the completion
promise. Worker creation, readiness, validation, and shutdown remain outside the timed region.

`worker-message-port-throughput` transfers one side of a `MessageChannel` to a persistent worker
and times bursts in both directions. This covers the dedicated port queue, the compiled/interpreter
port bridge, structured cloning, event-loop wake coalescing, and listener dispatch independently
from the Worker's built-in parent channel.

Unlike the steady-state worker workloads, `worker-lifecycle` deliberately times construction,
worker bootstrap, the ready message, cooperative shutdown, and termination-promise settlement.
Repeated invocations use the same script so runtime artifact/code-cache behavior is represented.

Each JSON measurement preserves the reported mean, minimum, sample standard
deviation, sample count, calibrated inner-iteration count, sampled duration,
and launch number in milliseconds. Multiple launches are kept as separate
measurements inside one machine-bound run; the exporter never combines results
from different machines. Cases are ordered by stable
`<script-family>/<case-name>?n=<parameter>` IDs. Every case contains explicit
records for interpreter, compiled, Node.js, and Bun; an unselected, unavailable,
or failed runtime is represented as `missing` rather than removing the case.
The Markdown formatter reports the median per-launch mean and median
within-launch standard deviation, while leaving every launch intact in the raw
and JSON artifacts.

## Refreshing the public snapshot

An intentional refresh should use a clean checkout and the pinned tool versions
from `global.json`, `.node-version`, and `.bun-version`. Use multiple launches so
reviewers can see run-to-run variation:

```powershell
./benchmarks/cross-runtime/run-benchmarks.ps1 `
  -Launches 3 `
  -OutputDirectory .perf-cross-runtime-refresh

./benchmarks/cross-runtime/validate-snapshot.ps1 `
  .perf-cross-runtime-refresh/snapshot.json

Copy-Item .perf-cross-runtime-refresh/snapshot.json `
  benchmarks/cross-runtime/snapshots/latest.json
```

Review the revision (including the `dirty` flag), environment/tool identity,
case set, missing-runtime reasons, and per-launch variance along with the JSON
diff. Do not hand-edit presentation values: consumers choose rounding and
ratios from the canonical-unit measurements. A structural snapshot-contract
change requires a new `schemaVersion` and schema file; a compatible timing or
validation-method change requires a new harness version/methodology identifier.
Validators reject unknown versions.

If a runtime produces no `BENCH:` line (crash, parse error, missing API),
`run-benchmarks.ps1` warns loudly and echoes the tail of its output rather than
silently leaving a blank cell.

## CI

`.github/workflows/benchmarks.yml` runs this suite on `workflow_dispatch`,
validates the generated and canonical snapshots, publishes the formatted table
to the job summary, and uploads both `results.txt` and `snapshot.json` as
diagnostic artifacts. It does not rewrite `snapshots/latest.json`; publication
is an intentional reviewed source change. Ordinary CI runs the deterministic
contract tests and validates the checked-in snapshot, but does not execute the
timed sweep (shared-runner timing is noisy and the full sweep is slow).

## Why two benchmark suites?

| | `benchmarks/cross-runtime/` (this suite) | `benchmarks/micro/SharpTS.Microbenchmarks/` |
|---|---|---|
| **Question** | Are we as fast as Node/Bun? | How close are we to the C# ceiling, and where's the overhead? |
| **Compares against** | Node.js, Bun | Idiomatic C# (native types) + "equivalent" C# (`object?`/boxing) |
| **Tool** | PowerShell + shared `bench.ts` | BenchmarkDotNet |
| **Scope** | In-process workload function | In-process, per-function (delegate-invoked) |
| **Profiling** | Wall-clock mean/min/stdev | + allocations/GC (`MemoryDiagnoser`) |

They can't be merged: BenchmarkDotNet must run in-process against managed code
(it can't drive the `node`/`bun` executables). The cross-runtime runner instead
launches each runtime as a black-box process, while the published metric is measured
inside that process around the workload function. Keeping the suites separate is
intentional; the shared `scripts/lib/algorithms.ts` ensures both measure identical source.
