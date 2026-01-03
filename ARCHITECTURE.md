# SharpTS Architecture

SharpTS is a TypeScript interpreter and compiler implemented in C# (.NET 10). It supports two execution modes:

1. **Interpretation** - Tree-walking execution of TypeScript code
2. **AOT Compilation** - Ahead-of-time compilation to .NET IL assemblies

This document explains how the compiler and interpreter work internally.

---

## Pipeline Overview

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Source Code                                     │
│                         (TypeScript .ts file)                               │
└─────────────────────────────────────┬───────────────────────────────────────┘
                                      │
                                      ▼
                            ┌──────────────────┐
                            │      Lexer       │
                            │   (Lexer.cs)     │
                            └────────┬─────────┘
                                     │
                                     ▼
                            ┌──────────────────┐
                            │   Token Stream   │
                            │  List<Token>     │
                            └────────┬─────────┘
                                     │
                                     ▼
                            ┌──────────────────┐
                            │     Parser       │
                            │   (Parser.cs)    │
                            └────────┬─────────┘
                                     │
                                     ▼
                            ┌──────────────────┐
                            │       AST        │
                            │   List<Stmt>     │
                            └────────┬─────────┘
                                     │
                                     ▼
                            ┌──────────────────┐
                            │   TypeChecker    │
                            │ (TypeChecker.cs) │
                            └────────┬─────────┘
                                     │
                    ┌────────────────┴────────────────┐
                    │                                 │
                    ▼                                 ▼
          ┌──────────────────┐              ┌──────────────────┐
          │   Interpreter    │              │    ILCompiler    │
          │ (Interpreter.cs) │              │  (ILCompiler.cs) │
          └────────┬─────────┘              └────────┬─────────┘
                   │                                 │
                   ▼                                 ▼
          ┌──────────────────┐              ┌──────────────────┐
          │  Execute Result  │              │   .NET Assembly  │
          │                  │              │     (.dll)       │
          └──────────────────┘              └──────────────────┘
```

**Entry Point**: `Program.cs` orchestrates the pipeline based on command-line arguments:
- No arguments → REPL mode
- `<file>.ts` → Interpret file
- `--compile <file>.ts` → Compile to .NET assembly

---

## Frontend Components

### Lexer (`Lexer.cs`, `Token.cs`)

The lexer performs single-pass tokenization, converting source text into a stream of tokens.

**Token Types** (58 total):
- Keywords: `class`, `function`, `const`, `let`, `if`, `while`, `for`, `return`, etc.
- Operators: `+`, `-`, `*`, `/`, `==`, `===`, `&&`, `||`, `?.`, `??`, etc.
- Literals: `NUMBER`, `STRING`, `IDENTIFIER`
- Template literals: `TEMPLATE_HEAD`, `TEMPLATE_MIDDLE`, `TEMPLATE_TAIL`

**Example**:
```
Input:  const x = 42;
Output: [CONST, IDENTIFIER("x"), EQUAL, NUMBER(42), SEMICOLON, EOF]
```

### Parser (`Parser.cs`)

A recursive descent parser that builds an Abstract Syntax Tree from tokens.

**Key Features**:
- Precedence climbing for expressions
- Desugaring: `for` loops → `while` loops during parsing
- Destructuring: `let [a, b] = arr` → temporary variable assignments
- Arrow function detection with backtracking

**Expression Precedence** (lowest to highest):
```
Assignment → Ternary → NullishCoalescing → Or → And → BitwiseOr →
BitwiseXor → BitwiseAnd → Equality → Comparison → Shift → Term →
Factor → Exponentiation → Unary → Call → Primary
```

### AST (`AST.cs`)

Immutable C# records representing the syntax tree.

**Expressions** (`Expr`):
```
Binary, Unary, Ternary, Logical          // Operators
Variable, Get, GetIndex, Set, SetIndex   // Data access
Literal, ArrayLiteral, ObjectLiteral     // Literals
Call, New, ArrowFunction                 // Functions
This, Super, Assign, CompoundAssign      // Special
```

**Statements** (`Stmt`):
```
Var, Function, Class, Interface, TypeAlias  // Declarations
If, While, DoWhile, ForOf, Switch           // Control flow
Block, Return, Break, Continue              // Structure
TryCatch, Throw                             // Errors
```

---

## Type System

SharpTS performs **static type checking before execution**. Type errors prevent code from running.

```
┌─────────────────────────────────────────────────────────────────┐
│                    COMPILE-TIME (Types)                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                 │
│  TypeEnvironment                    TypeInfo                    │
│  ┌────────────────┐                ┌─────────────────────────┐  │
│  │ "x" → number   │                │ Primitive(TYPE_NUMBER)  │  │
│  │ "Dog" → Class  │                │ Class("Dog", ...)       │  │
│  │ "add" → Func   │                │ Function([num], num)    │  │
│  └────────────────┘                └─────────────────────────┘  │
│                                                                 │
│  TypeChecker.Check() validates type compatibility               │
│                                                                 │
└─────────────────────────────────────────────────────────────────┘
```

### TypeInfo (`TypeInfo.cs`)

Abstract record hierarchy representing types:

| Type | Description |
|------|-------------|
| `Primitive` | `string`, `number`, `boolean` |
| `Function` | Parameter types, return type, required param count |
| `Class` | Methods, static members, superclass chain |
| `Interface` | Member shapes for structural typing |
| `Instance` | Reference to a class type |
| `Array` | Element type |
| `Record` | Object literal shape `{key: type}` |
| `Void`, `Any` | Special types |

### TypeChecker (`TypeChecker.cs`)

Validates type correctness using two typing strategies:

- **Nominal typing** for classes: Inheritance chain must match
- **Structural typing** for interfaces: Shape must match (duck typing)

```typescript
interface Walkable { walk(): void }
class Dog { walk(): void { } }

let w: Walkable = new Dog();  // OK - Dog has walk() method
```

### TypeEnvironment (`TypeEnvironment.cs`)

Scoped symbol table for type information. Supports nested scopes via `Enclosing` property.

---

## Runtime System

### Two-Environment Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                    COMPILE-TIME                                 │
│  TypeEnvironment stores TypeInfo records                        │
│  "What type is this variable?"                                  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│                    RUNTIME                                      │
│  RuntimeEnvironment stores actual object? values                │
│  "What value does this variable hold?"                          │
└─────────────────────────────────────────────────────────────────┘
```

These environments are **completely separate**. Type checking happens first; runtime never sees type information.

### Interpreter (`Interpreter.cs`)

Tree-walking interpreter that executes validated AST:

- `Execute(Stmt)` - Execute statements
- `Evaluate(Expr)` - Evaluate expressions to values
- `ExecuteBlock(stmts, env)` - Execute with scoped environment

### RuntimeEnvironment (`RuntimeEnvironment.cs`)

Scoped symbol table for runtime values:
- `Define(name, value)` - Create variable binding
- `Get(name)` - Retrieve value
- `Assign(name, value)` - Update existing binding

### Runtime Objects

| Class | Purpose |
|-------|---------|
| `SharpTSClass` | Class metadata, methods, static members |
| `SharpTSInstance` | Object instance with field dictionary |
| `SharpTSFunction` | Function with closure environment |
| `SharpTSArray` | Array with built-in methods |
| `SharpTSObject` | Object literal |
| `SharpTSMath` | Math singleton |

### Control Flow via Exceptions

The interpreter uses exceptions for control flow unwinding:
- `ReturnException` - Return from function
- `BreakException` - Break from loop/switch
- `ContinueException` - Continue to next iteration
- `ThrowException` - User-thrown errors

---

## IL Compilation

The IL compiler translates TypeScript to .NET assemblies as an alternative to interpretation.

### 9-Phase Pipeline

```
┌──────────────────────────────────────────────────────────────────────────┐
│ Phase 1: Emit Runtime Types                                              │
│   Create TSFunction, RuntimeTypes helper classes                         │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 2: Closure Analysis                                                │
│   Identify captured variables in arrow functions                         │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 3: Create $Program Type                                            │
│   Container for top-level code and static methods                        │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 4: Define Classes & Functions                                      │
│   Create TypeBuilder stubs for all declarations                          │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 5: Collect Arrow Functions                                         │
│   Register arrows, create display classes for closures                   │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 6: Emit Arrow Bodies                                               │
│   Generate IL for arrow function implementations                         │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 7: Emit Class Methods                                              │
│   Generate IL for all method bodies                                      │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 8: Emit Entry Point                                                │
│   Generate Main() with top-level statements                              │
├──────────────────────────────────────────────────────────────────────────┤
│ Phase 9: Finalize Types                                                  │
│   Call CreateType() on all builders, produce assembly                    │
└──────────────────────────────────────────────────────────────────────────┘
```

### Key Components

**ILCompiler** (`Compilation/ILCompiler.cs`)
- Main orchestrator running the 9 phases
- Manages TypeBuilder instances
- Coordinates closure handling

**ILEmitter** (`Compilation/ILEmitter.cs`)
- Emits IL instructions for statements and expressions
- Handles special cases: `console.log`, `Math.*`, array methods
- Manages boxing/unboxing for value types

**ClosureAnalyzer** (`Compilation/ClosureAnalyzer.cs`)
- Walks AST to find captured variables
- Determines which arrows need display classes

**RuntimeTypes** (`Compilation/RuntimeTypes.cs`)
- Emits helper types into the assembly
- `TSFunction`: Wraps method references
- `RuntimeTypes`: 50+ helper methods for TypeScript semantics

### Closure Compilation

**Non-capturing arrow** → Static method on `$Program`:
```csharp
static object? <>Arrow_0(object? x) { return x + 1; }
```

**Capturing arrow** → Display class with captured fields:
```csharp
class <>c__DisplayClass0 {
    public object? capturedVar;
    public object? Invoke(object? arg) { return capturedVar + arg; }
}
```

---

## Key Architectural Patterns

### 1. Two-Environment Separation

Types and values never mix:
- `TypeEnvironment` + `TypeInfo` = compile-time
- `RuntimeEnvironment` + `object?` = runtime

### 2. Discriminated Unions

AST nodes use C# records with pattern matching:
```csharp
var result = expr switch {
    Expr.Binary b => HandleBinary(b),
    Expr.Call c => HandleCall(c),
    Expr.Variable v => HandleVariable(v),
    _ => throw new Exception("Unknown")
};
```

### 3. Visitor-Style Traversal

All phases use switch-based visitors on AST nodes:
- `TypeChecker.Check()` / `CheckExpr()`
- `Interpreter.Execute()` / `Evaluate()`
- `ILEmitter.EmitStatement()` / `EmitExpression()`

### 4. Exception-Based Control Flow

Return, break, and continue use exceptions for stack unwinding rather than complex state tracking.

---

## File Reference

### Core Pipeline
| File | Purpose |
|------|---------|
| `Program.cs` | Entry point, orchestrates pipeline |
| `Parsing/Lexer.cs` | Tokenization |
| `Parsing/Token.cs` | Token types and representation |
| `Parsing/Parser.cs` | Recursive descent parser |
| `Parsing/AST.cs` | AST node definitions |
| `TypeSystem/TypeChecker.cs` | Static type analysis |
| `TypeSystem/TypeInfo.cs` | Type representations |
| `TypeSystem/TypeEnvironment.cs` | Compile-time symbol table |
| `Execution/Interpreter.cs` | Tree-walking execution (see partial classes below) |
| `Runtime/RuntimeEnvironment.cs` | Runtime symbol table |

### Runtime Objects
| File | Purpose |
|------|---------|
| `Runtime/Types/SharpTSClass.cs` | Class metadata and methods |
| `Runtime/Types/SharpTSInstance.cs` | Object instances |
| `Runtime/Types/SharpTSFunction.cs` | Callable functions with closures |
| `Runtime/Types/SharpTSArray.cs` | Array implementation |
| `Runtime/Types/SharpTSObject.cs` | Object literal implementation |
| `Runtime/Types/SharpTSMath.cs` | Math object singleton |
| `Runtime/Types/SharpTSEnum.cs` | Enum implementation |

### Built-ins
| File | Purpose |
|------|---------|
| `Runtime/BuiltIns/ArrayBuiltIns.cs` | Array methods (push, pop, map, filter, etc.) |
| `Runtime/BuiltIns/StringBuiltIns.cs` | String methods (charAt, substring, etc.) |
| `Runtime/BuiltIns/ObjectBuiltIns.cs` | Object methods (keys, values, entries, etc.) |
| `Runtime/BuiltIns/MathBuiltIns.cs` | Math functions (sin, cos, sqrt, etc.) |
| `Runtime/BuiltIns/BuiltInMethod.cs` | Base class for built-in methods |
| `Runtime/BuiltIns/BuiltInTypes.cs` | Built-in type definitions |

### Control Flow
| File | Purpose |
|------|---------|
| `Runtime/Exceptions/ReturnException.cs` | Return statement unwinding |
| `Runtime/Exceptions/BreakException.cs` | Break statement unwinding |
| `Runtime/Exceptions/ContinueException.cs` | Continue statement unwinding |
| `Runtime/Exceptions/ThrowException.cs` | User-thrown exceptions |

### IL Compilation
| File | Purpose |
|------|---------|
| `Compilation/ILCompiler.cs` | Main compilation orchestrator |
| `Compilation/ILEmitter.cs` | IL instruction emission (see partial classes below) |
| `Compilation/ClosureAnalyzer.cs` | Captured variable detection |
| `Compilation/CompilationContext.cs` | Compilation state management |
| `Compilation/RuntimeTypes.cs` | Runtime support type emission |
| `Compilation/RuntimeEmitter.cs` | Runtime helper method emission |
| `Compilation/EmittedRuntime.cs` | References to emitted runtime helpers |
| `Compilation/TypeMapper.cs` | TypeScript → .NET type mapping |
| `Compilation/LocalsManager.cs` | Local variable tracking |

### Partial Class Organization

Large classes are split across multiple files for maintainability:

**Interpreter** (`Execution/`):
| File | Purpose |
|------|---------|
| `Interpreter.cs` | Core infrastructure and setup |
| `Interpreter.Statements.cs` | Statement execution (if, while, for, etc.) |
| `Interpreter.Expressions.cs` | Expression evaluation |
| `Interpreter.Properties.cs` | Property and member access |
| `Interpreter.Calls.cs` | Function and method calls |
| `Interpreter.Operators.cs` | Operator evaluation |

**ILEmitter** (`Compilation/`):
| File | Purpose |
|------|---------|
| `ILEmitter.cs` | Core IL emission infrastructure |
| `ILEmitter.Statements.cs` | Statement IL emission |
| `ILEmitter.Expressions.cs` | Expression IL emission |
| `ILEmitter.Properties.cs` | Property access IL emission |
| `ILEmitter.Calls.cs` | Function call IL emission |
| `ILEmitter.Operators.cs` | Operator IL emission |

---

## Language Features

SharpTS supports a substantial subset of TypeScript:

- **Primitives**: `string`, `number`, `boolean`, `null`
- **Arrays**: `number[]` with push, pop, map, filter, reduce, etc.
- **Objects**: `{ key: value }` with structural typing
- **Classes**: Constructors, methods, fields, inheritance, `super`
- **Interfaces**: Structural type checking
- **Functions**: First-class, arrow functions, closures, defaults
- **Control Flow**: if/else, while, do-while, for, for-of, switch
- **Error Handling**: try/catch/finally, throw
- **Operators**: `??`, `?.`, `?:`, `instanceof`, `typeof`, bitwise ops
- **Built-ins**: console.log, Math object, string methods
