# Checked editor analysis benchmark

Optional console runner for issue #1975. It is outside the solution and ordinary test runs.
The executable uses the `SharpTS.Tests` friend assembly name to read the LSP's internal analysis counters; it is not a test assembly or a distributed tool.

Run from the repository root with .NET 10 and PowerShell:

```powershell
./benchmarks/editor-analysis/run.ps1
# Offline replay using an already populated NuGet cache:
./benchmarks/editor-analysis/run.ps1 -PackageCache C:/Users/you/.nuget/packages
```

The script archives the exact baseline commit `df4589b7` beneath ignored `artifacts/analysis-benchmark`, adds one `Interlocked.Increment` counter at the archived `TypeChecker.CheckModules` entry point, and builds baseline and current Release binaries separately. No baseline service behavior is reconstructed. The runner compiles against each binary set using `LibraryDirectory` and `Baseline=true` for the old API. All fixtures, archived source, intermediate files and raw samples remain ignored. No package dependency is added to the language server.

Each fixture runs the same definition, references (including declarations and workspace expansion), statement retrieval, and full diagnostics sequence. The runner verifies usable results and records a source-relative fingerprint; the script refuses unequal baseline/current fingerprints. The small fixture has one source file; the multi-project fixture has 25 source files, four configs, project references and two consumers with 30 functions per file. Both explicitly set `noLib: true` and `types: []` to isolate source graph work. This measures service queries, excluding stdio transport, editor UI and CLR metadata refresh.

After three process-local prewarm sequences, 11 cache-cold samples use fresh services and 31 warm samples repeat an unchanged capture. “Cold” means an empty analysis cache; JIT, embedded resources and operating-system file caches are already warm. Reports include median/p90 latency, process-wide allocated bytes (including asynchronous workers), actual whole-graph checker calls, feature timing in the order definition/references/diagnostics, and current cache estimates. Document and expanded-workspace currentness validation are measured separately with their observed-input counts and byte estimates.

Retained memory is measured in a separate non-inlined synchronous frame: populate one session, release all temporary query tasks/AST references, force full GC, invalidate its caches without holding a lease, and force GC again. Reports include both absolute live-heap values and their difference. These are process-wide managed heap observations, not a strict peak or private working-set limit. Cache byte estimates are approximate source/token/type costs and do not claim to include every runtime object or CLR metadata generation.

Interpret individual regressions alongside the combined sequence. The old warm diagnostics cache returned without disk/config validation; the new path must read the observed inputs before publication. Cold definition also pays capture, immutable projection, input validation and bounded-cache admission that later features reuse. The acceptance target is shared trustworthy results and zero warm whole-project checks, not zero-cost validation.

The [2026-10-07 compact report](results-2026-10-07.json) records matching query fingerprints, zero warm checks, and reduced combined cold/warm latency for both fixtures. Individual cold definition latency and allocation increase: 4.60→6.73 ms / 182→237 KB for the small fixture and 33.17→44.87 ms / 10.64→13.03 MB for the multi-project fixture. This includes the new completed snapshot capture and immutable projections plus input validation; it is not a measurement of freezing alone. Direct document validation costs 0.55 ms and 2.44 ms respectively in that run. Workspace validation costs 0.70 ms and 6.17 ms. The old warm diagnostics result was nearly free because it reused diagnostics without validating closed inputs; the current result deliberately pays this validation cost. Combined warm allocations fall from 534→117 KB and 40.53→3.32 MB, while retained cache data grows from 2.4→19.1 KB and 81.9 KB→3.42 MB to keep the reusable semantic graphs. Cache invalidation releases the measured data once requests and leases have finished.
