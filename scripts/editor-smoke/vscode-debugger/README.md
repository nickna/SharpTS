# VS Code interpreter debugger acceptance

This harness launches a real VS Code extension host and exercises the actual SharpTS
extension. It registers an observing debug tracker and calls VS Code's debug APIs. It
does not replace the shipping commands, register an adapter factory, or start an
adapter itself.

## Setup and run

Use an installed VS Code, Node.js, and the .NET SDK required by the repository. Restore
and compile `extensions/vscode-sharpts` with its locked dependencies. Publish the
language server and debug adapter to one coordinated directory, and copy that directory
to `extensions/vscode-sharpts/bin/server` for the development-extension run. For example,
from the repository root in PowerShell:

```powershell
npm ci --prefix extensions/vscode-sharpts
npm run compile --prefix extensions/vscode-sharpts
dotnet publish src/SharpTS.LanguageServer/SharpTS.LanguageServer.csproj -c Release -o artifacts/editor-debugger/published
dotnet publish src/SharpTS.DebugAdapter/SharpTS.DebugAdapter.csproj -c Release -o artifacts/editor-debugger/published
New-Item -ItemType Directory -Force extensions/vscode-sharpts/bin/server | Out-Null
Copy-Item artifacts/editor-debugger/published/* extensions/vscode-sharpts/bin/server -Recurse -Force
node scripts/editor-smoke/vscode-shipping/prepare.mjs --binaries artifacts/editor-debugger/published
node scripts/editor-smoke/vscode-debugger/run.mjs --mode both --package artifacts/editor-shipping/vscode/package/package.json
```

The two modes run sequentially with separate, fresh profiles and extension directories:

- `--mode development` loads the repository extension with `--extensionDevelopmentPath`.
- `--mode vsix` verifies the prepared VSIX hash, installs it through the VS Code CLI,
  and activates the installed extension.
- `--mode both` runs both variants and writes an aggregate result; this is the default.

Optional `--name value` arguments are `--code` (VS Code executable), `--code-cli`
(the VS Code `cli.js` path for a Windows install), `--dotnet`,
`--development-extension`, `--package`, and `--artifacts`. The default executable on
Windows is `%LOCALAPPDATA%/Programs/Microsoft VS Code/Code.exe`; on other platforms it
is `code`. No dependencies are downloaded by the harness itself.

## Assertions and evidence

Each variant checks the active shipping extension's version/path and the hashes of its
language server, core, and adapter assemblies. The test copies
`tests/fixtures/InterpreterDebuggerAcceptance` into its isolated workspace and checks:

- The real **SharpTS: Debug Current File in Interpreter** command saves its dirty active
  document, preserves an unrelated dirty document, and forwards project/reference settings.
  The isolated profile sets `debug.saveBeforeStart` to `allEditors` to verify the command
  suppresses VS Code's save-all participant and saves only its active source document.
- The real `launch.json` resolves the workspace, arguments, environment, and launch defaults.
- Source breakpoints stop in the entry file, imported functions, async and generator
  resumptions, and a worker. A breakpoint on a comment binds to the next executable line.
- Source call stacks, module/function scopes, function arguments, closure values, class
  expansion, and a distinct worker thread are available through VS Code's session API.
- Step in, next, step out, and worker next retain TypeScript source locations.
- Watch and REPL evaluation can read properties; hover evaluation reads an identifier;
  assignment evaluation is rejected without changing the value.
- Debug Console DAP output includes the fixture's class, closure, caught/finally, arguments,
  environment, async, generator, promise, timer, and worker results.
- Caught, uncaught, and unhandled-rejection filters each produce an exception stop and
  `exceptionInfo`, followed by the expected process exit.
- A running loop can pause and terminate, and a new session can run immediately afterward.
- VS Code's restart command stops a paused adapter and launches a fresh adapter with
  new initialize/launch traffic; its Stop API ends that new paused session. VS Code may
  preserve the public session ID across restart.
- Editing a launched source file makes a newly added VS Code breakpoint unverified with
  a changed-source explanation. Restoring the source and relaunching runs normally.

Results are written under `artifacts/editor-debugger/vscode/run-*/`. Each mode retains
`result.json`, `runner-result.json`, `dap-transcript.json`, `dap-transcript.ndjson`, `extension-host.log`, its copied workspace, and its
isolated VS Code profile. The VSIX mode also retains `install.log`. The top-level
`result.json` aggregates both variants. Failures retain the same evidence for diagnosis.
The NDJSON transcript is appended as messages arrive, so it can also diagnose a launch
that has not returned yet; individual launch calls have a 30-second limit.
Tracker errors retain their lifecycle context. VS Code's executable-adapter transport
reports stdout closing as `Error: read error`, even on ordinary shutdown. The harness
accepts only that exact message after receiving both `terminated` and a successful
`disconnect` response, with an `exited` event and no unanswered requests. Natural adapter
exit code zero is accepted. On Windows, VS Code's transport forcibly terminates its
adapter process tree; code one is accepted only when the tracker had already received
`onWillStopSession` and the same complete protocol shutdown preceded the EOF diagnostic.
Those messages are retained as `expectedShutdownDiagnostics`; errors during a session
still fail acceptance. Actual adapter exit codes remain in the evidence; the separate
adapter protocol tests verify natural process exit without VS Code's forced teardown.
The compact [last verified result](last-verified.json) records the tested binary fingerprints,
editor modes, package size, and related regression checks. Full local evidence stays under
the ignored `artifacts/` directory.

These are extension-host and protocol acceptance assertions. They verify the data VS Code
receives for Debug Console, Call Stack, Variables, Watch, breakpoints, and exception
controls. They do not prove visual presentation of those views, Command Palette labels,
gutter icons, focus behavior, or the editor's highlighted execution line. Manual GUI
acceptance remains the source of evidence for those presentation details. Named function
breakpoints, conditional breakpoints, hit counts, and logpoints are outside this harness's
scope; source breakpoints inside functions are covered.

## Timeout cleanup

Each editor run has a 180-second limit. On Windows the runner terminates the process tree
for the exact VS Code process it launched with `taskkill /PID <pid> /T /F`, with a
10-second cleanup limit. Cleanup errors are retained in `runner-result.json`; a failed
cleanup does not make the runner wait indefinitely for an exit event. On POSIX it
sends `SIGKILL` to the launched process only; descendant cleanup is not guaranteed there.
If a POSIX run times out, inspect processes using that run's isolated `user-data` path
before removing its artifacts. The harness never closes unrelated VS Code windows.
