# SharpTS

SharpTS is a TypeScript interpreter and ahead-of-time compiler written in C# for .NET 10. It
parses and type-checks TypeScript directly, then either executes the syntax tree or emits .NET IL;
there is no JavaScript transpilation step.

[![NuGet](https://img.shields.io/nuget/v/SharpTS.svg)](https://www.nuget.org/packages/SharpTS)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

## Install

Install the command-line tool from NuGet:

```bash
dotnet tool install --global SharpTS
```

Self-contained managed and Native AOT binaries are also published on
[GitHub Releases](https://github.com/nickna/SharpTS/releases). See the
[Native AOT guide](docs/native-aot.md) before choosing the native build because it deliberately
has a smaller interop and tooling surface.

## Interpret TypeScript

Create `hello.ts`:

```typescript
interface Greeting {
  name: string;
}

const greeting: Greeting = { name: "SharpTS" };
console.log(`Hello, ${greeting.name}!`);
```

Run it directly:

```bash
sharpts hello.ts
```

Running `sharpts` without a file starts the interactive REPL.

## Compile to .NET

Compile the same program to a runnable .NET assembly:

```bash
sharpts --compile hello.ts -o hello.dll
dotnet hello.dll
```

Use `--target exe` for an executable target, or the `SharpTS.Sdk` MSBuild SDK for repeatable .NET
project builds. Compiled output embeds the runtime it needs and only copies `SharpTS.dll` when an
emitted soft-dependent feature requires it.

Allocation-heavy services can opt into adaptive server GC with
`--gc-profile adaptive`; workstation GC remains the conservative default. See the
[GC profile decision](benchmarks/gc-profiles/decision.md) for measured tradeoffs.

## Major feature areas

- TypeScript syntax, type checking, modules, projects, declarations, JSX/TSX, async functions,
  decorators, and JavaScript built-ins
- Interpreter and compiled-IL execution with a documented parity contract
- Node-compatible built-in modules and real npm package loading
- `.NET` imports, external assemblies and NuGet references, C# consumption, and embedding APIs
- MSBuild SDK integration, interpreted and compiled TypeScript-source debugging, and a standalone
  language server
- Retained Avalonia desktop applications written in TypeScript/TSX
- Managed self-contained and Native AOT distribution options

The current capability matrix and known gaps are in [STATUS.md](STATUS.md).

## Documentation

Start with the [documentation hub](docs/README.md) for task-oriented guides. Runnable examples are
collected in the [samples cookbook](samples/README.md). Contributors should read
[CONTRIBUTING.md](CONTRIBUTING.md) and [ARCHITECTURE.md](ARCHITECTURE.md).

SharpTS is licensed under the [MIT License](LICENSE).
