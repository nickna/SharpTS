# Contributing to SharpTS

Thank you for your interest in contributing to SharpTS! This project is a TypeScript interpreter and compiler written in C#, and we welcome contributions of all kinds.

## Table of Contents

- [Getting Started](#getting-started)
- [How to Contribute](#how-to-contribute)
- [Development Workflow](#development-workflow)
- [Code Style Guidelines](#code-style-guidelines)
- [Adding New Language Features](#adding-new-language-features)
- [Areas Needing Help](#areas-needing-help)
- [Code of Conduct](#code-of-conduct)

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later

### Setup

1. Fork and clone the repository:
   ```bash
   git clone https://github.com/YOUR_USERNAME/SharpTS.git
   cd SharpTS
   ```

2. Build the project:
   ```bash
   dotnet build
   ```

3. Run the REPL to verify everything works:
   ```bash
   dotnet run
   ```

### Understanding the Codebase

Before diving in, we recommend reading [ARCHITECTURE.md](ARCHITECTURE.md) which explains:
- The compiler/interpreter pipeline
- How the type system works
- Key design patterns used throughout

## How to Contribute

### Reporting Bugs

1. Check if the bug has already been reported in [Issues](../../issues)
2. Create a new issue with:
   - A clear, descriptive title
   - Steps to reproduce the bug
   - Expected vs actual behavior
   - A minimal TypeScript code example that triggers the bug
   - Whether it affects interpretation, compilation, or both

### Suggesting Features

1. Open an issue to discuss the feature before implementing
2. Describe the TypeScript feature or improvement you'd like to add
3. Include examples of the syntax and expected behavior

### Submitting Pull Requests

1. Create a feature branch from `main`:
   ```bash
   git checkout -b feature/your-feature-name
   ```

2. Make your changes following the [code style guidelines](#code-style-guidelines)

3. Add tests for new functionality in the `SharpTS.Tests/` directory

4. Ensure all tests pass in both interpreter and compiler modes

5. Commit with clear, descriptive messages:
   ```
   Add support for [feature]

   - Updated Lexer.cs to handle new tokens
   - Added AST nodes in AST.cs
   - Implemented type checking in TypeChecker.cs
   - etc.
   ```

6. Push and open a pull request against `main`

## Development Workflow

### Building

```bash
dotnet build
```

### Running Tests

Tests are xUnit tests in the `SharpTS.Tests/` directory:

```bash
dotnet test
```

**Important:** When adding or modifying features, verify they work in BOTH modes:
1. Interpretation (`dotnet run -- file.ts`)
2. Compilation (`dotnet run -- --compile file.ts` then `dotnet file.dll`)

### Creating Test Files

Add new test classes to `SharpTS.Tests/` following the existing patterns (e.g., `InterpreterTests/`, `CompilerTests/`).

## Code Style Guidelines

### C# Conventions

- **C# Version:** 12/13 with .NET 10 features
- **Nullable Reference Types:** Always enabled
- **Records:** Use for immutable data (AST nodes, type representations)
- **Primary Constructors:** Preferred for simple classes

### AST Node Pattern

All AST nodes should be immutable records:

```csharp
// In AST.cs
public record MyNewExpr(Token Operator, Expr Operand) : Expr;
public record MyNewStmt(Expr Value, Token Keyword) : Stmt;
```

### Visitor Pattern

Use switch expressions for AST traversal:

```csharp
return expr switch
{
    Expr.MyNewExpr e => HandleMyNewExpr(e),
    // ... other cases
    _ => throw new Exception($"Unknown expression type: {expr.GetType()}")
};
```

### Error Messages

Use consistent prefixes:
- Type errors: `throw new Exception("Type Error: message");`
- Runtime errors: `throw new Exception("Runtime Error: message");`

## Adding New Language Features

When adding a new TypeScript feature, you typically need to modify these files in order:

### 1. Token.cs
Add new token types if needed:
```csharp
public enum TokenType
{
    // ...
    MY_NEW_TOKEN,
}
```

### 2. Lexer.cs
Handle tokenization of new syntax in `ScanToken()`.

### 3. AST.cs
Add new expression/statement record types.

### 4. Parser.cs
Parse the new syntax and build AST nodes.

### 5. TypeChecker.cs
Add type checking logic in `CheckExpr()` or `CheckStmt()`.

### 6. Interpreter.cs
Implement runtime behavior in `Evaluate()` or `Execute()`.

### 7. Compilation/ILEmitter.cs
Generate IL instructions in `EmitExpression()` or `EmitStatement()`.

### 8. SharpTS.Tests/
Add a test class demonstrating the feature.

### Example: Adding a New Operator

1. Add token: `TokenType.MY_OP`
2. Lexer: Recognize the operator characters
3. AST: Reuse `Expr.Binary` or create new node
4. Parser: Add to appropriate precedence level
5. TypeChecker: Validate operand types
6. Interpreter: Implement the operation
7. ILEmitter: Emit equivalent IL
8. Test: Add tests to `SharpTS.Tests/`

## Areas Needing Help

We especially welcome contributions in these areas:

### TypeScript Features
- Generics (`<T>`)
- Enums
- Modules and imports
- Decorators
- Union and intersection types
- Type guards
- Async/await

### IL Compiler Parity
- Ensure all interpreter features work when compiled
- Fix any behavioral differences between modes
- Improve generated IL efficiency

### Performance
- Lexer/parser optimization
- Interpreter evaluation speed
- Compiled code performance

### Test Coverage
- Add tests for edge cases
- Add tests for error conditions
- Improve existing test comprehensiveness

## Code of Conduct

This project follows the [Contributor Covenant Code of Conduct](CODE_OF_CONDUCT.md). By participating, you are expected to uphold this code. Please report unacceptable behavior to the project maintainers.

---

Thank you for contributing to SharpTS!
