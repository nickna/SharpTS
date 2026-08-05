# Embedding SharpTS

SharpTS exposes managed APIs for hosts that need to analyze or execute source
without going through the command-line interface.

## Compile and execute separately

`SharpTS.Compilation.CompilationService` compiles a source string to an in-memory
assembly and can execute those bytes through a collectible load context. It
returns structured compiler diagnostics and never performs source-file I/O.

## Execute one bounded source string

`SharpTS.Execution.SourceExecutionService` is the higher-level facade for a
single-source host. `Interpret()` runs the normal lexer, parser, recovery-mode
type checker, resolver, and interpreter. `CompileAndExecute()` uses
`CompilationService`. Both disable decorators, capture stdout and stderr, and
stop retaining output at a caller-supplied character limit. The limit includes
the truncation marker, so returned output never exceeds it.

The single-source APIs have no module resolver. They explicitly reject static
imports, re-exports, dynamic `import()`, TypeScript `import = require()`, and
CommonJS `require()`. This is part of the untrusted-source boundary rather than
an incidental type-checker failure.

The service intentionally does not provide a hard timeout or memory sandbox.
Hosts executing untrusted code must call it in a separate process and enforce
wall-clock and memory limits by supervising that process. Calls through the
facade are serialized because compiled execution redirects process-wide console
writers; a disposable worker process should execute one request at a time.

## Calling from a SharpTS program

Trusted TypeScript host programs can use the runtime-backed module:

```typescript
import {
  configureUntrustedProcess,
  runSourceJson
} from "sharpts:execution";

configureUntrustedProcess("http://blocked.invalid:9");
const result = JSON.parse(
  runSourceJson("console.log(6 * 7);", "compile", 100 * 1024)
);
```

`configureUntrustedProcess()` enables SharpTS's cross-process signal restriction
and assigns `HttpClient.DefaultProxy` to the supplied blocking proxy. The module
is a soft dependency on the managed SharpTS runtime. Compiled output automatically
receives the complete SharpTS runtime closure; `--standalone` is therefore not
valid for programs that use this module. Native AOT compiler hosts reject it.

The proxy and signal settings are process-wide, permanent defense-in-depth
controls. They do not replace OS- or container-level network and process
isolation, and they do not affect non-HTTP networking APIs.

The module is intended for trusted orchestration code. It is not itself a
sandbox, and a host must not make it importable by the untrusted source being
executed.
