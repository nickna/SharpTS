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
SharpTS compilation, warmup, and batch calibration. Startup and compilation are
important but are not mixed into the workload metric.

> For the *internal* benchmarks — SharpTS-compiled vs the idiomatic-C#
> performance ceiling, with allocation/GC profiling — see
> [`../micro/SharpTS.Microbenchmarks`](../micro/SharpTS.Microbenchmarks). The two suites are
> complementary and measure different things; see "Why two suites?" below.

## Layout

| Path | Purpose |
|------|---------|
| `run-benchmarks.ps1` | Builds SharpTS (Release), runs every `scripts/*.ts` on all runtimes, writes `results.txt` and `snapshot.json`. |
| `format-results.ps1` | Renders `results.txt` as a Markdown table (used for the CI job summary). |
| `Snapshot.psm1` / `export-snapshot.ps1` | Parse raw diagnostics and deterministically export the public schema-v1 snapshot. |
| `snapshot-v1.schema.json` | Checked-in, versioned JSON Schema for the public snapshot contract. |
| `snapshots/latest.json` | Canonical reviewed snapshot consumed from a pinned SharpTS checkout. |
| `validate-snapshot.ps1` / `test-snapshot.ps1` | Validate a snapshot and exercise exporter/contract failures without running timed work. |
| `scripts/*.ts` | One workload per file (fibonacci, sort, json, regex, async/promises, …). |
| `scripts/lib/bench.ts` | Shared synchronous and asynchronous timing harnesses (auto-batching, warmup, mean/min/stdev). |
| `scripts/lib/algorithms.ts` | Algorithm bodies **shared byte-identical** with the microbenchmark suite (embedded there as a resource). |

The schema-v1 file remains the versioned contract for this one suite. The
[schema-v2 public snapshot](../snapshots/README.md) embeds it as an independent
run beside compiler-micro and GUI evidence, preserving this environment and
methodology rather than flattening unlike measurements together.

## Running

PowerShell 7 or later (`pwsh`) is required for the benchmark PowerShell tools.

```powershell
# Run everything; results land in $TEMP/bench-results/{results.txt,snapshot.json}
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
selected; Bun is skipped if not on `PATH`.

`-RepositoryRoot` is intended for the paired local performance harness. It lets
the current runner compile and execute a frozen source worktree, so the baseline
does not need to be modified when harness features are added later.
`-NodeExecutable` selects a specific Node binary when more than one version is
installed. `-NoSnapshot` retains only `results.txt`; the paired local harness
uses it because a historical baseline may predate the schema-v1 sampling fields.

For repeatable candidate-vs-baseline runs on native Windows and WSL, see
[`../local-perf/README.md`](../local-perf/README.md).

## How timing works

Each workload calls `bench(name, param, fn)` from `scripts/lib/bench.ts`, which:

1. Probes once. A result between 1 ms and the 100 ms warmup cap is confirmed with
   one post-JIT probe so cold compilation cannot misclassify a fast workload. Only
   a confirmed call that consumes the full 100 ms warmup budget is sampled one call
   at a time (honest for slow cases like the tree-walking interpreter on big inputs).
2. Otherwise it warms the JIT, calibrates an inner batch until a sample spans ≥ 1 ms
   (lifting fast cases above timer and call-overhead noise), then samples to a budget.
   This prevents a runtime's cold first-tier JIT from selecting a different sampling
   method than an already-fast optimizing JIT.
3. Emits one line per case, consumed by the PowerShell formatter and exporter:

   ```text
   BENCH:<name>:<param>:<meanMs>:<minMs>:<stdevMs>:<samples>:<inner>:<sampledMs>
   ```

`performance.now()` (sub-microsecond, monotonic) is used everywhere so the
methodology is identical across runtimes. A `guard` accumulator defeats
dead-code elimination in both SharpTS modes and the JS engines.

Each JSON measurement preserves the reported mean, minimum, sample standard
deviation, sample count, calibrated inner-iteration count, sampled duration,
and launch number in milliseconds. Multiple launches are kept as separate
measurements inside one machine-bound run; the exporter never combines results
from different machines. Cases are ordered by stable
`<script-family>/<case-name>?n=<parameter>` IDs. Every case contains explicit
records for interpreter, compiled, Node.js, and Bun; an unselected, unavailable,
or failed runtime is represented as `missing` rather than removing the case.

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
ratios from the canonical-unit measurements. A schema change requires a new
`schemaVersion`, schema file, harness version/methodology identifier, and
consumer opt-in; validators reject unknown versions.

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
