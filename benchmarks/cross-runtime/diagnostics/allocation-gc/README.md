# Process-wide worker allocation diagnostic

This diagnostic runs the unchanged 20,000-record kernel, warms up for at least
one second, and measures exactly 20 validated jobs. `PROFILE_WORKERS=0` runs
directly; `1`, `2`, and `4` use a persistent worker pool. Run one process at a time.

The .NET startup hook records allocation, collections and pause duration across
**all process threads** between `GC_BEGIN` and `GC_END`. Parent-thread allocation
counters alone cannot measure worker allocation. Marker bookkeeping introduces
a small fixed overhead, so treat bytes/job as approximate. The hook is diagnostic
only and is never enabled by the regular benchmark runner.

From the repository root, after building SharpTS in Release:

```powershell
$diagnostic = 'benchmarks/cross-runtime/diagnostics/allocation-gc'
dotnet build "$diagnostic/AllocationGcHook.csproj" -c Release
New-Item -ItemType Directory -Force .perf-allocation-gc | Out-Null
dotnet src/SharpTS/bin/Release/net10.0/SharpTS.dll --compile `
  "$diagnostic/worker-allocation-gc.ts" -o .perf-allocation-gc/workload.dll
$previousHook = $env:DOTNET_STARTUP_HOOKS
$previousWorkers = $env:PROFILE_WORKERS
try {
  $env:DOTNET_STARTUP_HOOKS = (Resolve-Path "$diagnostic/bin/Release/net10.0/AllocationGcHook.dll").Path
  foreach ($workers in 0, 1, 2, 4) {
    $env:PROFILE_WORKERS = "$workers"
    dotnet .perf-allocation-gc/workload.dll
  }
} finally {
  $env:DOTNET_STARTUP_HOOKS = $previousHook
  $env:PROFILE_WORKERS = $previousWorkers
}
```

`GC_PHASE` is the steady-state interval and provides normalized `bytesPerJob`.
`GC_METRICS` covers the whole process, including compilation, startup and warmup;
its allocation count must **not** be divided by 20. `peakWorkingSetBytes` is the
whole-process peak, not a per-job or phase-only reading. Collection counts and
pause duration are diagnostic observations, not stable timing guarantees.

Compare the same source with frozen baseline/candidate compiler closures. Keep
the application GC profile explicit; the test project's server GC setting does
not describe a default compiled application. The adaptive profile can be emitted
separately with `--gc-profile adaptive`. Leave startup hooks unset for latency
measurements.
