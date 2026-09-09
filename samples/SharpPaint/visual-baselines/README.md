# SharpPaint visual baselines

The `windows` images are reviewed output from `visual.tests.tsx`: light and dark workspaces, color pickers, effects, compact canvas/layers/effects, 150% display scaling, and owned New dialogs. The suite also checks reachable actions, controlled values, text preservation, and native layer keyboard selection. Window comparisons allow at most 128 changed pixels for native antialiased corners (under 0.04% of the smallest workspace image). Dimensions must match, and every remaining pixel must be identical. Owned dialogs use exact comparisons. Baselines never update automatically.

## Rendering contract

Captured with Avalonia 12.1.1, .NET 10.0.11, Windows x64 build 29639, and Segoe UI from the host. The SHA-256 of `C:\Windows\Fonts\segoeui.ttf` was `8134DBCD09E7B123C9A7F229D49CFFBCB01352CC72EA5E1076B65D0DCA9F73CD`. Font fallback and native rendering can differ on another OS or font revision. Use a matching runner for pixel comparisons; review and maintain a separate baseline directory for other platforms.

The matrix sets explicit window themes and client sizes. Before capture it clears editing focus, moves the pointer away from tooltip targets, and waits for native transitions. Capture uses the specific window, including owned dialogs. The full workflow suite runs the structural and behavioral assertions without requiring this machine's font baseline.

## Compare

First build the current local SDK with `./samples/SharpPaint/run-local.ps1 -Headless`, or configure a feed containing the matching SDK. From the repository root:

```powershell
dotnet build samples/SharpPaint -c Release -p:SharpTSEntryPoint=visual.tests.tsx
$env:SHARPAINT_STORAGE_DIRECTORY = Join-Path $PWD ('artifacts/paint-visual-storage-' + [guid]::NewGuid().ToString('N'))
$env:SHARPAINT_VISUAL_BASELINES = (Resolve-Path samples/SharpPaint/visual-baselines/windows).Path
dotnet run --project samples/SharpPaint -c Release --no-build -- --mode compiled --headless
dotnet run --project samples/SharpPaint -c Release --no-build -- --mode interpreted --headless
Remove-Item Env:SHARPAINT_VISUAL_BASELINES
```

A mismatch fails the run and writes a sibling `.actual.png` with expected/actual hashes in the error. Missing baselines also fail. Review differences at full resolution; do not replace references simply to make a test pass.

## Review an intentional change

Unset `SHARPAINT_VISUAL_BASELINES`, set `SHARPAINT_SNAPSHOTS` to an absolute directory under `artifacts`, and run the same entry. Inspect all images, compare both execution modes, and repeat a capture to check stability before copying the reviewed PNGs into `windows`. Clear the environment variables afterwards. Rebuild with `-p:SharpTSEntryPoint=main.tsx` to restore the normal entry point.

Headless snapshots cover layout and rendering, not OS window chrome, physical pen latency, screen-reader output, IME composition, or multi-monitor transitions. Those require real-window validation.
