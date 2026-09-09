# Drawing measurements

Run the repeatable raster scenario with:

```powershell
dotnet test tests/gui-conformance/SharpTS.Gui.Conformance.Tests -c Release --filter FullyQualifiedName~DrawingWorkloadsReportRasterCosts --logger "console;verbosity=detailed"
```

The scenario draws a 10,000-point stroke in 2048×2048 logical coordinates. It warms each size once, then averages five rasterizations. A Windows x64 / .NET 10 development run on September 7, 2026 produced:

| Raster size | Mean raster time | Managed allocation per raster | Bytes per BGRA buffer |
| --- | ---: | ---: | ---: |
| 2048×2048 | 16.00 ms | 2,992 | 16,777,216 |
| 512×512 | 1.65 ms | 2,992 | 1,048,576 |
| 40×40 | 0.56 ms | 2,992 | 6,400 |

These are isolated CPU raster measurements, not pointer-to-pixel latency, frame-rate guarantees, or total process/native memory. Timings depend on the machine and concurrent work. The buffer column describes one allocation; rendering, effects, and encoding can require several buffers. Managed allocation counts exclude native Skia storage.

The same test mounts a real logical DrawingCanvas, captures a frame, and verifies that a thumbnail retains a 40×40 bitmap. An empty input surface with an 8192×8192 logical extent retains no bitmap. This makes the memory improvement an asserted contract, independent of benchmark timing.

The workflow tests cover drawing, history, layers and merge, live effects, cancellation, exact zoom, narrow layouts, and a 1.5 display scale in both execution modes. Rendering tests separately cover isolated erasing and international text. Use the workflow screenshots and native inspection together when changing layout.

Large exports and effects still allocate full-resolution buffers. Brush gestures still submit command lists and redraw the changing layer. Before further rendering changes, profile real pen/mouse input, serialization, frame time, peak native memory, and several simultaneous full-resolution layers. Those measurements should determine whether incremental strokes, retained scene/image handles, or tiled rendering are needed. The sample's 8192-pixel format limit is a validation limit, not a performance promise.

## Application workflow baseline

`performance.tests.tsx` drives native input through the actual editor, waits for guest/native rendering, and captures a PNG. After building the current local SDK, run the measurement wrapper in a quiet environment:

```powershell
./samples/SharpPaint/measure-performance.ps1 -Mode compiled
./samples/SharpPaint/measure-performance.ps1 -Mode interpreted
```

The wrapper creates an isolated artifact/storage directory, records JSON and PNG outputs, and samples the host's OS peak working set every 50 ms. It leaves the performance entry built; rebuild with `-p:SharpTSEntryPoint=main.tsx` to restore the application. An existing staged host can be measured with `-RuntimeDirectory <absolute-output-directory>`.

A September 8, 2026 UTC run on an Intel Core i7-14700T (28 logical processors), Windows x64 build 29639, .NET 10.0.11, and Avalonia 12.1.1 produced the following. [Raw samples](performance-results.json) preserve every observation.

| Scenario | Samples after warm-up | Compiled p50 / p95 | Interpreted p50 / p95 |
| --- | ---: | ---: | ---: |
| 100 native points → completed stroke/frame + PNG capture | 10 after 2 | 54.0 / 58.3 ms | 235.3 / 260.8 ms |
| Open blur inspector, replace four parameters → preview/frame + PNG capture | 5 after 1 | 544.6 / 631.4 ms | 1375.4 / 1704.6 ms |
| Export 2048×2048 PNG through the document command | 3 after 1 | 187.0 / 187.2 ms | 1106.9 / 1187.7 ms |

Peak working set across the complete fresh process was **230.4 MiB compiled** and **303.0 MiB interpreted**. This includes managed and native memory, runtime startup, editor state, rendering, previews, and encoding; it is not a measurement of native allocations alone.

All scenarios use one 2048×2048 layer, default window size, and Fit. The stroke run accumulates twelve strokes; preview and export use the resulting layer. Preview timing includes inspector creation, the 180 ms debounce, rapidly replaced parameter requests, rasterization, and capture. Export uses a scripted file dialog; it includes rendering and disk encoding but no user interaction time. Percentiles use nearest rank; with three or five samples, p95 is the observed maximum and is not a robust tail estimate. These are one-machine development observations, not release budgets.

The result supports prioritizing compiled mode for sustained editing and profiling guest dispatch/serialization before redesigning the raster backend. The stroke metric times an entire synthetic 100-point gesture and PNG output, so it does not establish per-point response time or physical pen-to-display latency. Hardware input, several large simultaneous layers, sustained frame pacing, and multi-monitor/IME/accessibility checks remain real-window validation work.
