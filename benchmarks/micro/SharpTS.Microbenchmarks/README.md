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
| `Program.cs` | BenchmarkDotNet entry point (GitHub-Markdown + HTML exporters, `MemoryDiagnoser`, rank/ops-per-sec columns). |
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

Results (Markdown + HTML, plus allocation columns) are written under
`BenchmarkDotNet.Artifacts/`.

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

## Embedded-resource gotcha

A wrong resource name **compiles fine** but throws at `[GlobalSetup]`
(`GetManifestResourceStream` returns null). After adding a `.ts` source or
renaming anything, verify the manifest names resolve:

```powershell
$asm = [System.Reflection.Assembly]::LoadFrom(
  (Get-ChildItem -Recurse benchmarks/micro/SharpTS.Microbenchmarks/bin -Filter SharpTS.Microbenchmarks.dll)[0].FullName)
$asm.GetManifestResourceNames()
```
