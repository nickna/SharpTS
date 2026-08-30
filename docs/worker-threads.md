# Worker threads

SharpTS `Worker` instances execute TypeScript concurrently on dedicated operating-system threads.
This is true in both execution modes:

- an interpreted parent creates an isolated interpreter on the worker thread;
- a compiled parent compiles an eligible worker module graph to IL, loads it into a collectible
  `AssemblyLoadContext`, and runs that compiled artifact on the worker thread. Graphs that use a
  not-yet-ported cross-realm feature retain the interpreter-backed compatibility path described
  below.

The compatibility target is the repository's pinned Node.js 22.23.2 `worker_threads` behavior.
This target is an API and lifecycle contract, not an assertion that V8 isolates and .NET load
contexts have identical implementation details.

## Isolation model

Every compiled worker realm has its own generated runtime statics, module cache, globals, and event
loop. The worker artifact is cached by a hash of its source graph, but every worker loads the cached
bytes into a new collectible realm. Consequently, two workers compiled from the same files do not
share mutable module state. The realm is unloaded after exit.

`isMainThread`, `threadId`, `workerData`, `parentPort`, and the worker-global `postMessage` are bound
when the realm starts. Messages are marshalled onto the receiving realm's event loop, and ordinary
values cross the boundary through SharpTS structured cloning rather than by sharing their mutable
object graph. A `parentPort` message listener keeps the worker loop alive until the port is closed or
unrefed, matching Node's port lifecycle.

Termination is cooperative. SharpTS checks for cancellation at interpreted statement/loop safe
points and at compiled loop backedges and event-loop turns. `terminate()` wakes an idle loop and
reports exit code 1. Managed code that never returns to a SharpTS safe point remains a .NET hosting
limit; .NET does not provide a safe general-purpose forced thread abort.

## Compatibility contract

| Capability | Interpreted | Compiled | Notes |
| --- | --- | --- | --- |
| Dedicated OS thread | Yes | Yes | CPU work can overlap across workers. |
| Isolated language/module state | Yes | Yes | Compiled workers use one collectible load context per worker. |
| File/module worker entry | Yes | Yes | Relative imports are resolved as a module graph. |
| `isMainThread`, `threadId`, `workerData`, `parentPort` | Yes | Yes | Both imported and maintained worker-global forms are supported. |
| Parent/worker message delivery | Yes | Yes | Listener receives the posted value directly, as in Node. |
| Early parent messages | Yes | Yes | Queued until worker bootstrap completes. |
| `messageerror` for clone failures | Yes | Yes | Receiver-side event model. |
| `parentPort.start/close/ref/unref/hasRef` | Yes | Yes | A message listener starts and refs the port. |
| `online`, `message`, `error`, `exit` lifecycle | Yes | Yes | Events are marshalled onto the parent loop. |
| CPU-loop termination | Yes | Yes | Cooperative safe-point cancellation. |
| `stdout: true` / `stderr: true` capture | Yes | Yes | Compiled output is routed per worker rather than by swapping process-global writers. |
| Transfer ports/buffers and blocking atomics | Yes | Compatibility fallback | Runs on a dedicated interpreter-backed worker pending compiled realm bridges. |
| `stdin: true` | Yes | Compatibility fallback | Runs on a dedicated interpreter-backed worker pending compiled stdin routing. |
| Worker realm cleanup | Yes | Yes | Compiled load-context collection is covered by a regression test. |

The following currently use the interpreter-backed worker compatibility path when started by a
compiled parent. They remain real dedicated threads and preserve existing API behavior, but they
must not be used as evidence of compiled-worker throughput or full Node parity:

- cross-realm transfer of `MessagePort`, `ArrayBuffer`, `SharedArrayBuffer`, and typed-array views;
- blocking `Atomics.wait`;
- per-worker stdin routing.

Other current differences are that dynamic `eval` worker entry points are not supported,
`resourceLimits` are reported but cannot be enforced as V8 heap limits by the .NET runtime, and a
Native AOT compiler host rejects compiled output that constructs `Worker` because the worker
bootstrap requires runtime IL generation and collectible loading.

Compiled deployment must include the managed SharpTS runtime dependency closure and the worker
TypeScript source/module graph. The CLI co-locates that closure automatically unless
`--standalone` is requested. Worker artifacts are compiled lazily on first use in the process and
then reused by source hash. Worker compilation time is therefore a startup cost, not part of
steady-state execution throughput.

## Benchmarking multi-threaded performance

A worker benchmark should prove parallelism and measure scaling separately from startup:

1. Use one byte-identical TypeScript parent and worker source graph for SharpTS and Node.
2. Create a fixed worker pool outside the steady-state timing interval and complete one warmup job
   per worker. Report first-worker compilation/startup as a separate metric.
3. Give each worker independent CPU-bound work with a deterministic checksum. Do not time only
   message passing or a task too small to amortize dispatch.
4. Run the same total work with 1, 2, 4, and up to the machine's logical-processor count. Report
   throughput and speedup relative to that runtime's one-worker result.
5. Record processor, logical-core count, OS, .NET, Node, GC profile, and worker count. Avoid comparing
   results collected on different hosts.
6. Keep correctness checks outside the timed interval but fail the run if any checksum differs.

The cross-runtime suite's normal timings exclude process startup and SharpTS compilation. A worker
scaling workload should preserve that convention for its throughput result and publish cold worker
startup beside it, rather than mixing two different costs into one number.
