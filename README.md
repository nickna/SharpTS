# SharpTS

A TypeScript interpreter and ahead-of-time compiler written in C#.

[![NuGet](https://img.shields.io/nuget/v/SharpTS.svg)](https://www.nuget.org/packages/SharpTS)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com/)

## Overview

SharpTS is an educational implementation of a TypeScript interpreter and compiler. It demonstrates how programming languages work by implementing the complete pipeline from source code to execution:

1. **Lexical Analysis** - Tokenizing source code
2. **Parsing** - Building an Abstract Syntax Tree
3. **Type Checking** - Static type validation
4. **Execution** - Either interpretation or compilation to .NET IL

SharpTS supports two execution modes:
- **Interpretation** - Tree-walking execution for rapid development
- **AOT Compilation** - Compile TypeScript to native .NET assemblies

## Features

### Language Support

- **Types**: `string`, `number`, `boolean`, `null`, `any`, `void`
- **Arrays**: Typed arrays with `push`, `pop`, `map`, `filter`, `reduce`, `forEach`, etc.
- **Objects**: Object literals with structural typing
- **Classes**: Constructors, methods, fields, inheritance, `super`, static members
- **Interfaces**: Structural type checking (duck typing)
- **Functions**: First-class functions, arrow functions, closures, default parameters
- **Control Flow**: `if/else`, `while`, `do-while`, `for`, `for...of`, `switch`
- **Error Handling**: `try/catch/finally`, `throw`
- **Operators**: `??`, `?.`, `?:`, `instanceof`, `typeof`, bitwise operators
- **Built-ins**: `console.log`, `Math` object, string methods

### Compiler Features

- Static type checking with helpful error messages
- Nominal typing for classes, structural typing for interfaces
- Compile to standalone .NET executables

## Quick Start

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later

### Installation

**Install from NuGet (recommended):**
```bash
dotnet tool install -g SharpTS
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

## Documentation

- [**Architecture Guide**](ARCHITECTURE.md) - Deep dive into the compiler/interpreter internals
- [**Contributing Guide**](CONTRIBUTING.md) - How to contribute to the project

## Project Status

SharpTS is under active development. See [STATUS.md](STATUS.md) for current feature support and roadmap.

**Looking for help with:**
- Additional TypeScript features (generics, enums, modules)
- IL compiler feature parity
- Performance optimizations
- Test coverage

## Contributing

Contributions are welcome! Please read our [Contributing Guide](CONTRIBUTING.md) for details on:
- Setting up your development environment
- Code style guidelines
- How to add new language features
- Submitting pull requests

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
