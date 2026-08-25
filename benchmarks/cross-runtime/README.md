# Cross-Runtime Benchmarks (`benchmarks/cross-runtime/`)

**External / competitive benchmarks.** This suite answers one question: *how does
SharpTS compare to other JavaScript/TypeScript runtimes?* The bar is to **meet or
exceed Node.js**.

It runs the same TypeScript workloads across four runtimes and prints a
side-by-side table:

- SharpTS **interpreter** (`dotnet run -- script.ts`)
- SharpTS **compiled** (`dotnet run -- --compile script.ts` → run the DLL)
- **Node.js** (with `--experimental-strip-types` on Node < 23)
- **Bun** (if installed)

Measurement is **whole-program, process-level** (includes startup + the full
pipeline), so it reflects what a user actually experiences invoking each runtime.

> For the *internal* benchmarks — SharpTS-compiled vs the idiomatic-C#
> performance ceiling, with allocation/GC profiling — see
> [`../micro/SharpTS.Microbenchmarks`](../micro/SharpTS.Microbenchmarks). The two suites are
> complementary and measure different things; see "Why two suites?" below.

## Layout

| Path | Purpose |
|------|---------|
| `run-benchmarks.ps1` | Builds SharpTS (Release), runs every `scripts/*.ts` on all runtimes, writes `results.txt`. |
| `format-results.ps1` | Renders `results.txt` as a Markdown table (used for the CI job summary). |
| `scripts/*.ts` | One workload per file (fibonacci, sort, json, regex, async/promises, …). |
| `scripts/lib/bench.ts` | Shared synchronous and asynchronous timing harnesses (auto-batching, warmup, mean/min/stdev). |
| `scripts/lib/algorithms.ts` | Algorithm bodies **shared byte-identical** with the microbenchmark suite (embedded there as a resource). |

## Running

```powershell
# Run everything; results land in $TEMP/bench-results/results.txt
./benchmarks/cross-runtime/run-benchmarks.ps1

# Repeat only numeric-loop workloads, excluding the advisory Bun result
./benchmarks/cross-runtime/run-benchmarks.ps1 `
  -Workloads int-arrays,brainfuck,accumulate `
  -Runtimes compiled,node `
  -Launches 3 `
  -OutputDirectory .perf-cross-runtime

# Render the table from a results file
./benchmarks/cross-runtime/format-results.ps1 -ResultsFile $env:TEMP/bench-results/results.txt
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
installed.

For repeatable candidate-vs-baseline runs on native Windows and WSL, see
[`../local-perf/README.md`](../local-perf/README.md).

## How timing works

Each workload calls `bench(name, param, fn)` from `scripts/lib/bench.ts`, which:

1. Probes once; if a single call is already ≥ 1 ms it samples one call at a time
   (honest for slow cases like the tree-walking interpreter on big inputs).
2. Otherwise warms the JIT, calibrates an inner batch until a sample spans ≥ 1 ms
   (lifting fast cases above the timer noise floor), then samples to a budget.
3. Emits one line per case, consumed by `format-results.ps1`:

   ```
   BENCH:<name>:<param>:<meanMs>:<minMs>:<stdevMs>
   ```

`performance.now()` (sub-microsecond, monotonic) is used everywhere so the
methodology is identical across runtimes. A `guard` accumulator defeats
dead-code elimination in both SharpTS modes and the JS engines.

If a runtime produces no `BENCH:` line (crash, parse error, missing API),
`run-benchmarks.ps1` warns loudly and echoes the tail of its output rather than
silently leaving a blank cell.

## CI

`.github/workflows/benchmarks.yml` runs this suite on `workflow_dispatch` and
publishes the formatted table to the job summary, with the raw `results.txt`
uploaded as an artifact. It is **not** run on every push (timing on shared CI
runners is noisy and the full sweep is slow).

## Why two benchmark suites?

| | `benchmarks/cross-runtime/` (this suite) | `benchmarks/micro/SharpTS.Microbenchmarks/` |
|---|---|---|
| **Question** | Are we as fast as Node/Bun? | How close are we to the C# ceiling, and where's the overhead? |
| **Compares against** | Node.js, Bun | Idiomatic C# (native types) + "equivalent" C# (`object?`/boxing) |
| **Tool** | PowerShell + shared `bench.ts` | BenchmarkDotNet |
| **Scope** | Whole-program, process-level | In-process, per-function (delegate-invoked) |
| **Profiling** | Wall-clock mean/min/stdev | + allocations/GC (`MemoryDiagnoser`) |

They can't be merged: BenchmarkDotNet must run in-process against managed code
(it can't drive the `node`/`bun` executables), and the cross-runtime comparison
must be black-box at the process boundary. Keeping them separate is intentional;
the shared `scripts/lib/algorithms.ts` ensures both measure identical source.
