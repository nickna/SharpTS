# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SharpTS is a TypeScript interpreter and compiler implemented in C# using .NET 10.0. It supports both tree-walking interpretation and ahead-of-time compilation to .NET IL.

## Build and Run Commands

### Build the project
```bash
dotnet build
```

### Run REPL mode
```bash
dotnet run
```

### Execute a TypeScript file (interpreted)
```bash
dotnet run -- <filename>.ts
```

### Compile to .NET IL (ahead-of-time)
```bash
dotnet run -- --compile <filename>.ts           # Outputs <filename>.dll
dotnet run -- --compile <filename>.ts -o out.dll  # Custom output path
dotnet run -- --compile <filename>.ts --pack    # Generate NuGet package
```

### Run compiled output
```bash
dotnet <filename>.dll
```

### Run tests
The project uses xUnit tests in the `SharpTS.Tests/` directory:
```bash
dotnet test
```

## Architecture

SharpTS follows a traditional compiler/interpreter pipeline with a critical separation between compile-time and runtime phases:

### Directory Structure

```
SharpTS/
├── Parsing/                    # Frontend pipeline (namespace: SharpTS.Parsing)
│   ├── Token.cs                # Token types and definitions
│   ├── Lexer.cs                # Lexical analyzer
│   ├── Parser.cs               # Recursive descent parser
│   └── AST.cs                  # Expr and Stmt record types
│
├── TypeSystem/                 # Static type analysis (namespace: SharpTS.TypeSystem)
│   ├── TypeInfo.cs             # Type representations
│   ├── TypeChecker.cs          # Static type validator
│   └── TypeEnvironment.cs      # Type scope management
│
├── Runtime/                    # Runtime values and infrastructure
│   ├── RuntimeEnvironment.cs   # Variable scope management (namespace: SharpTS.Runtime)
│   ├── Types/                  # Runtime value types (namespace: SharpTS.Runtime.Types)
│   │   ├── SharpTSArray.cs
│   │   ├── SharpTSClass.cs
│   │   ├── SharpTSEnum.cs
│   │   ├── SharpTSFunction.cs
│   │   ├── SharpTSInstance.cs
│   │   ├── SharpTSMath.cs
│   │   └── SharpTSObject.cs
│   ├── BuiltIns/               # Built-in methods (namespace: SharpTS.Runtime.BuiltIns)
│   │   ├── ArrayBuiltIns.cs
│   │   ├── StringBuiltIns.cs
│   │   ├── ObjectBuiltIns.cs
│   │   ├── MathBuiltIns.cs
│   │   ├── BuiltInTypes.cs
│   │   └── BuiltInMethod.cs
│   └── Exceptions/             # Control flow exceptions (namespace: SharpTS.Runtime.Exceptions)
│       ├── ReturnException.cs
│       ├── BreakException.cs
│       ├── ContinueException.cs
│       └── ThrowException.cs
│
├── Execution/                  # Tree-walking interpreter (namespace: SharpTS.Execution)
│   ├── Interpreter.cs          # Core infrastructure
│   ├── Interpreter.Statements.cs
│   ├── Interpreter.Expressions.cs
│   ├── Interpreter.Properties.cs
│   ├── Interpreter.Calls.cs
│   └── Interpreter.Operators.cs
│
├── Compilation/                # IL compilation (namespace: SharpTS.Compilation)
│   ├── ILCompiler.cs           # Main orchestrator
│   ├── ILEmitter.cs            # IL instruction emission (+ partial files)
│   ├── ClosureAnalyzer.cs      # Closure detection
│   ├── CompilationContext.cs   # Compilation state
│   ├── RuntimeTypes.cs         # Runtime type emission
│   ├── RuntimeEmitter.cs       # Runtime code emission
│   ├── LocalsManager.cs        # Local variable tracking
│   ├── TypeMapper.cs           # TypeScript-to-.NET type mapping
│   └── EmittedRuntime.cs       # Emitted runtime references
│
├── Packaging/                  # NuGet package generation (namespace: SharpTS.Packaging)
│   ├── AssemblyMetadata.cs     # Assembly version and attributes
│   ├── AssemblyAttributeBuilder.cs # Build assembly-level attributes
│   ├── PackageJson.cs          # package.json model
│   ├── PackageJsonLoader.cs    # package.json parser
│   ├── NuGetPackager.cs        # .nupkg generation
│   ├── SymbolPackager.cs       # .snupkg generation
│   ├── PackageValidator.cs     # Pre-packaging validation
│   └── NuGetPublisher.cs       # Push to NuGet feeds
│
├── SharpTS.Tests/              # xUnit test project
├── Program.cs                  # Entry point
└── SharpTS.csproj
```

### Pipeline Phases

1. **Lexical Analysis** (`Parsing/Lexer.cs`)
   - Tokenizes source code into `Token` objects
   - Produces a flat stream for parsing

2. **Syntax Analysis** (`Parsing/Parser.cs`)
   - Recursive descent parser
   - Builds Abstract Syntax Tree (AST) from tokens
   - Performs "desugaring" (e.g., `for` loops become `while` loops)
   - AST nodes defined in `Parsing/AST.cs` with expression (`Expr`) and statement (`Stmt`) records

3. **Static Type Checking** (`TypeSystem/TypeChecker.cs`) - **Separate compile-time phase**
   - Runs BEFORE interpretation or compilation
   - Validates type compatibility (nominal and structural)
   - Checks function signatures, inheritance safety
   - Uses `TypeSystem/TypeEnvironment.cs` for type scopes
   - Type representations in `TypeSystem/TypeInfo.cs` (Primitive, Function, Class, Interface, Instance, Array, Record, Void, Any)

4. **Runtime Interpretation** (`Execution/Interpreter*.cs`) - One execution path
   - Tree-walking interpreter split across partial class files
   - Executes validated AST
   - Uses `Runtime/RuntimeEnvironment.cs` for variable scopes
   - Runtime types in `Runtime/Types/`: SharpTSClass, SharpTSInstance, SharpTSFunction, SharpTSArray, SharpTSObject

5. **IL Compilation** (`Compilation/` directory) - Alternative execution path
   - `ILCompiler.cs`: Main orchestrator, multi-phase compilation
   - `ILEmitter.cs`: Emits IL instructions for statements and expressions
   - `ClosureAnalyzer.cs`: Detects captured variables for closure support
   - `CompilationContext.cs`: Tracks locals, parameters, and compilation state
   - Uses `System.Reflection.Emit` with `PersistedAssemblyBuilder` to generate .NET assemblies

### Critical Architecture Notes

**Two-Environment System:**
- `TypeEnvironment`: Tracks types during static analysis
- `RuntimeEnvironment`: Tracks values during execution
- These are completely separate - never mix them

**Type System Design:**
- Supports **structural typing** for interfaces (duck typing)
- Supports **nominal typing** for classes (inheritance-based)
- Type checking happens at compile-time; runtime uses dynamic object model
- `TypeInfo` records represent types statically
- Runtime objects (`SharpTSInstance`, etc.) are independent of `TypeInfo`

**Control Flow via Exceptions:** (`Runtime/Exceptions/`)
- `ReturnException.cs`: Unwinding the call stack on `return` statements
- `BreakException.cs`: Breaking out of loops and switch statements
- `ContinueException.cs`: Continuing to next loop iteration
- `ThrowException.cs`: User-thrown exceptions in try/catch
- This is intentional - exceptions as control flow mechanism

**Entry Point:**
- `Program.cs` orchestrates the pipeline: Lex → Parse → TypeCheck → (Interpret OR Compile)
- Errors in type checking prevent execution or compilation from running
- The `--compile` flag switches from interpretation to IL compilation

## Language Features

- **Primitives:** `string`, `number`, `boolean`, `null`
- **Arrays:** Homogeneous typed arrays (`number[]`) with built-in methods (push, pop, map, filter, etc.)
- **Objects:** JSON-style object literals with structural typing
- **Classes:** Constructors, methods, fields, inheritance (`extends`), `super` calls
- **Interfaces:** Structural type checking (shape-based compatibility)
- **Functions:** First-class functions, arrow functions with closures, default parameters
- **Control Flow:** `if/else`, `while`, `for`, `for...of`, `switch`, `break`, `continue`
- **Error Handling:** `try/catch/finally`, `throw`
- **Operators:** Nullish coalescing (`??`), optional chaining (`?.`), ternary (`?:`), template literals
- **Built-ins:** `console.log`, `Math` object (constants and methods), string methods

## Development Patterns

### AST Node Pattern
- All AST nodes are discriminated unions using C# records
- Expression nodes inherit from `Expr`
- Statement nodes inherit from `Stmt`
- Use pattern matching (`switch` expressions) to traverse

### Visitor-Style Traversal
- `TypeChecker.Check()` and `TypeChecker.CheckExpr()` for static analysis
- `Interpreter.Execute()` and `Interpreter.Evaluate()` for runtime
- `ILEmitter.EmitStatement()` and `ILEmitter.EmitExpression()` for IL compilation
- All use switch pattern matching on AST node types

### Error Handling
- Type errors throw exceptions with "Type Error:" prefix
- Runtime errors throw exceptions with "Runtime Error:" prefix
- Main loop in `Program.cs` catches and displays errors

## Code Conventions

- **C# Version:** C# 12/13 with .NET 10 preview features
- **Nullable Reference Types:** Enabled (`<Nullable>enable</Nullable>`)
- **Records:** Heavily used for immutable AST nodes and type representations
- **Primary Constructors:** Used in `Parser`, `RuntimeEnvironment`, `TypeEnvironment`

## Important Implementation Details

- **For Loop Desugaring:** Parser converts `for` loops into `while` loops during parsing
- **console.log Special Case:** Handled as a hardcoded special case in type checker, interpreter, and compiler
- **Constructor Validation:** Type checker validates constructor signatures during class instantiation
- **Method Lookup:** Searches up the inheritance chain for methods (see `TypeSystem/TypeChecker.cs` CheckGet and `Execution/Interpreter.Properties.cs` EvaluateGet)
- **Closure Compilation:** Arrow functions use display classes for captured variables; non-capturing arrows compile to static methods
- **IL Compilation Phases:** ILCompiler runs in 9 phases - emit runtime types, analyze closures, define classes/functions, collect arrow functions, emit arrow bodies, emit class methods, emit entry point, finalize types
