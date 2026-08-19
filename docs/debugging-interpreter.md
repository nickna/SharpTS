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

Interpreter v1 exposes one DAP thread and uses cooperative, statement-boundary suspension. A pause
request therefore takes effect at the next safe point, not in the middle of a blocking .NET call.
Worker-created interpreters are independent runtimes and are not yet surfaced as child DAP threads;
the main interpreter remains debuggable while workers execute. This is the principal v1 concurrency
limitation.

## Security and shutdown

The adapter owns the interpreter in-process so paused AST nodes and lexical environments retain
their identity. The tradeoff is that interpreted code has the same operating-system permissions as
the adapter. Only debug programs you trust, restrict `cwd` and assembly references as appropriate,
and do not expose adapter standard I/O through an unauthenticated network bridge.

`terminate`, `disconnect`, client EOF, Ctrl+C, and interpreter exit all release a cooperative pause
and dispose the session. Output is emitted only through DAP `output` events, leaving protocol stdout
clean.
