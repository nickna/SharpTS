# Contributing to SharpTS

Thank you for your interest in contributing to SharpTS! This project is a TypeScript interpreter and compiler written in C#, and we welcome contributions of all kinds.

## Table of Contents

- [Getting Started](#getting-started)
- [How to Contribute](#how-to-contribute)
- [Development Workflow](#development-workflow)
- [Code Style Guidelines](#code-style-guidelines)
- [Adding New Language Features](#adding-new-language-features)
- [Areas Needing Help](#areas-needing-help)

## Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download) or later

### Setup

1. Fork and clone the repository:
   ```bash
   git clone https://github.com/nickna/SharpTS.git
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

Before diving in, read [ARCHITECTURE.md](ARCHITECTURE.md) for the stable subsystem and emitted-runtime
contracts. `CLAUDE.md` contains only tool-specific operating guidance.

## How to Contribute

### Reporting Bugs

1. Check if the bug has already been reported in
   [Issues](https://github.com/nickna/SharpTS/issues)
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

3. Add tests for new functionality in the `tests/SharpTS.Tests/` directory

4. Ensure all tests pass in both interpreter and compiler modes

5. Commit with clear, descriptive messages

6. Push and open a pull request against `main`

## Development Workflow

### Building

```bash
dotnet build
```

Place new shipping projects under `src/`, normal tests under `tests/`, performance suites under
`benchmarks/`, runnable examples under `samples/`, and editor integrations under `extensions/`.
External-corpus conformance projects belong in `tests/conformance/` and stay outside the default
solution test loop unless that policy is changed deliberately.

### Running Tests

Tests are xUnit tests in the `tests/SharpTS.Tests/` directory:

```bash
dotnet test
```

On Windows, the filesystem tests create real symbolic links. Enable Developer
Mode (`ms-settings:developers`) and restart the terminal or IDE that runs the
tests. Running the tests elevated or granting the account the **Create symbolic
links** (`SeCreateSymbolicLinkPrivilege`) user right are alternatives. The tests
fail with setup guidance when this prerequisite is missing; they are not skipped.

### Writing Tests: The Dual-Mode Contract

Almost every feature must behave identically in the interpreter and the IL
compiler, and the test suite enforces that mechanically. The convention (used
by `tests/SharpTS.Tests/SharedTests/`, ~60% of the suite) is a theory parameterized
over both modes:

```csharp
using SharpTS.Tests.Infrastructure;
using Xunit;

public class MyFeatureTests
{
    [Theory, ModeData]   // runs once Interpreted, once Compiled
    public void MyFeature_DoesTheThing(ExecutionMode mode)
    {
        var source = """
            console.log(1 + 1);
            """;
        Assert.Equal("2\n", TestHarness.Run(source, mode));
    }
}
```

- `TestHarness.Run(source, mode)` executes the guest source in the given mode
  and returns captured console output (line endings normalized to `\n`).
- Multi-file programs use `TestHarness.RunModules(files, entryPoint, mode)`.
- `[InterpretedOnlyData]` / `[CompiledOnlyData]` exist for genuinely
  single-mode behavior — use them sparingly and say why in a comment.
- New shared tests belong in `SharedTests/` (or `SharedTests/BuiltInModules/`
  for Node-module surface). `InterpreterTests/` and `CompilerTests/` are only
  for tests of one engine's internals.

Manual verification of both modes:
1. Interpretation: `dotnet run --project src/SharpTS -- file.ts`
2. Compilation: `dotnet run --project src/SharpTS -- --compile file.ts` then `dotnet file.dll`

Two conformance suites (not in `SharpTS.sln`, run explicitly) pin SharpTS
against external corpora: `tests/conformance/SharpTS.Test262/` (ECMA-262) and
`tests/conformance/SharpTS.TypeScriptConformance/` (type checker vs `tsc`). If your change could
affect JS semantics or checker behavior, run the relevant suite — CI compiles
them but does not execute them.

## Code Style Guidelines

### C# Conventions

- **C# Version:** `LangVersion=latest` on .NET 10
- **Nullable Reference Types:** Always enabled
- **Records:** Use for immutable data (AST nodes, type representations)
- **Analyzers:** IDE0051/IDE0052 (unused/unread private members) are build
  warnings — keep the count at zero
- **Comments:** State constraints the code can't show; don't narrate the code

### AST Node Pattern

All AST nodes are immutable records in `Parsing/AST.cs`:

```csharp
public record MyNewExpr(Token Operator, Expr Operand) : Expr;
public record MyNewStmt(Expr Value, Token Keyword) : Stmt;
```

Dispatch is reflection-free (a Native AOT requirement): the node universe is
the explicit, declaration-ordered list in `Parsing/Visitors/AstNodeCatalog.cs`,
and each phase dispatches through a hand-ordered type switch
(`AstVisitorBase`, `Interpreter.Dispatch.cs`, `TypeChecker.Dispatch.cs`).
`tests/SharpTS.Tests/RegistryTests/AstDispatchTests.cs` reflectively re-derives the
true node set and drives every switch — so adding a node without extending the
catalog and each dispatch site fails those tests with a message naming exactly
what to edit.

### Error Messages

Type-checker diagnostics carry a canonical `TSnnnn` code where one exists
(`tsCode:` on `TypeCheckException`); the structured model lives in
`Diagnostics/Diagnostic.cs`. Human-facing prefixes in output are
`"Type Error:"` and `"Runtime Error:"`.

## Adding New Language Features

The parser, type checker, interpreter, and IL compiler are all partial-class
families, not single files. A new feature typically touches, in order:

1. **`Parsing/Token.cs` + `Parsing/Lexer.cs`** — new tokens, if any
2. **`Parsing/AST.cs`** — new record node(s)
3. **`Parsing/Parser.*.cs`** — the parser partials (e.g. `Parser.Expressions.cs`,
   `Parser.Classes.cs`; per-parameter units live in `Parser.Parameters.cs`)
4. **`TypeSystem/TypeChecker.*.cs`** — static checking (`CheckExpr`/`Check`)
5. **`Execution/Interpreter.*.cs`** — runtime behavior (`Evaluate`/`Execute`)
6. **`Compilation/`** — IL emission (`ILEmitter.*.cs` for statement/expression
   codegen; `RuntimeEmitter.*.cs` if the emitted runtime needs new surface).
   Read the standalone-DLL constraint in CLAUDE.md first: compiled output must
   never gain a metadata reference to SharpTS.dll.
7. **`tests/SharpTS.Tests/SharedTests/`** — dual-mode tests (see above)

## Areas Needing Help

Ongoing work is organized under five standing issues (they stay open; new work
attaches to them rather than spawning new epics):

- **#1278 — Performance:** the standing hunt for the next perf gap
- **#1279 — JS conformance:** close the interpreter's Test262 gap to the compiler
- **#1280 — JS conformance:** grow Test262 coverage and mature the harness
- **#1281 — TS conformance:** chip away at type-checker divergence from `tsc`
- **#1282 — Node.js:** expand built-in module coverage (breadth and depth)

Check those issues for a live scoreboard and prioritized checklists. Test
coverage for edge cases and error conditions is always welcome.

---

Thank you for contributing to SharpTS!
