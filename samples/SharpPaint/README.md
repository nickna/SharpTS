# SharpPaint

SharpPaint is a Paint.NET-inspired desktop editor built in TypeScript and TSX with
`SharpTS.Gui.Sdk`. It is deliberately ambitious: the sample demonstrates retained native layout,
pointer capture, composited drawing, layers, history, filesystem access, native dialogs, PNG
export, drag/drop, keyboard commands, and interpreted/compiled guest parity.

## Run it

After `SharpTS.Gui.Sdk` is available from a configured feed:

```powershell
dotnet run --project samples/SharpPaint -- --mode compiled
dotnet run --project samples/SharpPaint -- --mode interpreted
dotnet publish samples/SharpPaint/SharpPaint.csproj -c Release -r win-x64
```

Inside this repository, `./samples/SharpPaint/run-local.ps1` builds and packs the current SDK
before starting the unchanged sample. Pass `-Mode interpreted` for source execution or
`-Headless` for a non-interactive startup smoke test.

## Functional surface

- Brush, eraser, line, rectangle, and ellipse tools with live previews.
- Outline or filled shapes, 16 swatches, custom hex color, and 1–64 px stroke sizes.
- Add, duplicate, rename, delete, reorder, hide, and fade layers.
- Fifty document-level undo/redo steps and zoom from 25% to 400%.
- Portable `.sharpaint` v1 projects with embedded imported PNGs.
- PNG import/export, file drop, unsaved-change prompts, and keyboard shortcuts.
- Adaptive width and height breakpoints that remain usable at small OS-scaled logical sizes.

The project format is intentionally operation-backed instead of promising Paint.NET file
compatibility. Each layer stores validated drawing commands; imported PNG bytes are embedded as a
data URI so the project can move between machines without breaking references.

## Controls

- `B`, `E`, `L`, `R`, `O`: Brush, Eraser, Line, Rectangle, Ellipse.
- `Ctrl+N`, `Ctrl+O`, `Ctrl+S`, `Ctrl+Shift+S`, `Ctrl+E`: New, Open, Save, Save As, Export PNG.
- `Ctrl+Z`, `Ctrl+Y`: Undo and redo. `+` / `-`: zoom.

## Gap matrix

The disabled commands are part of the experiment: they make the next SharpTS investments
discoverable without pretending the behavior exists.

| Deferred feature | Missing or immature capability | Likely SharpTS direction |
| --- | --- | --- |
| Flood fill | Efficient pixel readback and region mutation | Mutable raster surface or bounded pixel-buffer API |
| Eyedropper | Pixel sampling from the composited scene | Typed canvas sampling service |
| Editable text | Text layout, editing handles, and raster/vector commit semantics | Text drawing command plus overlay editor primitives |
| Selection/transform | Selection geometry, adorners, resize/rotate handles | Transform and overlay/adorner contract |
| Merge down | Lossless flattening of layer commands and eraser masks | In-memory render-to-image service |
| Effects | Filter graph and performant image processing | Reviewed GPU/CPU filter pipeline |
| Advanced blending | More isolated compositing modes | Expanded, portable blend-mode contract |
| Very large documents | Command serialization and full-surface rerasterization costs | Incremental dirty-region rendering and retained scene handles |

Pressure is normalized by SharpTS.Gui for mouse, pen, and touch input, but v1 intentionally uses a
fixed width per gesture. Multi-document tabs, plug-ins, and Paint.NET format compatibility are out
of scope.

## Responsive layout

SharpPaint treats `Window.onMetricsChanged` dimensions as DIPs. A compact width uses an icon tool
rail and smaller command controls; a narrow width collapses Layers behind a toolbar button; a short
height bounds and scrolls layer properties. The palette scrolls horizontally and both side panes
scroll vertically, so no pane can draw over the palette or status bar. These are content
breakpoints, not inverse-DPI scaling—the application continues to honor the Windows accessibility
scale selected by the user.
