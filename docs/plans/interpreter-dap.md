# Interpreter Debug Adapter Protocol plan

Status: launch-only v1 implemented; debuggable worker threads follow-up #1404 implemented
(2026-08-19).

Tracks [#1400](https://github.com/nickna/SharpTS/issues/1400) and its children #1401–#1405.
This decision is independent of compiled-mode portable-PDB debugging in #1306.

## Product decision

SharpTS will ship an interpreter debugger. Compiled `--debug` remains the preferred path when a
program can be compiled and a managed debugger is available, but it cannot cover these concrete
interpreter use cases:

- reproducing behavior that exists only in the tree-walking backend;
- debugging embedded or dynamically evaluated code for which no managed assembly/PDB exists;
- diagnosing module loading, REPL/evaluation, promise, timer, and worker behavior before compilation;
- avoiding compile and managed-debugger startup latency during short edit/run cycles; and
- using a client that speaks DAP but has no compatible CLR debug adapter.

The maintenance cost is accepted only for the deliberately narrow boundary below. SharpTS owns
one protocol state machine, one debugger-neutral interpreter controller, one VS Code integration,
and a deterministic raw-protocol harness. Editor-specific behavior must not enter the interpreter.
Time travel, hot replacement, managed/native debugging inside CLR calls, and attach-to-process are
not v1 features.

## Process, packaging, and compatibility

`sharpts-dap` is a separate .NET tool and process. It owns one launched interpreter and speaks DAP
over stdin/stdout. stdout is protocol-only; guest stdout/stderr is converted to DAP `output`
events, while adapter diagnostics go to stderr or an explicitly configured log file. The adapter
and core assemblies use the same package version. The VS Code extension always launches its
bundled adapter; it never silently selects a separately installed, potentially incompatible tool.

The adapter hosts the interpreter in-process instead of spawning the ordinary `sharpts` CLI. This
keeps AST nodes, `SourceDocument` instances, lexical environments, and guest object identities
available for inspection. The adapter owns and restores process-wide cwd/environment/guest-exit
hooks and stops the debuggee before exiting, so disconnect cannot leave an orphan process.

The DAP protocol surface is implemented directly over `System.Text.Json`; no protocol package is
added. This avoids a new license, trimming, NativeAOT, and package-size dependency. The supported
wire contract is the DAP schema published with VS Code 1.85, limited to capabilities advertised by
the adapter. Unknown optional fields are ignored for forward compatibility.

## Launch contract

The v1 request is `launch` only. Its configuration accepts:

| Property | Meaning |
| --- | --- |
| `program` | Required saved `.ts`, `.mts`, `.cts`, or `.tsx` entry file. |
| `cwd` | Working directory; defaults to the program directory. |
| `args` | Guest `process.argv` entries after the script path. |
| `env` | Explicit environment additions/overrides. Null removes a variable. |
| `project` | Optional tsconfig path; normal discovery applies when omitted. |
| `references` | Additional managed assembly references. |
| `stopOnEntry` | Stop at the first executable source statement. |
| `justMyCode` | Hide virtual/default-library sources by default. |
| `diagnostics` | `errors` (default), `all`, or `none`. |
| `console` | `internalConsole` in v1; other values fail clearly. |
| `logFile` | Optional redacted adapter diagnostic log (configured by the client process launch). |

The source checksum captured during launch is authoritative. A file changed after launch is marked
stale and its breakpoints remain unverified rather than silently binding to different text.

## Execution and concurrency model

Interpreter safe points are source-backed executable statements plus function/module/callback
entry. Debugging is opt-in: a normal interpreter has no controller, so its hot dispatch performs a
single predictable null check and allocates no debugger state.

The main interpreter, its event-loop callbacks, promise continuations, and timers form DAP thread
1. Every interpreter-mode Worker, including nested workers, registers with a debugger-neutral
session host and becomes a separate DAP thread. A stop requests cooperative suspension of every
live interpreter. Threads report their real parked state individually until `allThreadsStopped`
becomes true; an interpreter blocked in a managed/native call is never reported stopped early.
Evaluation requests are marshalled back to the selected parked interpreter thread.

Continue resumes every interpreter. Step in/over/out applies its condition only to the selected
thread while peers resume normally, preserving worker-message and event-loop liveness. Workers
created during stop convergence inherit the pending pause, and worker exit removes that thread from
the convergence set. The DAP reader remains live on its own thread throughout.

Step-in stops at the next distinct executable statement. Step-over also requires call depth not to
increase; step-out requires it to decrease. Source identity, frame identity, and span progress—not
display text—define distinct locations. Breakpoints take precedence over an active step, followed
by explicit pause. Synthetic/hidden nodes never stop.

## Inspection and evaluation policy

Stopped-state frame and variable handles include a stop generation and are invalid immediately on
continue, disconnect, or termination. Stack frames are guest frames only. Scopes are projected
from the actual `RuntimeEnvironment` chain as Arguments/Locals, Closure, Module, and Global scopes;
shadowed bindings remain in their owning scope.

Expansion reads interpreter-owned data stores directly. It never invokes getters, proxies, CLR
properties, `toString`, or arbitrary guest code. Collections are lazy, paged, deterministically
ordered, cycle-safe, and bounded. Host-only controller, synchronization, reflection, and stream
objects are summarized rather than exposed.

`evaluate` is supported for hover, watch, and REPL while stopped, but v1 is read-only: assignment,
updates, calls, construction, deletion, dynamic import, `await`, and `yield` are rejected before
execution. Explicit property/index reads may invoke a guest getter or proxy and therefore are
allowed only for watch/REPL, not hover. Evaluation runs on the paused interpreter thread with a
250 ms cooperative timeout and cancellation. Guest throws become failed DAP responses and never
escape into the adapter state machine. `setVariable` and `setExpression` are not advertised.

## Exceptions, async work, modules, and shutdown

Exception filters are `caught`, `uncaught`, and `unhandledRejection`. Exception stops preserve the
guest value and originating source location. Async physical frames remain ordinary guest frames;
when an await/timer/promise scheduling origin is known it is shown as a labeled logical async
origin, never as a fabricated physical stack.

All real source modules register by normalized absolute identity, including late dynamic imports.
Virtual standard-library documents are available through DAP `source` but are hidden by Just My
Code unless requested. Duplicate basenames never share breakpoint state.

`disconnect` defaults to terminating the owned debuggee. `terminate`, cancellation, Ctrl+C, EOF,
and protocol failure release every cooperative waiter, cancel outstanding inspection work, drain
bounded adapter state, restore process-wide hooks, and close streams within five seconds.

## Acceptance bounds

- Debug-disabled statement-dispatch median overhead must remain below 2% on dispatch-heavy
  microbenchmarks; no debugger allocations are permitted on that path.
- Adapter package growth is recorded at release validation and must remain below 5 MiB compressed
  beyond the existing SharpTS tool payload.
- Median local continue-to-breakpoint latency is below 50 ms and ordinary variable expansion below
  100 ms for the documented end-to-end fixture.
- Retained debugger handles are capped per stop and all guest references are released on continue.

The automated protocol suite is the second, client-agnostic DAP client required by the epic. The
VS Code checklist remains a release gate because UI presentation cannot be proved by protocol
transcripts alone.

## Implementation validation

The implementation was validated on Windows on 2026-08-19:

- The client-independent debugger suite covers framing, lifecycle, breakpoints, step/stack/scope/
  evaluate flows, caught and uncaught exceptions, unhandled rejections, async and generator frames,
  duplicate module basenames, stale source, worker output, cancellation-safe shutdown, handle
  generations, paging/cycles, redaction, and getter-free expansion.
- A load-sensitive raw-DAP benchmark runs 21 continue-to-breakpoint and variable-expansion samples
  and enforces the 50 ms / 100 ms median limits; both limits passed locally.
- An alternating A/B statement-dispatch audit against commit `4efa3e46` produced aggregate medians
  of 67.191 ms without debugger support and 64.149 ms with the null debugger hook. The distributions
  overlap, so no debug-disabled slowdown was measurable (and no debugger allocation occurs).
- The packed `SharpTS.DebugAdapter` tool was installed and executed from its `.nupkg`. Its compressed
  size was 6,683,855 bytes versus 6,540,982 bytes for the existing `SharpTS` tool payload: a 142,873
  byte delta, well below the 5 MiB ceiling.
- The VS Code extension compiled and produced an 8.71 MiB VSIX containing the adapter. Co-locating
  the language server and adapter reduced the first duplicate-runtime package attempt from 15 MiB.

Two release gates cannot be completed purely in this repository: NuGet must onboard the new
`SharpTS.DebugAdapter` package ID before a tag can pass preflight, and the VS Code presentation
checklist still requires an interactive Extension Development Host. The worker-interpreter
limitation was resolved by #1404. Cooperative suspension still cannot preempt a blocking
managed/native call; partial-stop events report that limitation accurately.
