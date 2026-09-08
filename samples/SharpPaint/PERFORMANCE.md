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
