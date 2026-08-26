# Public performance snapshots

This directory defines schema v2 of the public SharpTS performance snapshot. A
snapshot is an ordered collection of independent runs, not a table assembled by
joining measurements from different machines:

- `cross-runtime` embeds the existing schema-v1 Node.js/Bun comparison unchanged.
- `compiler-micro` normalizes BenchmarkDotNet compiler-headroom results.
- `gui` normalizes Avalonia headless benchmarks or Native AOT packaging evidence.

Each run retains its own timestamp, source revision, machine identity, toolchain,
and methodology. Consumers may compare variants inside a run. They must not trend
absolute values across unlike environments.

## BenchmarkDotNet source data

Both managed benchmark executables always write BenchmarkDotNet's built-in
`*-report-full-compressed.json` plus a `*-sharpts-metadata.json` companion. The
companion comes directly from BenchmarkDotNet's `Summary` objects and supplies
descriptor fields omitted by its built-in JSON exporter: categories, typed
parameters, and `OperationsPerInvoke`. The snapshot exporter joins the two JSON
sources by their shared summary title and report order, checking the case count,
type, and method at every position. It never treats display-formatted parameter
text as identity and never parses Markdown.

Compiler cases use stable `<family>/<method>?<parameters>` IDs. Their records keep
the original nanosecond samples and unrounded mean/min/max/standard deviation, and
also expose throughput, allocated bytes per operation, and available generation
collection counts per 1,000 operations.

GUI benchmark methods have an explicit stable-ID map. Actual mean/allocation
values are joined to `PerformanceBudgets.json`; both actual and limit remain in
the snapshot, so pass/fail and headroom are mechanical calculations. Direct
Avalonia and compiled-XAML mount baselines are retained without inventing budgets.
An expected method that did not run is emitted as `missing`, never zero. Unknown
GUI methods or budget IDs fail export.

The packaged GUI harness accepts `-PerformanceEvidencePath`. With `-NativeAot`,
it records cold startup, peak working set, executable bytes, and complete shipping
bytes with the applicable release budgets and full run provenance. Startup and
working set are explicitly `missing/notExecutable` on a cross-publish runner.

## Commands

Export one run to a schema-v2 partial snapshot:

```powershell
$reports = @(Get-ChildItem BenchmarkDotNet.Artifacts/results -Filter '*report-full-compressed.json')
$metadata = @(Get-ChildItem BenchmarkDotNet.Artifacts/results -Filter '*sharpts-metadata.json')

./benchmarks/snapshots/export-snapshot.ps1 `
  -CompilerMicro `
  -ReportPath $reports.FullName `
  -MetadataPath $metadata.FullName `
  -OutputFile compiler-micro.json
```

Use `-GuiBenchmark` plus `-BudgetPath` for the headless GUI suite,
`-GuiPackagingEvidence` for packaging evidence, or `-CrossRuntimeSnapshot` for a
schema-v1 cross-runtime snapshot. Compose independently captured partials without
changing their run envelopes:

```powershell
./benchmarks/snapshots/merge-snapshots.ps1 `
  -Path cross-runtime.json, compiler-micro.json, gui.json, native-aot.json `
  -OutputFile public-snapshot.json `
  -RequireAllSuites

./benchmarks/snapshots/validate-snapshot.ps1 public-snapshot.json
```

`benchmarks.yml` performs the cross-runtime, compiler, and GUI measurements on
their appropriate runners and publishes the merged `public-performance-snapshot`
artifact. Native AOT evidence is produced by the desktop/distribution workflows
and can be appended as another GUI run. A reviewed dated snapshot can be published
without rewriting `PerformanceBudgets.json`; the budget file remains the release
contract and the snapshot remains evidence.

Contract tests are deterministic and do not run timed work:

```powershell
./benchmarks/snapshots/test-snapshot.ps1
```

A schema change requires a new schema file/version and explicit consumer opt-in.
