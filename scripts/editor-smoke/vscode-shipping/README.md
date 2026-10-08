# Shipping VS Code extension smoke

This optional local smoke installs the packaged SharpTS extension into fresh VS Code user-data and extension directories. Its test extension has an empty activation function, registers no language providers, and starts no language server. The installed shipping extension starts SharpTS with its unchanged `interop-only` / `sharpts-only` defaults. VS Code's built-in TypeScript extension and the real Prettier extension remain active.

The test checks an imported ordinary identifier's definition, a CLR decorator hover containing `System.Text.StringBuilder`, a reflection member hover containing `StringBuilder.Append`, and two actual dirty-document saves. Each save must match the exact pinned local Prettier output, and fresh navigation and both CLR hovers must still succeed afterward. Prettier's logs must identify the local module and pinned version on both saves. Installed server/core hashes must match the VSIX preparation manifest. The real LanguageClient trace must show six successful hover responses and reject ordinary navigation/formatting providers in both initialize and dynamic registrations.

Prerequisites: .NET 10, Node 22 or later, a real VS Code installation, and an unpacked `esbenp.prettier-vscode` 12.4.0 extension directory. Acquire that extension separately through VS Code's supported installation mechanism; the runner neither downloads it nor uses a user profile. Install locked dependencies:

```powershell
npm ci --ignore-scripts --prefix extensions/vscode-sharpts
npm ci --prefix tools/formatter-interop
dotnet build SharpTS.sln -c Release
dotnet publish src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj -c Release --no-build --no-restore -o artifacts/editor-shipping/vscode/published
dotnet publish src/SharpTS.DebugAdapter/SharpTS.DebugAdapter.csproj -c Release --no-build --no-restore -o artifacts/editor-shipping/vscode/published
node scripts/editor-smoke/vscode-shipping/prepare.mjs --binaries artifacts/editor-shipping/vscode/published
node scripts/editor-smoke/vscode-shipping/run.mjs --prettier-extension /absolute/path/to/esbenp.prettier-vscode-12.4.0
```

`prepare.mjs` compiles the unchanged extension source, stages its locked production dependencies and published binaries, and invokes VSCE directly. It avoids npm's `prebuild` / `prepackage` hooks so build ownership stays explicit. It writes the VSIX, hashes and package log under `artifacts/editor-shipping/vscode/package` by default. A fresh staging directory prevents stale package files from carrying over.

`run.mjs` accepts `--package <preparation-manifest>`, `--code <executable>`, `--code-cli <cli.js>` (Windows only, when auto-detection is ambiguous), `--dotnet <executable>`, `--prettier-extension <directory>` and `--artifacts <output-parent>`. Arguments are `--name value` pairs. Windows defaults to the per-user stable VS Code install; other platforms default to `code` on PATH. Every run has a fresh artifact subdirectory and a 90-second deadline, with a result JSON, installation log, extension-host output and VS Code logs. On the tested Windows host, timeout cleanup stops the isolated editor process tree. The POSIX fallback kills the launched process only; descendant cleanup and the editor smoke have not been verified there.

This proves actual extension activation and provider coexistence for the recorded editor/package versions. It does not claim that VS Code exposes provider attribution or that multiple providers are routed exclusively. Ordinary navigation is checked in the shipping interop mode, where SharpTS does not advertise definition support; CLR content is specific to SharpTS. The test does not cover debugger launch or change the extension's default mode.

Verified on 2026-10-07 with VS Code 1.134.0 on Windows ARM64, shipping SharpTS 0.2.0 from base source `a20b07d7259043dd06a33fae34a507b443c847d8` plus the #1981 working-tree changes, built-in TypeScript extension 10.0.0, Prettier extension 12.4.0 and local Prettier 3.9.9. [Compact verification](last-verified.json) records the package, versions and actual capabilities. The default interop registrations, six fresh CLR responses, document versions 1 → 3 → 5 and two formatter-module log entries are recorded in `artifacts/editor-shipping/vscode/runs/run-1ubuPj/result.json`. The installed VSIX SHA-256 was `8a8817c21e4efeff77e3855f0e4ace2708403a7d6fb845d9f40af66214cf7c90`; the final server SHA-256 was `9dc0e3c3b4d401e833735179ee84185c460b9f2dcb2ae187f71f8e808627a40b`. Core hashes and raw logs remain alongside the result.
