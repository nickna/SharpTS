# Phase 2B GUI performance and retention record

`SharpTS.Gui.Benchmarks` is the dedicated Avalonia Headless BenchmarkDotNet suite. It records cold construction/initial mount, scalar and repeated updates, keyed insert/move/remove, managed allocations, and input-to-render latency. The baselines are direct Avalonia construction and the small `BaselineView` control shape.

Benchmark timing and allocation output is an engineering record, not a CI threshold. CI gates deterministic renderer operation counts and the 1,000-cycle retention test instead, avoiding machine-load-dependent wall-clock failures.

Run a release baseline with:

```powershell
dotnet run -c Release --project SharpTS.Gui.Benchmarks -- --exporters json markdown
```

Commit the resulting BenchmarkDotNet report when intentionally changing the renderer hot path, together with the machine/runtime metadata printed by BenchmarkDotNet.

## Phase 2B baseline

Captured 2026-08-09 with BenchmarkDotNet 0.14.0 on Windows 11
(10.0.29639.1000), .NET SDK 10.0.302, and .NET 10.0.10 x64 RyuJIT AVX2.
The processor model was not reported by the benchmark host. Values are an
engineering snapshot only and are not pass/fail thresholds.

| Scenario | Mean | Managed allocation |
| --- | ---: | ---: |
| Direct Avalonia initial mount | 126.14 us | 27.02 KB |
| Compiled-XAML shape baseline | 36.98 us | 9.88 KB |
| SharpTS initial mount | 268.85 us | 63.88 KB |
| Scalar update | 350.81 us | 77.77 KB |
| Batched scalar updates | 49.34 us | 26.74 KB |
| Keyed insert/move/remove | 449.22 us | 109.30 KB |
| Input-to-render latency | 396.46 us | 87.77 KB |

This first baseline uses one invocation per iteration to preserve complete
mount/update lifecycles. BenchmarkDotNet warned that the individual iteration
times are below its recommended 100 ms minimum and that several distributions
are multimodal, so compare future runs by trend and retain the raw reports when
investigating a regression.
