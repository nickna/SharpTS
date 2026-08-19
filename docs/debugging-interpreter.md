# Debugging interpreted TypeScript

SharpTS includes a Debug Adapter Protocol (DAP) server for source-level debugging while the syntax
tree interpreter is executing. This is a different execution path from
[debugging compiled TypeScript](debugging-typescript.md): it launches the `.ts` entry point directly,
does not create a `.dll` or PDB, and does not require the C# extension.

## VS Code

The SharpTS extension bundles the adapter. Run **SharpTS: Debug Current File in Interpreter**, or
add this configuration to `.vscode/launch.json`:

```json
{
  "type": "sharpts-interpreter",
  "request": "launch",
  "name": "Debug app.ts in interpreter",
  "program": "${workspaceFolder}/app.ts",
  "cwd": "${workspaceFolder}",
  "args": [],
  "env": {},
  "stopOnEntry": false,
  "justMyCode": true,
  "console": "internalConsole"
}
```

The command saves a dirty editor before launch. Breakpoints are verified against the saved source;
if a requested line is blank, a comment, a declaration-only construct, or a brace, it moves to the
next executable statement and reports the bound line. Changing a file after launch makes its
breakpoints unverified until the next session, rather than running against mismatched source.

Use **SharpTS: Debug Compiled Current File** for the portable-PDB/CoreCLR path. The two commands are
deliberately separate because they execute different runtimes and may expose different edge cases.

## Standalone DAP tool

Install the client-independent adapter as a .NET tool:

```bash
dotnet tool install --global SharpTS.DebugAdapter
sharpts-dap --version
```

Launch `sharpts-dap` with DAP `Content-Length` framing on standard input and output. It supports one
launch session per process. Diagnostics go to standard error; pass `--log <path>` to write them to
a file. A file log is replaced for each adapter process and capped at 1,048,576 characters, so
repeated sessions cannot grow it without bound. Protocol messages and environment values are not
logged.

The `launch` request accepts:

- `program` (required): saved `.ts`, `.tsx`, `.mts`, or `.cts` entry point.
- `cwd`, `args`, and `env`: working directory, `process.argv` values, and environment overrides.
- `project`: optional tsconfig/project path; normal tsconfig discovery is used when omitted.
- `references`: additional .NET assembly paths.
- `stopOnEntry`, `justMyCode`, and `console`; `internalConsole` is the only console in v1.
- `diagnostics`: `errors` (default), `all`, or `none` for launch-time diagnostic output.

The adapter implements initialize/configuration, source breakpoints, pause/continue, step in/over/out,
threads, stack traces, scopes, paged variables, expression evaluation, exception information,
loaded sources/modules, cancellation, terminate, and disconnect. Frame and variable references are
valid only for the current stop and are capped, so a client cannot retain an unbounded object graph.

## Variables and evaluation

Scopes are grouped as arguments, locals, closure, module, and globals where those environments
exist. Arrays, objects, class instances, maps, sets, and errors expand without invoking getters,
proxy traps, user conversion methods, or arbitrary `ToString` implementations. Property names are
ordered deterministically and the `variables` request honors paging.

Watch and REPL evaluation are read-only. Assignments, updates, deletion, calls, construction,
dynamic import, `await`, `yield`, and function/class creation are rejected. Evaluation is performed
on the interpreter thread with a 250 ms budget and has no filesystem or network privilege beyond
what a permitted property read already references. Hover uses the stricter subset and does not read
properties or indices.

## Exceptions, async code, and concurrency

The adapter advertises three exception breakpoint filters:

- `uncaught` (on by default) stops before an exception leaves the program.
- `caught` stops as a `catch` receives the thrown value.
- `unhandledRejection` (on by default) stops when SharpTS reports an unhandled promise rejection.

Async functions and generators use their TypeScript statement locations; stepping resumes at the
next source statement rather than exposing the runtime continuation machinery. Imported and
dynamically discovered modules appear as separate sources.

The main interpreter is DAP thread 1. Every interpreter-mode `Worker`, including a worker created by
another worker, appears as a separate named DAP thread with a stable, session-only ID. Worker IDs are
not the same contract as guest `worker_threads.threadId`. A worker emits standard thread
started/exited events, and its script and imported modules appear in loaded sources/modules. A
breakpoint set before a worker loads its file is initially unverified and emits a standard
`breakpoint/changed` event when that worker binds it.

Suspension is coordinated all-stop and cooperative. A breakpoint, exception, or pause in any
interpreter requests a pause in every live interpreter and wakes idle worker event loops. Each
thread reports a stopped event only after it actually reaches a safe point;
`allThreadsStopped: false` means other interpreters may still be running, and the final parked
thread reports `allThreadsStopped: true`. Only threads that have reported a stop can be inspected
during partial convergence.

Continue always resumes every interpreter. Step in, over, or out applies its step condition to the
selected thread while also resuming its peers normally. This keeps worker messages, promises, and
timers live while the selected thread advances. A worker created while a coordinated stop is still
converging immediately inherits the pending pause request.

Safe points are TypeScript statement and callback boundaries plus idle event-loop checkpoints. A
pause therefore cannot preempt an interpreter in a blocking or long-running managed/native call;
that thread remains running and `allThreadsStopped` remains false until it reaches a safe point.
The DAP reader, continue, terminate, and disconnect requests remain responsive during that wait.

## Security and shutdown

The adapter owns the interpreter in-process so paused AST nodes and lexical environments retain
their identity. The tradeoff is that interpreted code has the same operating-system permissions as
the adapter. Only debug programs you trust, restrict `cwd` and assembly references as appropriate,
and do not expose adapter standard I/O through an unauthenticated network bridge.

`terminate`, `disconnect`, client EOF, Ctrl+C, and interpreter exit release every cooperative pause,
wake and shut down all registered interpreter event loops, and wait within the session's five-second
ownership bound. Output is emitted only through DAP `output` events, leaving protocol stdout clean.

## Extension Development Host acceptance

The protocol tests cannot validate VS Code presentation. Before releasing the extension, use the
tracked [`InterpreterDebuggerAcceptance`](../tests/fixtures/InterpreterDebuggerAcceptance) fixture
to exercise the development extension and the production VSIX.

From the repository root, install the locked extension dependencies and build both bundled servers:

```bash
cd extensions/vscode-sharpts
npm ci --ignore-scripts
npm run prebuild
npm test
```

Open a fresh Extension Development Host with the fixture as its workspace (replace `code` with the
appropriate VS Code command if necessary):

```bash
code --new-window --extensionDevelopmentPath="$PWD" ../../tests/fixtures/InterpreterDebuggerAcceptance
```

Copy `interpreter.launch.json` in the fixture to `.vscode/launch.json`. Also set
`sharpts.projectFile` to the fixture's `tsconfig.json` and leave
`sharpts.additionalReferences` empty. Run the checklist once with **SharpTS: Debug Current File in
Interpreter** and once with **SharpTS interpreter acceptance** from the Run and Debug view. For the
packaged pass, run `npm run package`, install the resulting VSIX into a clean VS Code profile with
separate `--user-data-dir` and `--extensions-dir` directories, and repeat the same checks there.

### Manual UI checklist

- Record the OS, architecture, VS Code version, `dotnet --version`, commit SHA, extension version,
  adapter `--version`, and whether the development extension or packaged VSIX is under test.
- Make `main.ts` dirty before running the command. Confirm that only that document is saved, the
  launched source matches the saved bytes, and the session uses the configured project.
- Set entry and imported-source breakpoints. Set another breakpoint on the comment above the loop
  conditional and on a closing brace; confirm VS Code shows their predictable relocated, verified
  executable lines.
- At a function and class-method stop, inspect Call Stack plus Arguments/Locals, Closure, Module, and
  Global scopes where applicable. Expand arrays/objects, add watches, hover an identifier, and use
  the Debug Console for a permitted read-only expression. Confirm mutation and call expressions are
  rejected.
- Enable each caught, uncaught, and unhandled-rejection exception filter in turn. The default run
  exercises the caught error; set `SHARPTS_DAP_EXCEPTION` in the launch configuration to `uncaught`
  or `unhandled` for separate sessions covering the other filters. Confirm exception details are
  presented without corrupting the following continue/step flow.
- Step in, over, and out through the loop, closure, class method, `afterAwait`, generator resume,
  Promise callback, and timer callback. Confirm runtime continuation frames do not replace the
  TypeScript locations.
- Set the worker breakpoint before `worker.ts` loads. Confirm it changes from unverified to verified,
  the worker appears as its own thread, and its stack, locals, evaluation, output, and exit event are
  attributed to that thread.
- While paused, change an imported source on disk. Confirm its breakpoints become unverified rather
  than binding to changed lines; undo the edit and start a new session to rebind them.
- Confirm `args=alpha,beta`, `env=configured`, and every expected fixture output line appears only in
  the Debug Console. Inspect `.sharpts/debug-adapter.log` to ensure it excludes DAP messages and
  environment values and never exceeds the documented cap.
- Exercise pause, continue, restart, stop, and closing the Extension Development Host while paused.
  Confirm each adapter/debuggee exits within five seconds with no orphan process or new tracked file.

Attach screenshots or a concise transcript for breakpoint verification, scope grouping, exception
presentation, worker threads, and clean termination to issue #1405. Record each checklist item as
pass/fail with a defect link; all items must pass for both development and packaged runs before the
issue is closed.
