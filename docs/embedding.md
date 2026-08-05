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
stop retaining output at a caller-supplied character limit.

The service intentionally does not provide a hard timeout or memory sandbox.
Hosts executing untrusted code must call it in a separate process and enforce
wall-clock and memory limits by supervising that process.

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

The module is intended for trusted orchestration code. It is not itself a
sandbox, and a host must not make it importable by the untrusted source being
executed.
