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
launch session per process. Diagnostics go to standard error; pass `--log <path>` to append them to
a file. Protocol messages and environment values are not logged.

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
