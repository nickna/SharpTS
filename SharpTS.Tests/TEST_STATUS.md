# SharpTS Test Coverage Status

This document tracks the test coverage status for TypeScript language features across both the interpreter and IL compiler execution paths.

## Legend
- ✅ Covered (interpreter + compiler tests passing)
- 🔶 Partial (interpreter only, or incomplete coverage)
- ❌ Not covered

---

## Core Language Features

### Primitives & Variables
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| number literals | ✅ | ✅ | ArithmeticTests, PipelineTests |
| string literals | ✅ | ✅ | PipelineTests, integration tests |
| boolean literals | ✅ | ✅ | ArithmeticTests (logical ops) |
| null | ✅ | ✅ | OperatorTests (nullish) |
| let declarations | ✅ | ✅ | Used throughout all tests |
| type annotations | ✅ | ✅ | Used throughout all tests |

### Operators
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| arithmetic (+, -, *, /, %) | ✅ | ✅ | ArithmeticTests.cs |
| comparison (==, !=, <, >, <=, >=) | ✅ | ✅ | ArithmeticTests.cs |
| logical (&&, \|\|, !) | ✅ | ✅ | ArithmeticTests.cs |
| nullish coalescing (??) | ✅ | ✅ | OperatorTests.cs |
| optional chaining (?.) | ✅ | ✅ | OperatorTests.cs |
| ternary (?:) | ✅ | ✅ | OperatorTests.cs |
| typeof | ✅ | ✅ | ControlFlowTests.cs |
| instanceof | ✅ | ✅ | ControlFlowTests.cs |
| bitwise (&, \|, ^, ~, <<, >>, >>>) | ✅ | ✅ | OperatorTests.cs |
| prefix/postfix (++, --) | ✅ | ✅ | OperatorTests.cs |
| compound assignment (+=, -=, etc.) | ✅ | ✅ | OperatorTests.cs |

### Arrays
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| array literals | ✅ | ✅ | ArrayTests.cs |
| indexing | ✅ | ✅ | ArrayTests.cs |
| .length | ✅ | ✅ | ArrayTests.cs |
| .push() | ✅ | ✅ | ArrayTests.cs |
| .pop() | ✅ | ✅ | ArrayTests.cs |
| .shift() | ✅ | ✅ | ArrayTests.cs |
| .unshift() | ✅ | ✅ | ArrayTests.cs |
| .slice() | ✅ | ✅ | ArrayTests.cs |
| .map() | ✅ | ✅ | ArrayTests.cs, ArrayMethodTests.cs |
| .filter() | ✅ | ✅ | ArrayTests.cs, ArrayMethodTests.cs |
| .forEach() | ✅ | ✅ | ArrayTests.cs |
| .find() | ✅ | ✅ | ArrayMethodTests.cs |
| .findIndex() | ✅ | ✅ | ArrayMethodTests.cs |
| .some() | ✅ | ✅ | ArrayMethodTests.cs |
| .every() | ✅ | ✅ | ArrayMethodTests.cs |
| .reduce() | ✅ | ✅ | ArrayMethodTests.cs |
| .includes() | ✅ | ✅ | ArrayMethodTests.cs |
| .indexOf() | ✅ | ✅ | ArrayMethodTests.cs |
| .join() | ✅ | ✅ | ArrayMethodTests.cs |
| .concat() | ✅ | ✅ | ArrayMethodTests.cs |
| .reverse() | ✅ | ✅ | ArrayMethodTests.cs |

### Objects
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| object literals | ✅ | ✅ | ObjectFeatureTests.cs |
| property access (dot) | ✅ | ✅ | Used throughout tests |
| property access (bracket) | ✅ | ✅ | object_test.ts |
| shorthand properties | ✅ | ✅ | ObjectFeatureTests.cs |
| method shorthand | ✅ | ✅ | ObjectFeatureTests.cs |
| object spread | ✅ | ✅ | phase4_test.ts |
| object rest pattern | ✅ | ✅ | ObjectFeatureTests.cs |
| destructuring | ✅ | ✅ | DestructuringTests.cs |
| Object.keys() | ✅ | ✅ | ObjectFeatureTests.cs |

### Functions
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| function declarations | ✅ | ✅ | PipelineTests.cs |
| arrow functions | ✅ | ✅ | PipelineTests.cs |
| closures | ✅ | ✅ | PipelineTests.cs |
| default parameters | ✅ | ✅ | default_params_test.ts |
| rest parameters | ✅ | ✅ | phase4_test.ts |
| return statements | ✅ | ✅ | Used throughout all tests |

### Classes
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| class declarations | ✅ | ✅ | ClassTests.cs |
| constructors | ✅ | ✅ | ClassTests.cs |
| instance methods | ✅ | ✅ | ClassTests.cs |
| instance fields | ✅ | ✅ | ClassTests.cs |
| inheritance (extends) | ✅ | ✅ | ClassTests.cs |
| super calls | ✅ | ✅ | ClassTests.cs |
| static methods | ✅ | ✅ | StaticMembersTests.cs |
| static fields | ✅ | ✅ | StaticMembersTests.cs |
| getters | ✅ | ✅ | GettersSettersTests.cs |
| setters | ✅ | ✅ | GettersSettersTests.cs |
| private modifier | ✅ | ✅ | AccessModifierTests.cs |
| protected modifier | ✅ | ✅ | AccessModifierTests.cs |
| public modifier | ✅ | ✅ | AccessModifierTests.cs |
| readonly modifier | ✅ | ✅ | AccessModifierTests.cs |

### Interfaces
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| interface declarations | ✅ | ✅ | InterfaceTests.cs |
| implements | ✅ | ✅ | InterfaceTests.cs |
| structural typing | ✅ | ✅ | InterfaceTests.cs |
| optional properties | ✅ | ✅ | InterfaceTests.cs |
| interface methods | ✅ | ✅ | InterfaceTests.cs |
| multiple implements | ✅ | ✅ | InterfaceTests.cs |

### Control Flow
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| if/else | ✅ | ✅ | PipelineTests.cs |
| while | ✅ | ✅ | PipelineTests.cs, ControlFlowTests.cs |
| for | ✅ | ✅ | PipelineTests.cs |
| for...of | ✅ | ✅ | ControlFlowTests.cs |
| switch/case | ✅ | ✅ | ControlFlowTests.cs |
| switch default | ✅ | ✅ | ControlFlowTests.cs |
| switch fall-through | ✅ | ✅ | ControlFlowTests.cs |
| break | ✅ | ✅ | ControlFlowTests.cs |
| continue | ✅ | ✅ | ControlFlowTests.cs |

### Error Handling
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| try/catch | ✅ | ✅ | ErrorHandlingTests.cs |
| finally | ✅ | ✅ | ErrorHandlingTests.cs |
| throw | ✅ | ✅ | ErrorHandlingTests.cs |
| nested try/catch | ✅ | ✅ | ErrorHandlingTests.cs |

### Type System
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| type aliases | ✅ | ✅ | type_alias_test.ts |
| union types | ✅ | ✅ | union_test.ts |
| literal types | ✅ | ✅ | literal_types_test.ts |
| enums (numeric) | ✅ | ✅ | EnumTests.cs |
| enums (string) | ✅ | ✅ | EnumTests.cs |
| enums (heterogeneous) | ✅ | ✅ | EnumTests.cs |
| enum reverse mapping | ✅ | ✅ | EnumTests.cs |
| type assertions (as) | ✅ | ✅ | type_assertion_test.ts |
| type assertions (<T>) | ✅ | ✅ | angle_bracket_assertion_test.ts |
| unknown | ✅ | ✅ | unknown_never_test.ts |
| never | ✅ | ✅ | unknown_never_test.ts |
| tuples | ✅ | ✅ | tuple_test.ts |
| generic functions | ✅ | ✅ | GenericsTests.cs |
| generic classes | ✅ | ✅ | GenericsTests.cs |
| generic interfaces | ✅ | ✅ | GenericsTests.cs |
| type constraints | ✅ | ✅ | GenericsTests.cs |

### Built-ins
| Feature | Interpreter | Compiler | Notes |
|---------|-------------|----------|-------|
| console.log | ✅ | ✅ | Used throughout all tests |
| Math.PI | ✅ | ✅ | MathBuiltInTests.cs |
| Math.E | ✅ | ✅ | MathBuiltInTests.cs |
| Math.abs() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.floor() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.ceil() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.round() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.max() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.min() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.sqrt() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.pow() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.sign() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.trunc() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.sin/cos/tan() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.log/exp() | ✅ | ✅ | MathBuiltInTests.cs |
| Math.random() | ✅ | ✅ | MathBuiltInTests.cs |
| String.length | ✅ | ✅ | StringMethodTests.cs |
| String.charAt() | ✅ | ✅ | StringMethodTests.cs |
| String.substring() | ✅ | ✅ | StringMethodTests.cs |
| String.indexOf() | ✅ | ✅ | StringMethodTests.cs |
| String.toUpperCase() | ✅ | ✅ | StringMethodTests.cs |
| String.toLowerCase() | ✅ | ✅ | StringMethodTests.cs |
| String.trim() | ✅ | ✅ | StringMethodTests.cs |
| String.split() | ✅ | ✅ | StringMethodTests.cs |
| String.replace() | ✅ | ✅ | StringMethodTests.cs |
| String.includes() | ✅ | ✅ | StringMethodTests.cs |
| String.startsWith() | ✅ | ✅ | StringMethodTests.cs |
| String.endsWith() | ✅ | ✅ | StringMethodTests.cs |
| template literals | ✅ | ✅ | TemplateLiteralTests.cs |

---

## Summary

| Category | Features | Covered | Partial | Not Covered |
|----------|----------|---------|---------|-------------|
| Primitives & Variables | 6 | 6 | 0 | 0 |
| Operators | 11 | 11 | 0 | 0 |
| Arrays | 21 | 21 | 0 | 0 |
| Objects | 9 | 9 | 0 | 0 |
| Functions | 6 | 6 | 0 | 0 |
| Classes | 14 | 14 | 0 | 0 |
| Interfaces | 6 | 6 | 0 | 0 |
| Control Flow | 9 | 9 | 0 | 0 |
| Error Handling | 4 | 4 | 0 | 0 |
| Type System | 17 | 17 | 0 | 0 |
| Built-ins | 30 | 30 | 0 | 0 |
| **Total** | **133** | **133** | **0** | **0** |

---

## Test Files

### Unit Tests (SharpTS.Tests/)

#### InterpreterTests/
| Test File | Features Tested |
|-----------|-----------------|
| ArithmeticTests.cs | arithmetic, comparison, logical, unary operators |
| ArrayTests.cs | array literals, indexing, length, push, pop, shift, unshift, slice, map, filter, forEach |
| ArrayMethodTests.cs | find, findIndex, some, every, reduce, includes, indexOf, join, concat, reverse |
| ClassTests.cs | class declarations, constructors, fields, methods, inheritance, super |
| ControlFlowTests.cs | switch, for-of, typeof, instanceof, break, continue |
| DestructuringTests.cs | array/object destructuring, rest patterns, renaming, defaults |
| EnumTests.cs | numeric enums, string enums, heterogeneous, reverse mapping |
| ErrorHandlingTests.cs | try/catch, finally, throw, nested try/catch |
| GenericsTests.cs | generic functions, classes, interfaces, type constraints |
| GettersSettersTests.cs | getter/setter accessors, computed properties |
| InterfaceTests.cs | interface declarations, implements, structural typing, optional props |
| AccessModifierTests.cs | private, protected, public, readonly modifiers |
| MathBuiltInTests.cs | Math object constants and methods |
| ObjectFeatureTests.cs | shorthand properties, method shorthand, rest pattern, Object.keys |
| OperatorTests.cs | bitwise, nullish coalescing, optional chaining, ternary, increment/decrement, compound assignment |
| StaticMembersTests.cs | static fields and methods |
| StringMethodTests.cs | string length, charAt, substring, indexOf, case conversion, trim, split, replace |
| TemplateLiteralTests.cs | template strings with interpolation |

#### CompilerTests/
| Test File | Features Tested |
|-----------|-----------------|
| ArithmeticTests.cs | arithmetic, comparison, logical, unary operators |
| ArrayTests.cs | array literals, indexing, length, push, pop, shift, unshift, slice, map, filter, forEach |
| ArrayMethodTests.cs | find, findIndex, some, every, reduce, includes, indexOf, join, concat, reverse |
| ClassTests.cs | class declarations, constructors, fields, methods, inheritance, super |
| ControlFlowTests.cs | switch, for-of, typeof, instanceof, break, continue |
| DestructuringTests.cs | array/object destructuring, rest patterns, renaming, defaults |
| EnumTests.cs | numeric enums, string enums, heterogeneous, reverse mapping |
| ErrorHandlingTests.cs | try/catch, finally, throw, nested try/catch |
| GenericsTests.cs | generic functions, classes, interfaces, type constraints |
| GettersSettersTests.cs | getter/setter accessors, computed properties |
| InterfaceTests.cs | interface declarations, implements, structural typing, optional props |
| AccessModifierTests.cs | private, protected, public, readonly modifiers |
| MathBuiltInTests.cs | Math object constants and methods |
| ObjectFeatureTests.cs | shorthand properties, method shorthand, rest pattern, Object.keys |
| OperatorTests.cs | bitwise, nullish coalescing, optional chaining, ternary, increment/decrement, compound assignment |
| StaticMembersTests.cs | static fields and methods |
| StringMethodTests.cs | string length, charAt, substring, indexOf, case conversion, trim, split, replace |
| TemplateLiteralTests.cs | template strings with interpolation |

#### Pipeline Tests
| Test File | Features Tested |
|-----------|-----------------|
| PipelineTests.cs | interpreter/compiler parity for all major features |

---

## Test Count Summary

| Category | Test Count |
|----------|------------|
| InterpreterTests | ~185 |
| CompilerTests | ~185 |
| PipelineTests | 18 |
| **Total** | **~388** |
