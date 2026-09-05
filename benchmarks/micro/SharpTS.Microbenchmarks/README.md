# Microbenchmarks (`benchmarks/micro/SharpTS.Microbenchmarks/`)

**Internal / headroom benchmarks.** This [BenchmarkDotNet](https://benchmarkdotnet.org/)
suite answers a different question from the cross-runtime suite: *how close is
SharpTS-compiled TypeScript to the performance ceiling, and where does the
overhead go?* It is the harness behind the compiler perf work (object-shape
structs, typed-array fast paths, the merge-sort `Array.sort`, etc.).

Each workload is measured three ways:

- **SharpTS** — TypeScript compiled to IL by `ILCompiler`, invoked in-process.
- **Idiomatic** — hand-written C# using native types. The **performance ceiling**.
- **Equivalent** — C# written with `object?`/boxing to approximate the
  **dynamic-typing tax** SharpTS pays for JS semantics.

It is part of `SharpTS.sln` and runs **in-process**: the TypeScript is compiled
once to a DLL at `[GlobalSetup]`, then reached through a cached strongly-typed
`Func<double,double>` delegate so reflection and argument boxing stay **out of
the timed region**.

> For the *external* comparison against Node.js and Bun, see
> [`../../cross-runtime`](../../cross-runtime). That suite has a table explaining why the two
> are kept separate ("Why two benchmark suites?").

## Layout

| Path | Purpose |
|------|---------|
| `Program.cs` | BenchmarkDotNet entry point (JSON + GitHub-Markdown + HTML exporters, `MemoryDiagnoser`, rank/ops-per-sec columns). |
| `Benchmarks/*.cs` | One file per workload family (computational, async/Promise, starter workloads, arrays, Map/Set, property access, object literals, regex). |
| `Baselines/*.cs` | Native-type C# ceilings plus `object?`/boxing controls for the dynamic-typing tax. |
| `Infrastructure/BenchmarkHarness.cs` | Compile TS → DLL, load it, resolve compiled methods/delegates. |
| `Infrastructure/CompilationCache.cs` | Compile each TS source once, reuse across benchmark classes. |
| `TypeScriptSources/*.ts` | TS bodies for the non-computational workloads (embedded as resources). |

The computational/starter workloads load their TS from
`../../cross-runtime/scripts/lib/algorithms.ts`, embedded as the resource
`SharpTS.Microbenchmarks.algorithms.ts`. That file is **shared byte-identical**
with the cross-runtime shell harness, so both suites measure the same source.

The `JsonImportedModule*` benchmarks compile a virtual three-module graph via
`CompileModules`: the shared algorithms module, a callback boundary, and a
driver whose closures capture `n`. This reproduces the imported `json.ts` call
shape while leaving BenchmarkDotNet in control of the timing loop. The phase
class measures cumulative build/stringify/parse/traversal work at `n=1,000`;
the imported interpreter counterpart measures the same module/callback path.
Both faithful phase classes cover `n=1,000` and `n=10,000`, while the compiled
round-trip class provides a shorter hard-gate allocation/GC run at both sizes.
The original direct-delegate JSON benchmarks remain as a comparison.

## Running

Allocation diagnostics share the unmodified cross-runtime `allocation-kernel.ts`.
`AllocationKernelBenchmarks` measures the complete kernel, graph construction,
and traversal of a graph built during setup, with allocation and GC counters.
Each runs with interface and equivalent type-alias declarations. Phase timings
are diagnostic and should not be added together: exposing the graph changes
escape analysis and traversal starts with an already retained graph.

```powershell
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --allocation-smoke
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --filter '*AllocationKernelBenchmarks*'
```

```bash
# From the repo root. BenchmarkDotNet requires a Release build.
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks

# Interactive picker, or filter to a subset:
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --filter '*Fibonacci*'
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --list flat
```

CI uses `--smoke` to compile every embedded TypeScript source and then perform
the same flat benchmark discovery without running any timed benchmark:

```bash
dotnet run -c Release --project benchmarks/micro/SharpTS.Microbenchmarks -- --smoke
```

Results (structured JSON, Markdown, HTML, and allocation columns) are written
under `BenchmarkDotNet.Artifacts/`. A `*-sharpts-metadata.json` companion records
typed parameters, categories, and operations-per-invoke directly from
BenchmarkDotNet descriptors. See the [public snapshot exporter](../../snapshots/README.md)
for normalized compiler-headroom publication.

`ArrayQueueBenchmarks` retains the preallocated C# `List<double>` baselines and
also measures the runtime's `Deque<double>` as an algorithmic ceiling. The list
baselines repeatedly copy elements at the front, so matching those baselines
does not establish efficient queue behavior. Deque baselines grow from empty,
matching the TypeScript initialization. Compare allocation columns as well as
time: counted push loops can reserve storage, while repeated unshift grows it.

Compiled non-escaping queues use two typed stacks with constant-time indexed
reads and amortized constant-time front operations. Ordinary arrays keep list
promotion. Queues with admitted bounded literal writes use nullable slots to
preserve holes; other write shapes and higher-order methods retain existing
lowering. The interpreter uses its circular buffer only when conservative shape
guards permit bypassing per-index property operations.

## Conventions

- **One algorithm per class**, each with a single `[Params]` axis — a single
  class with multiple independent `[Params]` would run BenchmarkDotNet's full
  Cartesian product and waste ~Nx of the work.
- Compiled functions are reached via `ComputationalBenchmarkBase.LoadCompiled`,
  which returns a cached `Func<double,double>` — keep reflection/boxing outside
  `[Benchmark]` methods so the measurement reflects the generated IL, not the
  invocation plumbing.
- Embedded-resource names are referenced **by string** (e.g.
  `"SharpTS.Microbenchmarks.TypeScriptSources.Regex.ts"`). `RootNamespace`/
  `AssemblyName` are pinned in the `.csproj` so those names stay stable; if you
  rename the project, keep the strings and the pinned names in sync.

`NumericRestBenchmarks.restSelectedTarget` alternates between two callees on each
iteration, complementing `restDynamicDispatch`, which receives one unknown target
through a parameter. Both retain runtime dispatch and validate their checksums.

## Embedded-resource gotcha

A wrong resource name **compiles fine** but throws at `[GlobalSetup]`
(`GetManifestResourceStream` returns null). After adding a `.ts` source or
renaming anything, verify the manifest names resolve:

```powershell
$asm = [System.Reflection.Assembly]::LoadFrom(
  (Get-ChildItem -Recurse benchmarks/micro/SharpTS.Microbenchmarks/bin -Filter SharpTS.Microbenchmarks.dll)[0].FullName)
$asm.GetManifestResourceNames()
```
