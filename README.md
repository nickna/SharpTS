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
- Nominal typing for classes, structural typing for interfaces
- Compile to standalone .NET executables
- Reference assembly output for C# interop (`--ref-asm`)
- IL verification (`--verify`)

### .NET Interop

- **Use .NET types from TypeScript** via `@DotNetType` decorator
- Access BCL classes like `StringBuilder`, `Guid`, `DateTime`, `TimeSpan`
- Automatic type conversion and overload resolution
- **Compile TypeScript for C# consumption** with reflection or direct reference

## Quick Start

### Prerequisites

- **[.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)** or later (required)
- **Visual Studio 2026 18.0+** (optional, for IDE support)

**Note for MSBuild SDK users:** SharpTS.Sdk requires .NET 10 SDK and uses modern C# features for optimal performance.

### Installation

**Install CLI tool from NuGet (recommended):**
```bash
dotnet tool install -g SharpTS
```

**Or use the MSBuild SDK in your project:**
```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

**Or build from source:**
```bash
git clone https://github.com/nickna/SharpTS.git
cd SharpTS
dotnet build
```

### Usage

**REPL Mode:**
```bash
sharpts
```

**Run a TypeScript file (interpreted):**
```bash
sharpts script.ts
```

**Compile to .NET assembly:**
```bash
sharpts --compile script.ts
dotnet script.dll
```

**Compile with custom output:**
```bash
sharpts --compile script.ts -o myapp.dll
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
```bash
sharpts --experimentalDecorators script.ts  # Legacy (Stage 2) decorators
sharpts --decorators script.ts              # TC39 Stage 3 decorators
```

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
// Use BCL types directly in TypeScript
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    append(value: string): StringBuilder;
    toString(): string;
}

let sb = new StringBuilder();
sb.append("Hello from .NET!");
console.log(sb.toString());
```

### Access TypeScript classes from .NET (C#)
```C#
// Compile your TypeScript with --ref-asm:

var person = new Person("Alice", 30.0);  // Direct instantiation
Console.WriteLine(person.name);          // Direct property access
string greeting = person.greet();        // Typed return values
```
[Example code](Examples/Interop/README.md)
## Documentation

- [**Using .NET Types**](docs/dotnet-types.md) - Use .NET BCL and libraries from TypeScript
- [**.NET Integration**](docs/dotnet-integration.md) - Consume compiled TypeScript from C#
- [**MSBuild SDK Guide**](docs/msbuild-sdk.md) - Integrate SharpTS into your .NET build process
- [**Architecture Guide**](ARCHITECTURE.md) - Deep dive into the compiler/interpreter internals
- [**Contributing Guide**](CONTRIBUTING.md) - How to contribute to the project

## Project Status

SharpTS is under active development. See [STATUS.md](STATUS.md) for current feature support and roadmap.

**Looking for help with:**
- Additional TypeScript features
- IL compiler feature parity
- Performance optimizations
- Test coverage

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on:
- Code style guidelines
- How to add new language features
- Submitting pull requests

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
