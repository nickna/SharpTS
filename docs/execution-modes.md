# Execution modes and CLI

SharpTS shares one parser, type checker, module resolver, and project model across interpretation
and compilation. Choose a command by the artifact you need, then rely on the parity contract below
for supported behavior.

## Command map

| Goal | Command | Result |
| --- | --- | --- |
| Start the REPL | `sharpts` | Interactive interpreted session |
| Run a program | `sharpts app.ts` | Interprets the module graph immediately |
| Type-check one program | `sharpts --noEmit app.ts` | Diagnostics only |
| Check a project | `sharpts -p .` | Checks roots selected by `tsconfig.json`; no runtime assembly |
| Check project references | `sharpts --build` | Checks the reference graph in dependency order |
| Compile a DLL | `sharpts --compile app.ts` | `app.dll` plus `app.runtimeconfig.json` |
| Compile an executable | `sharpts --compile app.ts --target exe` | Platform executable output |
| Emit declarations | `sharpts --compile app.ts --declaration` | Assembly plus checked `.d.ts` files |
| Emit declarations only | `sharpts --compile app.ts --emitDeclarationOnly` | `.d.ts` files without an assembly |

Arguments after a script are exposed through `process.argv`. Use `--` when a guest argument begins
with `-`:

```bash
sharpts app.ts -- --verbose input.txt
```

## Interpreted programs

```bash
sharpts app.ts
```

The interpreter executes the checked syntax tree and is the shortest development loop. It supports
the same project discovery, external-reference configuration, TypeScript strictness flags, JSX
settings, and module resolver used by compilation. `-r/--reference` and the nearest `sharpts.json`
apply in this mode too.

Interpretation is also used by the REPL and by some Node-compatible operations that start source
workers. It ships no output artifact.

## Compiled output

```bash
sharpts --compile app.ts -o out/app.dll
dotnet out/app.dll
```

`--compile` emits .NET IL directly. The default target is `dll`; `-t exe` or `--target exe` selects
an executable. Built-in executable bundling is supported on Windows and Linux. DLL output is the
portable choice for hosting, C# references, and other platforms.

| Option | Meaning |
| --- | --- |
| `-o <path>` | Set the output path. |
| `-t, --target dll\|exe` | Select a DLL (default) or executable. |
| `--bundler auto\|sdk\|builtin` | Choose executable bundling. `auto` permits fallback; an explicit mode fails instead of changing technique. |
| `--gc-profile workstation\|adaptive\|throughput` | Select deployment GC policy. Workstation is the conservative default; adaptive is recommended for measured allocation-heavy services; throughput is an expert fixed-server opt-in. |
| `--preserveConstEnums` | Keep `const enum` declarations instead of inlining them. |
| `--ref-asm` | Shape output for use as a C# reference assembly. |
| `--sdk-path <path>` | Override the .NET SDK reference-assembly location. |
| `--verify` | Run IL verification after emission. |
| `--msbuild-errors` | Format compiler diagnostics for MSBuild. |
| `--quiet` | Suppress success messages. |
| `--standalone` | Suppress automatic copies of soft runtime and external interop dependencies. |

### GC deployment profiles

GC selection is deployment policy, not a JavaScript semantic or IL optimization. The default
`workstation` profile minimizes startup and memory risk for interactive, one-shot, and unknown
programs. `adaptive` enables concurrent server GC with dynamic adaptation (DATAS) and is the
recommended starting point for sustained allocation-heavy services:

```bash
sharpts --compile service.ts --gc-profile adaptive -o service.dll
```

`throughput` disables DATAS and uses fixed server heaps. It can consume hundreds of megabytes more
than the other profiles and should be selected only after deployment-specific measurement. The
profile is propagated identically into DLL runtimeconfig files and both executable bundlers. See
the [benchmark decision record](../benchmarks/gc-profiles/decision.md) for cross-platform evidence.

### Conditional runtime dependencies

The emitted program contains its ordinary JavaScript runtime and normally has no metadata
dependency on `SharpTS.dll`. Some features deliberately late-bind to the managed SharpTS runtime,
including compiled `eval`, Proxy/Intl paths, selected `vm`/DNS behavior, and dynamic .NET events.
The compiler records these requirements and copies `SharpTS.dll` beside the output only when one is
present. Pure programs do not receive that copy.

External assemblies used by `.NET` interop are hard dependencies. The compiler normally copies
the used assemblies and their copy-local closure. `--standalone` suppresses both kinds of automatic
copy; it does not remove the program's need for them. A soft-dependent feature then raises a clear
runtime error, and hard dependencies must be deployed by the application.

### Hosted DLL output

`--hosted` is an advanced compiler option used by `SharpTS.Hosting` and the GUI host:

```bash
sharpts --compile guest.ts --target dll --hosted -o guest.dll
```

It emits the versioned hosted program factory and lifecycle contract, copies
`SharpTS.Hosting.Abstractions`, and permits host-controlled initialization and event-loop shutdown.
It is valid only with `--target dll`; a hosted DLL is not a normal command-line entry assembly.
Application hosts should consume the public hosting abstractions instead of reflecting over the
generated `$`-prefixed implementation types.

## Projects and build mode

SharpTS discovers `tsconfig.json` next to or above a named script. Use `-p/--project` to select a
file or directory explicitly, or `--no-tsconfig` to disable discovery:

```bash
sharpts -p ./configs/tsconfig.json
sharpts -p . --watch
sharpts --build
sharpts --build packages/app --force
```

Project commands type-check the roots selected by `files` and `include`; they do not produce a
runtime assembly. Imports can still bring an excluded file into the semantic program. Supported
project behavior includes `extends`, references/composite projects, incremental state, watch mode,
`baseUrl`/`paths`, declaration inputs, JavaScript roots, and classic, Node, and bundler module
resolution.

| Option | Meaning |
| --- | --- |
| `-p, --project <path>` | Select a `tsconfig.json` file or containing directory. |
| `-b, --build [projects...]` | Check a project-reference graph. |
| `-w, --watch` | Recheck affected project inputs after changes. |
| `--incremental` | Reuse SharpTS build state when inputs are unchanged. |
| `--force` | Ignore build state and recheck the graph. |
| `--no-tsconfig` | Skip configuration discovery for a script/compile command. |
| `--showConfig` | Print the resolved configuration and value sources as JSON, then exit. |

Command-line settings win over configuration. `target` and `module` are accepted configuration
keys but do not select .NET IL output. Set `SHARPTS_TSCONFIG_VERBOSE=1` to report every ignored
configuration option.

## Declaration output

Declaration flags apply to `--compile` and project commands:

```bash
sharpts --compile src/library.ts --declaration
sharpts --compile src/library.ts --emitDeclarationOnly --declarationDir types
sharpts -p .
```

`--declaration` emits checked `.d.ts` files; `--emitDeclarationOnly` implies declarations and
suppresses the .NET assembly; `--declarationDir` selects their root. The matching configuration
properties are `declaration`, `emitDeclarationOnly`, `declarationDir`, `rootDir`, and `outDir`.

## Debugging compiled TypeScript

```bash
sharpts --compile app.ts --debug
```

`--debug`/`-g` writes a portable PDB beside the assembly with TypeScript source documents,
checksums, sequence points, scopes, and async mappings. Keep the `.pdb` with the assembly and avoid
moving source files if the debugger does not have source remapping. See
[Debugging compiled TypeScript](debugging-typescript.md).

## Compilation timings

```bash
sharpts --compile app.ts --timings
sharpts --compile app.ts --timings-json > timings.json
```

`--timings` writes a human-readable ordered phase report to stderr. `--timings-json` writes the
report as the only stdout content so it can be piped into tooling. Failures include the phases
reached through the failing phase. The flags are mutually exclusive, cannot be combined with
`--showConfig`, and are not suppressed by `--quiet`.

Conditional phases—such as declaration emission, verification, bundling, runtime/dependency
copying, and packaging—appear only when they execute.

## Parity contract and deviations

For supported language and library features, interpreted and compiled programs should have the
same observable result. Every normal feature test should run in both modes. A backend-specific test
or behavior needs an explicit reason and documentation.

Known contractual deviations include:

- Interpreted `eval` is lexical; compiled `eval` is indirect and cannot see compiled locals.
- Compiled .NET calls currently propagate raw CLR exceptions, while the interpreter maps common
  CLR exceptions to JavaScript-style error names.
- Some Node APIs expose platform or backend ceilings documented in the
  [Node API guide](node-modules-api.md) and [status matrix](../STATUS.md#4-nodejs-built-in-modules).
- Hosted output follows host lifecycle and ABI rules rather than the normal console entry-point
  lifecycle.
- Native AOT uses a closed interop catalog and excludes managed-only tooling; see
  [Native AOT](native-aot.md).

A discrepancy outside a documented deviation is a bug; report the source, mode, platform, and
minimal output difference.
