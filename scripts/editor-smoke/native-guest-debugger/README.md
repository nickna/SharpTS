# Native guest debugger acceptance

This optional Windows x64 check publishes a real NativeAOT guest and launches it through the
official Microsoft C++ extension (`ms-vscode.cpptools`) inside its supported VS Code client.
It uses public editor debug APIs and records the actual native adapter traffic. It copies the
extension into a fresh owned profile, disables updates there, and leaves the normal profile
untouched. The runner does not invoke the licensed adapter outside VS Code.

Install the .NET 10 SDK, Windows NativeAOT C++ prerequisites, VS Code and the official C++
extension. Build the compiler, then pass that extension's installed directory:

```powershell
dotnet build src/SharpTS/SharpTS.csproj -c Release -m:1
node scripts/editor-smoke/native-guest-debugger/run.mjs `
  --cpp-extension C:/absolute/path/to/ms-vscode.cpptools-version-win32-x64 `
  --dotnet 'C:/Program Files/dotnet/dotnet.exe'
```

Optional `--name value` arguments are `--code`, `--code-cli` (for an ambiguous installation),
`--compiler` and `--artifacts`. The compiler defaults to the Release build above. The runner
requires Node.js and a Windows x64 process. It cleans stale SDK-smoke PATH entries only in
its child environment so the SDK's Windows command files can find the standard OS tools.

## Required observations

The tracked [entry](../../../tests/fixtures/NativeGuestDebuggerAcceptance/main.ts) imports the
[helper](../../../tests/fixtures/NativeGuestDebuggerAcceptance/helper.ts). Their original bytes
are copied unchanged. The runner exports with `--debug --ref-asm --verify`, then uses the
supported SDK `dotnet publish -c Debug -r win-x64` pipeline. Trim/AOT warning failures remain
enabled. It requires a native x64 PE without a CLR header, a native PDB, and a standalone run
with empty PATH and no DOTNET_ROOT/host-path variables producing `native-debug=42` and exit 0.

The real debugger must verify and hit the original `main.ts` entry breakpoint, step to the
call line, and hit the imported `helper.ts` breakpoint with the original TypeScript caller
frame. It must then step to the helper's return line, hit the output line, print the same
output, and exit successfully. Stop reasons and original file/line locations are asserted.
After proving the helper breakpoint hit, the test removes it and waits for the adapter's
acknowledgement before stepping: a native source breakpoint can bind several instruction
addresses on one line.

The test also issues Step In at the call and records what actually happens. In the accepted
run, it entered `RhpNewPtrArrayFast` in `AllocFast.asm`, retaining `main.ts` in the caller stack.
Continuing reached the imported helper breakpoint; stepping there reached `helper.ts` line 3.
Native runtime, reflection-dispatch and generated wrapper frames remain visible. This check
does not establish a seamless TypeScript-only Step In or managed Just My Code behavior.

Locals and Watch responses are recorded as observations. The accepted helper frame exposed
`value = 40` and `result = 42` as native doubles. The test does not require source-local
projection, hoisted async/generator locals, or that presentation under optimization.
Release output, other platforms, complex source domains and automatic native CLI launch
integration have separate acceptance requirements.

## Evidence

[Compact verification](last-verified.json) records the measured environment, original source
and harness hashes, guest/native image and symbol hashes, actual stop sequence and limitations.
Each unique `artifacts/native-guest-debugger/run-*` retains export/publish/standalone logs,
`result.json`, the full `transcript.json`, extension-host logs, native symbols and isolated
profile. Failed attempts retain their logs too. Tool phases have a 300-second deadline and the
editor has a 180-second deadline; timeout cleanup targets only that launched process tree.
Compiler, harness, original fixture and compiled/debugged workspace source hashes must remain
unchanged throughout an accepted run.

See [native TypeScript debugging](../../../docs/debugging-typescript.md#nativeaot-guest-debugging-windows-x64)
for a manual launch configuration. NativeAOT requires a native debugger after publication;
see the [.NET NativeAOT diagnostics guidance](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/diagnostics)
and the [C++ launch reference](https://code.visualstudio.com/docs/cpp/launch-json-reference).
