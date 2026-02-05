# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

SharpTS is a TypeScript interpreter and compiler implemented in C# using .NET 10.0. It supports both tree-walking interpretation and ahead-of-time compilation to .NET IL.

## Build and Run Commands

```bash
dotnet build                              # Build the project
dotnet test                               # Run xUnit tests
dotnet test --filter "FullyQualifiedName~SomeTest"  # Run specific test
dotnet run                                # Start REPL mode
dotnet run -- script.ts                   # Interpret a TypeScript file
dotnet run -- --compile script.ts         # Compile to .NET assembly
dotnet run -- --compile script.ts -o out.dll  # Custom output path
dotnet run -- --compile script.ts --verify    # Verify emitted IL
```

## Architecture

### Pipeline Flow

```
Source → Lexer → Parser → TypeChecker → Interpreter (tree-walk)
                                     ↘ ILCompiler (AOT to .NET IL)
```

### Directory Structure

| Directory | Purpose |
|-----------|---------|
| `Parsing/` | Lexer, Parser (14 partial files), AST node definitions |
| `TypeSystem/` | Static type checking (47 files), type compatibility, generics |
| `Execution/` | Tree-walking interpreter (16 files) |
| `Compilation/` | IL compilation (316 files), async state machines, bundling |
| `Runtime/` | Runtime values, environment, built-ins |
| `Runtime/Types/` | TypeScript value types (87 files): arrays, classes, promises, etc. |
| `Runtime/BuiltIns/` | Built-in method implementations (41 files) |
| `Runtime/Exceptions/` | Control flow exceptions (return, break, continue, yield) |
| `Modules/` | Module resolution, script detection |
| `Diagnostics/` | Error reporting, source locations |
| `Packaging/` | NuGet package generation |
| `Cli/` | Command-line argument parsing |
| `Declaration/` | TypeScript declaration generation from .NET types |
| `LspBridge/` | Language Server Protocol bridge for IDE integration |
| `SharpTS.Tests/` | xUnit test project |

### Critical Architecture Patterns

**Two-Environment System:**
- `TypeEnvironment` - Tracks types during static analysis (compile-time)
- `RuntimeEnvironment` - Tracks values during execution (runtime)
- Never mix these - they serve completely different phases

**Control Flow via Exceptions:**
- `ReturnException`, `BreakException`, `ContinueException`, `YieldException`
- This is intentional - exceptions as control flow mechanism for unwinding

**Type System:**
- Structural typing for interfaces (duck typing)
- Nominal typing for classes (inheritance-based)
- `TypeInfo` records represent types statically; runtime objects are independent

### RuntimeValue Boxing Elimination (Active Optimization)

The codebase is migrating from `object?` to `RuntimeValue` struct to eliminate boxing:

**RuntimeValue** (`Runtime/RuntimeValue.cs`):
- 24-byte discriminated union storing primitives inline
- `ValueKind` enum: Undefined, Null, Boolean, Number, String, Object, Symbol, BigInt
- Factory methods: `FromNumber()`, `FromString()`, `FromBoolean()`, `FromObject()`, `FromBoxed()`
- Accessors: `AsNumber()`, `AsString()`, `AsBoolean()`, `AsObject<T>()`
- JavaScript semantics: `IsTruthy()`, `TypeofString()`

**ISharpTSCallableV2** (`Runtime/Types/ISharpTSCallableV2.cs`):
- New callable interface using `RuntimeValue` instead of `object?`
- `CallableV2Adapter` wraps legacy → V2, `CallableLegacyAdapter` wraps V2 → legacy
- Extension methods: `.AsV2()`, `.AsLegacy()`, `.CallWithRuntimeValues()`

**BuiltInMethod** (`Runtime/BuiltIns/BuiltInMethod.cs`):
- Implements both `ISharpTSCallable` and `ISharpTSCallableV2`
- `CreateV2()` factory for RuntimeValue-based methods
- Thread-local pooling in array built-ins to avoid allocations

### Visitor-Style Traversal Pattern

All major phases use switch pattern matching on AST node types:
- `TypeChecker.Check()` / `CheckExpr()` - static analysis
- `Interpreter.Execute()` / `Evaluate()` - runtime
- `ILEmitter.EmitStatement()` / `EmitExpression()` - IL compilation

### AST Nodes

All AST nodes are C# records in `Parsing/AST.cs`:
- Expression nodes inherit from `Expr`
- Statement nodes inherit from `Stmt`
- Use pattern matching to traverse

### IL Compilation Phases

ILCompiler runs in multiple phases:
1. Emit runtime types
2. Analyze closures
3. Define classes/functions
4. Collect arrow functions
5. Emit arrow bodies
6. Emit class methods
7. Emit entry point
8. Finalize types

Arrow functions use display classes for captured variables; non-capturing arrows compile to static methods.

### Error Handling Conventions

- Type errors: "Type Error:" prefix
- Runtime errors: "Runtime Error:" prefix
- Compile errors: "Compile Error:" prefix

## Important Implementation Details

- **For Loop Desugaring:** Parser converts `for` loops into `while` loops
- **console.log:** Hardcoded special case in type checker, interpreter, and compiler
- **Inner function declarations:** Not supported in IL compiler - use arrow functions instead
- **Method Lookup:** Searches up inheritance chain (see `TypeChecker.cs` CheckGet, `Interpreter.Properties.cs` EvaluateGet)

## See Also

- `STATUS.md` - Feature implementation status and known bugs
- `README.md` - User documentation and examples
