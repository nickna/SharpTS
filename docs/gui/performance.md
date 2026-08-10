# GUI performance and retention

`SharpTS.Gui.Benchmarks` is the Avalonia Headless BenchmarkDotNet suite for cold construction and
initial mount, scalar and batched updates, keyed insert/move/remove operations, managed
allocations, and input-to-render latency. Direct Avalonia construction and the small
`BaselineView` compiled-XAML shape provide comparison baselines.

## Run the benchmarks

Run a Release baseline from the repository root:

```powershell
dotnet run -c Release --project SharpTS.Gui.Benchmarks -- --exporters JSON CSV GitHub
```

Verify the generated CSV against the versioned timing and allocation limits:

```powershell
./SharpTS.Gui.Benchmarks/Verify-PerformanceBudgets.ps1
```

BenchmarkDotNet results are sensitive to hardware, runtime, power state, and machine load. Record
the machine and runtime metadata with any intentionally committed report, compare trends on a
consistent environment, and retain raw reports when investigating a regression.

## Retention verification

Retention uses a deterministic conformance test rather than a wall-clock benchmark. The test
mounts, updates, and disposes 1,000 roots, then verifies that roots, native controls, callback
targets, refs, and subscriptions are released:

```powershell
dotnet test SharpTS.Gui.Conformance.Tests/SharpTS.Gui.Conformance.Tests.csproj -c Release `
  --filter "FullyQualifiedName~ThousandMountUpdateUnmountCyclesReleaseRootsControlsCallbacksRefsAndSubscriptions"
```

Renderer tests also assert native operation counts for updates so unnecessary control-tree work can
fail deterministically without depending on shared-runner timing.

## Release budgets

`SharpTS.Gui.Benchmarks/PerformanceBudgets.json` is the release budget contract. It limits mean
time and managed allocation for initial mount, scalar and batched updates, keyed reconciliation,
and input-to-render latency. The keyed timing limit corresponds to a minimum throughput of about
1,428 complete insert/move/remove cycles per second. The verifier reads BenchmarkDotNet CSV output
without depending on locale-specific Markdown formatting.

The same contract limits a Windows Native AOT executable to 50 MiB and its complete shipping
directory to 65 MiB. `Run-PackagedConsumer.ps1 -NativeAot` always enforces those deterministic
artifact limits. `-EnforcePerformanceBudgets` additionally enforces a 1.5-second cold Headless
startup and a 256 MiB peak working-set ceiling in the Windows preview and distribution workflows.
The harness records both process measurements even when enforcement is disabled.

Cross-publishing `win-arm64` does not satisfy the ARM64 performance gate. Final Native AOT linking,
execution, and measurement require the Visual C++ ARM64 linker workload and native ARM64 Windows
hardware.

## Evidence snapshot: 2026-08-09

The following values are dated engineering evidence, not current guarantees. They were captured
with BenchmarkDotNet 0.14.0 on Windows 11 `10.0.29639.1000`, .NET SDK 10.0.302, and .NET 10.0.10
x64 RyuJIT AVX2. The benchmark host did not report the processor model.

| Scenario | Mean | Managed allocation |
| --- | ---: | ---: |
| Direct Avalonia initial mount | 126.14 us | 27.02 KB |
| Compiled-XAML shape baseline | 36.98 us | 9.88 KB |
| SharpTS initial mount | 268.85 us | 63.88 KB |
| Scalar update | 350.81 us | 77.77 KB |
| Batched scalar updates | 49.34 us | 26.74 KB |
| Keyed insert/move/remove | 449.22 us | 109.30 KB |
| Input-to-render latency | 396.46 us | 87.77 KB |

This run used one invocation per iteration to preserve complete mount and update lifecycles.
BenchmarkDotNet warned that individual iteration times were below its recommended 100 ms minimum
and that several distributions were multimodal.

The same x64 candidate produced a 37,569,024-byte Native AOT executable and a 56,408,368-byte
shipping directory. These numbers establish the evidence for that candidate only; the checked-in
budget file remains the source of truth for release limits.
