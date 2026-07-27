# SharpTS

A TypeScript interpreter and ahead-of-time compiler written in C#.

[![NuGet](https://img.shields.io/nuget/v/SharpTS.svg)](https://www.nuget.org/packages/SharpTS)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

## Overview

<img width="2816" height="1536" alt="Gemini_Generated_Image_go1ahqgo1ahqgo1a" src="https://github.com/user-attachments/assets/565d98dc-8268-4cd6-8b34-7cc24a0f7a4a" />

SharpTS is an implementation of a TypeScript interpreter and compiler. It implements the complete pipeline from source code to execution:

1. **Lexical Analysis** - Tokenizing source code
2. **Parsing** - Building an Abstract Syntax Tree
3. **Type Checking** - Static type validation
4. **Execution** - Either interpretation or compilation to .NET IL

SharpTS supports two execution modes:

- **Interpretation** - Tree-walking execution for rapid development
- **AOT Compilation** - Compile TypeScript to native .NET assemblies

## Features

### Language Support

- **Types**: `string`, `number`, `boolean`, `null`, `any`, `void`, `unknown`, `never`
- **Generics**: Generic functions, classes, and interfaces with type constraints
- **Advanced Types**: Union (`|`), intersection (`&`), tuples, literal types
- **Arrays**: Typed arrays with `push`, `pop`, `map`, `filter`, `reduce`, `forEach`, etc.
- **Objects**: Object literals with structural typing
- **Classes**: Constructors, methods, fields, inheritance, `super`, static members
- **Abstract Classes**: Abstract classes and abstract methods
- **Interfaces**: Structural type checking (duck typing)
- **Functions**: First-class functions, arrow functions, closures, default parameters
- **Async/Await**: Full `async`/`await` with `Promise<T>` and combinators (`all`, `race`, `any`)
- **Modules**: ES6 `import`/`export` with default, named, and namespace imports
- **Decorators**: Legacy and TC39 Stage 3 decorators with Reflect metadata API
- **Control Flow**: `if/else`, `while`, `do-while`, `for`, `for...of`, `for...in`, `switch`
- **Error Handling**: `try/catch/finally`, `throw`
- **Operators**: `??`, `?.`, `?:`, `instanceof`, `typeof`, bitwise operators
- **Destructuring**: Array/object destructuring with rest patterns
- **Built-ins**: `console.log`, `Math`, `Date`, `Map`, `Set`, `RegExp`, `bigint`, `Symbol`, string methods

### Compiler Features

- Static type checking with helpful error messages
- Structural typing for interfaces and classes (matching `tsc`; a class branded by a private/protected member is nominal, like `tsc`)
- Compile to standalone executables or .NET assemblies
- Reference assembly output for C# interop (`--ref-asm`)
- IL verification (`--verify`)

### .NET Interop

- **Use .NET types from TypeScript** via `dotnet:` imports (or the `@DotNetType` decorator)
- Access BCL classes like `StringBuilder`, `Guid`, `DateTime`, `TimeSpan`
- **Third-party DLLs and NuGet packages** via a `sharpts.json` manifest (`references` + `packages`)
- Automatic type conversion and overload resolution
- **Compile TypeScript for C# consumption** with reflection or direct reference

## Quick Start

### Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** or later (required)
- **Visual Studio 2026 18.0+** or **Visual Studio Code** (optional, for IDE support)

**Note for MSBuild SDK users:** SharpTS.Sdk requires .NET 10 SDK and uses modern C# features for optimal performance.

### Installation

**Install CLI tool from NuGet (recommended):**

```bash
dotnet tool install -g SharpTS
```

**Or use TypeScript in a .NET project:**

```xml
<Project Sdk="SharpTS.Sdk/1.0.8">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

### Usage

**REPL Mode:**

```bash
sharpts
```

The interactive prompt supports multi-line editing, syntax highlighting, persistent history, and
autocomplete. Suggestions appear as you type — session variables, functions and classes, built-in
globals, and keywords — and typing `.` offers the members of the receiver's type along with each
member's signature. Press `Tab` to accept a suggestion, `Ctrl+Space` to request one, and `Esc` to
dismiss the list. Type `.help` for the dot-commands.

**Run a TypeScript file (interpreted):**

```bash
sharpts script.ts
```

**Compile to .NET assembly:**

```bash
sharpts --compile script.ts
dotnet script.dll
```

**Compile to self-contained executable:**

```bash
sharpts --compile script.ts -t exe
script.exe
```

**Additional compiler options:**

```bash
sharpts --compile script.ts --ref-asm       # Reference assembly for C# interop
sharpts --compile script.ts --verify        # Verify emitted IL
sharpts --compile script.ts --preserveConstEnums  # Keep const enums
```

**Generate NuGet package:**

```bash
sharpts --compile Library.ts --pack         # Creates Library.1.0.0.nupkg
sharpts --compile Library.ts --pack --version 2.0.0-beta  # Custom version
sharpts --compile Library.ts --pack --push https://api.nuget.org/v3/index.json --api-key $KEY
```

Package metadata is read from `package.json` in the source directory.

**Decorator support:**

Decorators are enabled by default (TC39 Stage 3).

```bash
sharpts script.ts                           # Stage 3 decorators (default)
sharpts --experimentalDecorators script.ts  # Legacy (Stage 2) decorators
sharpts --noDecorators script.ts            # Disable decorators
```

**Type-checking strictness:**

The tsc-compatible strictness flags apply in every mode (run, compile, REPL), and accept tsc's
`=false` negation form:

```bash
sharpts --strict script.ts                  # also noImplicitThis + strictPropertyInitialization
sharpts --strictNullChecks=false script.ts  # individual flags override the umbrella
sharpts --noImplicitAny script.ts
sharpts --exactOptionalPropertyTypes script.ts
sharpts --noUncheckedIndexedAccess script.ts
sharpts --noEmit script.ts                  # type-check only; exit 1 on errors
```

SharpTS's defaults are `strictNullChecks: true`, `strictFunctionTypes: false`,
and the other strictness flags off — unchanged unless you ask.

> `noImplicitAny` reports unannotated parameters of **declared** functions, methods and
> constructors. Arrow and function-expression parameters are exempt: SharpTS contextually types
> a callback only for some callee shapes, so reporting there would flag idiomatic code such as
> `promise.then(v => …)`. This under-reports relative to tsc rather than over-reporting.

**tsconfig.json:**

A `tsconfig.json` next to (or above) the entry script is discovered automatically, `extends`
chains included. Command-line flags win over it.

```bash
sharpts -p ./configs/tsconfig.json script.ts  # explicit config (file or directory)
sharpts -p .                                  # check every selected project root
sharpts -p . --watch                          # recheck after source/config changes
sharpts --build                               # check references in dependency order
sharpts --build packages/app --force          # rebuild a reference graph
sharpts --no-tsconfig script.ts               # skip discovery entirely
sharpts --showConfig script.ts                # print the resolved config as JSON, then exit
```

Project commands implement TypeScript's root-selection rules: `files` and `include` are unioned,
`exclude` filters only files found through `include`, and imports can still pull excluded files
into the semantic program. An explicit `files: []` remains empty. JavaScript roots are selected
when `allowJs` or `checkJs` is enabled.

The project model also supports:

- `references` plus `--build`/`-b`, with referenced projects checked first and required to set
  `composite`;
- `incremental`, `composite`, `tsBuildInfoFile`, `--incremental`, `--force`, and `--watch`;
- `baseUrl` and `paths`, including exact mappings, longest-prefix wildcard matching, and target
  fallbacks;
- `moduleResolution` values `classic`, `node`/`node10`, `node16`, `nodenext`, and `bundler`;
- `lib`, `noLib`, `types`, and `typeRoots`, loaded as type-only declaration inputs.

The program resolver loads a pinned TypeScript 5.5.4 `lib.*.d.ts` graph, reachable `.d.ts`,
`.d.mts`, and `.d.cts` modules, package `types`/`typings` and `"types"` exports, `typesVersions`,
and visible `node_modules/@types` packages. `.tsx` files parse in the TSX dialect (JSX commits at
`<`, angle-bracket assertions are rejected, as in tsc) and JSX intrinsic attributes are checked
against `JSX.IntrinsicElements`. The `jsx` family of options (`jsx`, `jsxFactory`,
`jsxFragmentFactory`, `jsxImportSource`) is honored; the default is `react-jsx` — a deliberate
deviation from tsc, which errors without an explicit `--jsx` (restore that behavior with
`--jsx none`). `preserve`/`react-native` are rejected since SharpTS cannot emit .jsx output.

**TypeScript declaration output:**

SharpTS can emit checked `.d.ts` files for TypeScript consumers while continuing to compile
runtime code directly to .NET IL:

```bash
sharpts --compile src/library.ts --declaration
sharpts --compile src/library.ts --emitDeclarationOnly --declarationDir types
sharpts -p .                                  # emits when tsconfig enables declaration
```

The matching `tsconfig.json` options are `declaration`, `emitDeclarationOnly`, and
`declarationDir`. `rootDir` controls the preserved source hierarchy; when `declarationDir` is
absent, `outDir` is used, then the source directory. `.mts` and `.cts` inputs produce `.d.mts`
and `.d.cts`. Project, build, incremental, watch, and MSBuild SDK paths all use the same emitter.
Type-only imports and exports are preserved, implementation bodies are removed, and inferred
public types come from SharpTS's checker. Declaration output is not written when checking fails.

JavaScript output remains an intentional non-goal: SharpTS does not emit JavaScript, bundle or
minify it, or generate JavaScript source maps. Declaration maps are not currently supported.

Reference ordering and the `composite` requirement follow the
[TypeScript project-reference model](https://www.typescriptlang.org/docs/handbook/project-references).

SharpTS build state uses its own `.sharptsbuildinfo` format. If `tsBuildInfoFile` is set, SharpTS
adds a `.sharpts` suffix rather than overwriting tsc's incompatible file. Watch mode currently
re-evaluates the graph after a relevant change and uses that state to skip unchanged projects;
parsed module and package caches are reused within each affected project check.

`-p` without a script and `--build` do not produce runtime assemblies, but emit declarations
when configured. Script and `--compile` commands retain an explicit runtime entry point and use
the loaded project's module-resolution and declaration settings. Project-reference edges
orchestrate semantic checks and build ordering and can emit
each project's `.d.ts` files. They do not yet automatically consume cross-project `.d.ts`/CLR
artifacts, so imports between projects must still resolve through normal `paths` or package
mappings.

JavaScript emit options such as `target`, `module` and `sourceMap` do not apply — SharpTS
compiles to .NET IL, not JavaScript — and are ignored silently; set
`SHARPTS_TSCONFIG_VERBOSE=1` to list them. An unrecognized key is reported with a suggestion.

## Examples

### Hello World

```typescript
// hello.ts
console.log("Hello, World!");
```

```bash
$ sharpts hello.ts
Hello, World!
```

### Classes and Inheritance

```typescript
// animals.ts
class Animal {
    name: string;
    constructor(name: string) {
        this.name = name;
    }
    speak(): string {
        return this.name + " makes a sound";
    }
}

class Dog extends Animal {
    speak(): string {
        return this.name + " barks!";
    }
}

let dog = new Dog("Rex");
console.log(dog.speak());
```

```bash
$ sharpts animals.ts
Rex barks!
```

### Functional Programming

```typescript
// functional.ts
let numbers: number[] = [1, 2, 3, 4, 5];

let doubled = numbers.map((n: number): number => n * 2);
let evens = numbers.filter((n: number): boolean => n % 2 == 0);
let sum = numbers.reduce((acc: number, n: number): number => acc + n, 0);

console.log(doubled);  // [2, 4, 6, 8, 10]
console.log(evens);    // [2, 4]
console.log(sum);      // 15
```

### Compiled Execution

```bash
# Compile to .NET assembly
$ sharpts --compile functional.ts

# Run the compiled assembly
$ dotnet functional.dll
[2, 4, 6, 8, 10]
[2, 4]
15
```

### Use .NET Types from TypeScript

```typescript
// Import BCL types directly — the static type surface comes from reflection
import { StringBuilder } from "dotnet:System.Text.StringBuilder";

let sb = new StringBuilder();
sb.append("Hello from .NET!");
console.log(sb.toString());
```

```bash
$ sharpts --compile dotnet-example.ts
$ dotnet dotnet-example.dll
Hello from .NET!
```

Third-party assemblies and NuGet packages plug in via a `sharpts.json` manifest next to
your script (or a per-invocation `-r ./libs/MyLib.dll` flag):

```jsonc
{
  "references": ["./libs/MyLib.dll"],
  "packages": { "Newtonsoft.Json": "13.0.3" }
}
```

```typescript
import { Widget } from "dotnet:MyLib.Widget";
import { JsonConvert } from "dotnet:Newtonsoft.Json.JsonConvert";
```

See [Using .NET Types](docs/dotnet-types.md) for the full guide (including the manual
`@DotNetType` declaration style and the `--gen-decl` discovery tool).

### Access TypeScript classes from .NET (C#)

```C#
// Compile your TypeScript with --ref-asm:

var person = new Person("Alice", 30.0);  // Direct instantiation
Console.WriteLine(person.name);          // Direct property access
string greeting = person.greet();        // Typed return values
```

[More code examples](Examples/README.md)

## Documentation

- [**Using .NET Types**](docs/dotnet-types.md) - Use .NET BCL and libraries from TypeScript
- [**.NET Integration**](docs/dotnet-integration.md) - Consume compiled TypeScript from C#
- [**MSBuild SDK Guide**](docs/msbuild-sdk.md) - Integrate SharpTS into your .NET build process
- [**Code Samples**](docs/code-samples.md) - Task-oriented snippets across both modes
- [**Execution Modes**](docs/execution-modes.md) - Interpreter vs compiled: differences and trade-offs
- [**Node.js Module API**](docs/node-modules-api.md) - Per-module API coverage reference
- [**Node.js Module Status**](STATUS-NODE.md) - Detailed per-module implementation status
- [**npm Compatibility**](docs/npm-compatibility.md) - Real-package smoke testing
- [**Architecture Guide**](ARCHITECTURE.md) - Deep dive into the compiler/interpreter internals
- [**Contributing Guide**](CONTRIBUTING.md) - How to contribute to the project

## Project Status

SharpTS is under active development. See [STATUS.md](STATUS.md) for current feature support and roadmap.

## Testing

The standard test suite (xUnit, in `SharpTS.Tests/`) runs via:

```bash
dotnet test
```

Two additional **conformance suites** live as standalone projects (not in `SharpTS.sln`, so the standard `dotnet test` doesn't pick them up). They each pin against an external corpus via git submodule:

- **[SharpTS.Test262/](SharpTS.Test262/README.md)** — TC39 ECMA-262 conformance, interpreter + compiled-IL modes.
- **[SharpTS.TypeScriptConformance/](SharpTS.TypeScriptConformance/README.md)** — TypeScript conformance against the canonical `microsoft/TypeScript` test corpus, type-checker only.

Initialize the submodules and invoke the projects explicitly:

```bash
git submodule update --init external/test262 external/typescript
dotnet test SharpTS.Test262/SharpTS.Test262.csproj
dotnet test SharpTS.TypeScriptConformance/SharpTS.TypeScriptConformance.csproj
```

Both suites use a committed-baseline + diff harness — runs hard-fail on regression or new-pass against the committed bucket assignments. Update baselines after intentional changes via `SHARPTS_TEST262_UPDATE_BASELINE=1` or `SHARPTS_TSCONFORMANCE_UPDATE_BASELINE=1`.

> **Windows note:** the `microsoft/TypeScript` repo contains paths exceeding Windows' default 260-char `MAX_PATH`. Set `git config --global core.longpaths true` before initializing the `external/typescript` submodule.

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on:

- Code style guidelines
- How to add new language features
- Submitting pull requests

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
