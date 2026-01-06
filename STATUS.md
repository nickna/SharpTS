# SharpTS Implementation Status

This document tracks TypeScript language features and their implementation status in SharpTS.

**Last Updated:** 2026-01-05 (Date object: Full constructor support, getters, setters, conversion methods, Date.now(), local timezone handling)

## Legend
- ✅ Implemented
- ❌ Missing
- ⚠️ Partially Implemented

---

## 1. TYPE SYSTEM

| Feature | Status | Notes |
|---------|--------|-------|
| Primitive types (`string`, `number`, `boolean`, `null`) | ✅ | |
| `void` type | ✅ | |
| `any` type | ✅ | |
| Array types (`T[]`) | ✅ | |
| Object types | ✅ | Structural typing |
| Interfaces | ✅ | Structural typing |
| Classes | ✅ | Nominal typing |
| Generics (`<T>`) | ✅ | Full support with true .NET generics and constraints |
| Union Types (`string \| number`) | ✅ | With type narrowing support |
| Intersection Types (`A & B`) | ✅ | For combining types with full TypeScript semantics |
| Literal Types (`"success" \| "error"`) | ✅ | String, number, and boolean literals |
| Type Aliases (`type Name = ...`) | ✅ | Including function types |
| Tuple Types (`[string, number]`) | ✅ | Fixed-length typed arrays with optional and rest elements |
| `unknown` type | ✅ | Safer alternative to `any` |
| `never` type | ✅ | For exhaustive checking |
| Type Assertions (`as`, `<Type>`) | ✅ | Both `as` and angle-bracket syntax |
| Type Guards (`is`, `typeof` narrowing) | ✅ | `typeof` narrowing in if-statements |
| `readonly` modifier | ✅ | Compile-time enforcement |
| Optional Properties (`prop?:`) | ✅ | Partial object shapes |
| Index Signatures (`[key: string]: T`) | ✅ | String, number, and symbol key types |

---

## 2. ENUMS

| Feature | Status | Notes |
|---------|--------|-------|
| Numeric Enums | ✅ | `enum Color { Red, Green }` with auto-increment |
| String Enums | ✅ | `enum Color { Red = "RED" }` |
| Const Enums | ✅ | Compile-time inlined enums with computed value support |
| Heterogeneous Enums | ✅ | Mixed string/number values |

---

## 3. CLASSES

| Feature | Status | Notes |
|---------|--------|-------|
| Basic classes | ✅ | Constructors, methods, fields |
| Inheritance (`extends`) | ✅ | Single inheritance |
| `super` calls | ✅ | |
| `this` keyword | ✅ | |
| Access modifiers (`public`/`private`/`protected`) | ✅ | Compile-time enforcement |
| `static` members | ✅ | Class-level properties/methods |
| `abstract` classes | ✅ | Cannot be instantiated |
| `abstract` methods | ✅ | Must be overridden, includes abstract accessors |
| Getters/Setters (`get`/`set`) | ✅ | Property accessors |
| Parameter properties | ✅ | `constructor(public x: number)` |
| `implements` keyword | ✅ | Class implementing interface |
| Method overloading | ✅ | Multiple signatures with implementation function |
| `override` keyword | ✅ | Explicit override marker for methods/accessors |

---

## 4. FUNCTIONS

| Feature | Status | Notes |
|---------|--------|-------|
| Function declarations | ✅ | |
| Arrow functions | ✅ | |
| Closures | ✅ | Variable capture works |
| Default parameters | ✅ | `(x = 5)` |
| Type annotations | ✅ | Parameters and return types |
| Rest parameters (`...args`) | ✅ | Variable arguments |
| Spread in calls (`fn(...arr)`) | ✅ | Array expansion |
| Overloads | ✅ | Multiple signatures with implementation function |
| `this` parameter typing | ✅ | Explicit `this` type in function declarations |
| Generic functions | ✅ | `function identity<T>(x: T)` with type inference |

---

## 5. ASYNC/PROMISES

| Feature | Status | Notes |
|---------|--------|-------|
| Promises | ✅ | `Promise<T>` type with await support |
| `async` functions | ✅ | Full state machine compilation |
| `await` keyword | ✅ | Pause and resume via .NET Task infrastructure |
| Async arrow functions | ✅ | Including nested async arrows |
| Async class methods | ✅ | Full `this` capture support |
| Try/catch in async | ✅ | Await inside try/catch/finally blocks |
| Nested await in args | ✅ | `await fn(await getValue())` |
| `Promise.all/race/any/allSettled` | ✅ | Full interpreter support; IL compiler: all/race/allSettled as pure IL state machines, any delegates to runtime |

---

## 6. MODULES

| Feature | Status | Notes |
|---------|--------|-------|
| `import` statements | ✅ | `import { x } from './file'` |
| `export` statements | ✅ | `export function/class/const` |
| Default exports | ✅ | `export default` |
| Namespace imports | ✅ | `import * as X from './file'` |
| Re-exports | ✅ | `export { x } from './file'`, `export * from './file'` |
| TypeScript namespaces | ❌ | `namespace X { }` |
| Dynamic imports | ❌ | `await import('./file')` |

---

## 7. OPERATORS

| Feature | Status | Notes |
|---------|--------|-------|
| Arithmetic (`+`, `-`, `*`, `/`, `%`) | ✅ | |
| Comparison (`==`, `!=`, `<`, `>`, `<=`, `>=`) | ✅ | |
| Logical (`&&`, `\|\|`, `!`) | ✅ | Short-circuit evaluation |
| Nullish coalescing (`??`) | ✅ | |
| Optional chaining (`?.`) | ✅ | |
| Ternary (`? :`) | ✅ | |
| `typeof` | ✅ | |
| Assignment (`=`, `+=`, `-=`, `*=`, `/=`, `%=`) | ✅ | |
| Increment/Decrement (`++`, `--`) | ✅ | Pre and post |
| Bitwise (`&`, `\|`, `^`, `~`, `<<`, `>>`, `>>>`) | ✅ | Including compound assignments |
| Strict equality (`===`, `!==`) | ✅ | Same behavior as `==`/`!=` |
| `instanceof` | ✅ | With inheritance chain support |
| `in` operator | ✅ | Property existence check |
| Exponentiation (`**`) | ✅ | Right-associative |
| Spread operator (`...`) | ✅ | In arrays/objects/calls |

---

## 8. DESTRUCTURING

| Feature | Status | Notes |
|---------|--------|-------|
| Array destructuring | ✅ | `const [a, b] = arr` |
| Object destructuring | ✅ | `const { x, y } = obj` |
| Nested destructuring | ✅ | Deep pattern matching |
| Default values in destructuring | ✅ | `const { x = 5 } = obj` (via nullish coalescing) |
| Array rest pattern | ✅ | `const [first, ...rest] = arr` |
| Object rest pattern | ✅ | `const { x, ...rest } = obj` |
| Array holes | ✅ | `const [a, , c] = arr` |
| Object rename | ✅ | `const { x: newName } = obj` |
| Parameter destructuring | ✅ | `function f({ x, y })` and `([a, b]) => ...` |

---

## 9. CONTROL FLOW

| Feature | Status | Notes |
|---------|--------|-------|
| `if`/`else` | ✅ | |
| `while` loops | ✅ | |
| `for` loops | ✅ | Desugared to while |
| `for...of` loops | ✅ | Array iteration |
| `switch`/`case` | ✅ | With fall-through |
| `break` | ✅ | |
| `continue` | ✅ | |
| `return` | ✅ | |
| `try`/`catch`/`finally` | ✅ | |
| `throw` | ✅ | |
| `for...in` loops | ✅ | Object key iteration |
| `do...while` loops | ✅ | Post-condition loop |
| Label statements | ✅ | `label: for (...)` with break/continue support |

---

## 10. BUILT-IN APIS

| Feature | Status | Notes |
|---------|--------|-------|
| `console.log` | ✅ | Multiple arguments |
| `Math` object | ✅ | PI, E, abs, floor, ceil, round, sqrt, sin, cos, tan, log, exp, sign, trunc, pow, min, max, random |
| String methods | ✅ | length, charAt, substring, indexOf, toUpperCase, toLowerCase, trim, replace, split, includes, startsWith, endsWith, slice, repeat, padStart, padEnd, charCodeAt, concat, lastIndexOf, trimStart, trimEnd, replaceAll, at |
| Array methods | ✅ | push, pop, shift, unshift, reverse, slice, concat, map, filter, forEach, find, findIndex, some, every, reduce, includes, indexOf, join |
| `JSON.parse`/`stringify` | ❌ | |
| `Object.keys`/`values`/`entries` | ✅ | Full support for object literals and class instances |
| `Array.isArray` | ✅ | Type guard for array detection |
| `Number` methods | ✅ | parseInt, parseFloat, isNaN, isFinite, isInteger, isSafeInteger, toFixed, toPrecision, toExponential, toString(radix); constants: MAX_VALUE, MIN_VALUE, NaN, POSITIVE_INFINITY, NEGATIVE_INFINITY, MAX_SAFE_INTEGER, MIN_SAFE_INTEGER, EPSILON |
| `Date` object | ✅ | Full local timezone support with constructors, getters, setters, conversion methods |
| `RegExp` | ❌ | |
| `Map`/`Set` | ❌ | |

---

## 11. SYNTAX

| Feature | Status | Notes |
|---------|--------|-------|
| Line comments (`//`) | ✅ | |
| Double-quoted strings | ✅ | |
| Template literals | ✅ | With interpolation |
| Object literals | ✅ | |
| Array literals | ✅ | |
| Block comments (`/* */`) | ✅ | |
| Single-quoted strings | ✅ | |
| Object method shorthand | ✅ | `{ fn() {} }` |
| Computed property names | ❌ | `{ [expr]: value }` |
| Class expressions | ❌ | `const C = class { }` |
| Shorthand properties | ✅ | `{ x }` instead of `{ x: x }` |

---

## 12. ADVANCED FEATURES

| Feature | Status | Notes |
|---------|--------|-------|
| Decorators (`@decorator`) | ❌ | Class/method metadata |
| Generators (`function*`) | ❌ | Iterable generators |
| Iterators (Symbol.iterator) | ❌ | Custom iteration |
| Symbols | ✅ | Unique identifiers via `Symbol()` constructor |
| `bigint` type | ✅ | Arbitrary precision integers with full operation support |
| Mapped types | ❌ | `{ [K in keyof T]: ... }` |
| Conditional types | ❌ | `T extends U ? X : Y` |
| Template literal types | ❌ | `` `${string}ID` `` |
| Utility types | ❌ | `Partial<T>`, `Required<T>`, etc. |

---

## Known Bugs

### IL Compiler Bugs

_No known bugs at this time._

---

### Phase 1 Features (Completed)
- ✅ Bitwise operators (`&`, `|`, `^`, `~`, `<<`, `>>`, `>>>`) with compound assignments
- ✅ `instanceof` operator with inheritance support
- ✅ Access modifiers (`private`, `protected`, `public`, `readonly`)
- ✅ Type aliases with function type support

### Phase 2 Features (Completed)
- ✅ Optional properties (`prop?:`) for interfaces and classes
- ✅ Getters/Setters (`get`/`set`) for property accessors
- ✅ Fixed IL compilation bug with chained numeric operations

### Phase 3 Features (Completed)
- ✅ Array destructuring with parser desugaring
- ✅ Object destructuring with rename syntax
- ✅ Nested destructuring patterns
- ✅ Default values in destructuring (via nullish coalescing)
- ✅ Array rest pattern (`...rest`) using slice()
- ✅ Array holes in patterns
- ✅ Added `...` (DOT_DOT_DOT) token support

### Phase 3b Features (Completed)
- ✅ Object rest pattern (`const { x, ...rest } = obj`)
- ✅ Object.keys() built-in for object iteration
- ✅ Parameter destructuring in functions (`function f({ x, y })`)
- ✅ Parameter destructuring in arrow functions (`({ x }) => ...`)

### Phase 4 Features (Completed)
- ✅ Parameter properties (`constructor(public x: number, private y: string)`)
- ✅ All access modifiers supported: `public`, `private`, `protected`, `readonly`
- ✅ `for...in` loops for iterating over object keys
- ✅ Rest parameters (`function sum(...nums: number[])`)
- ✅ Spread in function calls (`fn(...arr)`)
- ✅ Spread in array literals (`[...a, ...b, 5]`)
- ✅ Spread in object literals (`{...base, x: 100}`)
- ✅ Full IL compiler support for all new features

### Phase 5 Features (Completed)
- ✅ Union Types (`string | number | boolean`)
- ✅ Parentheses grouping for array-of-union (`(string | number)[]`)
- ✅ Null as union member (`string | null`)
- ✅ Type narrowing with `typeof` in if-statements
- ✅ Union-to-union compatibility checking
- ✅ Nullish coalescing (`??`) removes null from union types
- ✅ Mixed-type array literals infer union element types

### Phase 6 Features (Completed)
- ✅ Numeric Enums with auto-increment (`enum Direction { Up, Down }`)
- ✅ String Enums (`enum Status { Success = "SUCCESS" }`)
- ✅ Heterogeneous Enums (mixed string and numeric values)
- ✅ Enum reverse mapping for numeric members (`Direction[0]` → `"Up"`)
- ✅ Full IL compiler support for all enum types

### Phase 7 Features (Completed)
- ✅ `implements` keyword for class implementing interface (was already working, undocumented)
- ✅ String Literal Types (`type Status = "success" | "error"`)
- ✅ Number Literal Types (`type Digit = 0 | 1 | 2 | 3`)
- ✅ Boolean Literal Types (`true | false`)
- ✅ Literal to primitive widening compatibility
- ✅ Type Assertions with `as` syntax (`value as string`)
- ✅ Full interpreter and IL compiler support

### Phase 8 Features (Completed)
- ✅ `unknown` type (safer alternative to `any`)
- ✅ `never` type (bottom type for exhaustive checking)
- ✅ Type narrowing for `unknown` via `typeof` checks
- ✅ Angle-bracket type assertion syntax (`<Type>expr`)
- ✅ Full interpreter and IL compiler support

### Phase 9 Features (Completed)
- ✅ Tuple Types (`[string, number, boolean]`)
- ✅ Optional tuple elements (`[string, number?]`)
- ✅ Rest elements in tuples (`[string, ...number[]]`)
- ✅ Contextual typing for array literals assigned to tuple types
- ✅ Tuple indexing with position-based type inference
- ✅ Tuple destructuring support
- ✅ Tuple methods (length, slice, etc.)
- ✅ Tuple compatibility with arrays

### Phase 10 Features (Completed)
- ✅ Generic functions (`function identity<T>(x: T): T`)
- ✅ Multiple type parameters (`function pair<T, U>(a: T, b: U)`)
- ✅ Type argument inference from arguments
- ✅ Explicit type arguments (`identity<number>(42)`)
- ✅ Constraint support (`<T extends Base>`)
- ✅ Type substitution and instantiation
- ✅ TypeInfo records: TypeParameter, GenericFunction, GenericClass, GenericInterface, InstantiatedGeneric
- ✅ Generic classes (`class Box<T> { value: T; }`)
- ✅ Type parameter scoping in class/interface body
- ✅ Constructor parameter type substitution
- ✅ Field and method access with type substitution
- ✅ Generic interfaces (`interface Container<T> { value: T; }`)
- ✅ Generic type annotations in variable declarations (`let x: Container<number>`)
- ✅ Structural compatibility with substituted interface members
- ✅ IL compilation for generic classes/interfaces

### Phase 11 Features (Completed)
- ✅ True .NET generics using `GenericTypeParameterBuilder` and `DefineGenericParameters()`
- ✅ Generic function IL compilation with `MakeGenericMethod()`
- ✅ Generic class IL compilation with `MakeGenericType()` and `TypeBuilder.GetConstructor()`
- ✅ Type constraint enforcement in IL (`SetBaseTypeConstraint`, `SetInterfaceConstraints`)
- ✅ Constraint-aware type inference fallback (uses constraint type instead of object)
- ✅ Generic type parameter tracking in `CompilationContext`
- ✅ Output parity between interpreter and compiled modes verified

### Phase 12 Features (Intersection Types)
- ✅ Intersection Types (`A & B`) for combining multiple types
- ✅ Primitive intersections produce `never` (`string & number = never`)
- ✅ Object type merging with property combination
- ✅ Property conflict detection (conflicting types become `never`)
- ✅ Correct precedence: `&` binds tighter than `|` (`A | B & C` = `A | (B & C)`)
- ✅ Special type rules: `never & T = never`, `any & T = any`, `unknown & T = T`
- ✅ Type alias support for intersection types
- ✅ Full interpreter and IL compiler support

### Phase 13 Features (Index Signatures & Symbols)
- ✅ Index Signatures with string key type (`[key: string]: T`)
- ✅ Index Signatures with number key type (`[key: number]: T`)
- ✅ Index Signatures with symbol key type (`[key: symbol]: T`)
- ✅ Property compatibility validation with index signature types
- ✅ Inline object type index signatures (`let obj: { [k: string]: number }`)
- ✅ Symbol type (`symbol`) as a first-class type
- ✅ Symbol constructor (`Symbol()` and `Symbol("description")`)
- ✅ Symbol uniqueness (each `Symbol()` call creates a unique symbol)
- ✅ `typeof` returns `"symbol"` for Symbol values
- ✅ Symbols as object keys
- ✅ Class instance bracket notation (`instance["fieldName"]`)
- ✅ Full interpreter and IL compiler support for all index signature and symbol features

### Phase 14 Features (Const Enums)
- ✅ Const enum declaration syntax (`const enum Direction { Up, Down }`)
- ✅ Compile-time inlining of const enum values
- ✅ Computed const enum member values (`B = A * 2`)
- ✅ Support for arithmetic, bitwise, and string concatenation in computed values
- ✅ Auto-increment for const enum members without explicit values
- ✅ String const enums (`const enum Status { Success = "success" }`)
- ✅ Type error when attempting reverse mapping on const enums
- ✅ `--preserveConstEnums` compiler flag for debugging
- ✅ Full interpreter and IL compiler support

### Phase 15 Features (Abstract Classes)
- ✅ Abstract class declaration (`abstract class Shape { }`)
- ✅ Abstract methods with semicolon-only syntax (`abstract area(): number;`)
- ✅ Abstract getters (`abstract get name(): string;`)
- ✅ Abstract setters (`abstract set value(v: number);`)
- ✅ Type error when instantiating abstract class
- ✅ Type error when non-abstract class fails to implement abstract members
- ✅ Inheritance chain validation (all abstract members from all ancestors must be implemented)
- ✅ Abstract class can extend another abstract class without implementing
- ✅ Concrete methods in abstract classes
- ✅ Polymorphic behavior with abstract class as parameter type
- ✅ No-op callable for super() when parent has no constructor
- ✅ Full interpreter and IL compiler support with 20 test cases

### Phase 16 Features (String Methods)
- ✅ Complete string method support (23 methods total)
- ✅ Fixed missing type signatures for `includes`, `startsWith`, `endsWith`
- ✅ New methods: `slice`, `repeat`, `padStart`, `padEnd`, `charCodeAt`, `concat`, `lastIndexOf`, `trimStart`, `trimEnd`, `replaceAll`, `at`
- ✅ Negative index support for `slice` and `at`
- ✅ Optional parameter support for `slice`, `padStart`, `padEnd`
- ✅ Full interpreter and IL compiler support with 74 test cases

### Phase 17 Features (Overloading & Labels)
- ✅ Function overloading with signature declarations and implementation function
- ✅ Method overloading in classes (instance and static methods)
- ✅ Constructor overloading
- ✅ Type-based and arity-based overload resolution
- ✅ Label statements (`label: for (...)`, `label: while (...)`, `label: { }`)
- ✅ Labeled `break` and `continue` for targeting specific loops
- ✅ Nested loop label targeting (break outer from inner loop)
- ✅ Labeled block statements with break support
- ✅ Label scoping and shadowing validation
- ✅ Full interpreter and IL compiler support

### Phase 18 Features (Override Keyword)
- ✅ `override` keyword for methods (`override speak(): string`)
- ✅ `override` keyword for getters and setters
- ✅ Type checking validates override targets exist in parent class
- ✅ Multi-level inheritance support (override grandparent methods)
- ✅ Validation: `override` cannot be used with `static`
- ✅ Validation: `override` cannot be used on constructors
- ✅ Validation: `override` requires class to have a superclass
- ✅ Override keyword is optional (implicit override still works)
- ✅ Full interpreter and IL compiler support with 32 test cases

### Phase 19 Features (This Parameter Typing)
- ✅ `this` parameter syntax in function declarations (`function f(this: MyType)`)
- ✅ `this` parameter in class methods (`method(this: ClassName)`)
- ✅ `this` parameter in abstract methods
- ✅ `this` parameter in interface method signatures
- ✅ `this` parameter in object literal method shorthand
- ✅ `this` parameter in function type annotations (`type Fn = (this: Ctx) => void`)
- ✅ `this` parameter in overloaded functions
- ✅ `this` parameter with generic types
- ✅ Type checking uses declared `this` type for `this` expressions
- ✅ Full interpreter and IL compiler support with 23 test cases

### Phase 20 Features (Class Field Initializers & Object Method This)
- ✅ Class field initializers (`class Foo { count: number = 10; }`)
- ✅ Instance field initializers evaluated at object creation time
- ✅ Proper field initialization order (superclass fields first)
- ✅ Object method shorthand `this` binding in interpreter (`{ fn() { return this.x; } }`)
- ✅ `IsObjectMethod` flag for arrow functions to distinguish object methods
- ✅ Type checker allows `this` in object methods (infers `any` type)
- ✅ Full interpreter support for both features
- ✅ IL compiler support for class field initializers
- ✅ IL compiler support for object method `this` binding

### Phase 21 Features (BigInt Type)
- ✅ `bigint` type with `123n` literal syntax
- ✅ `BigInt()` constructor for converting numbers, strings, and bigints
- ✅ Arbitrary precision using `System.Numerics.BigInteger` as CLR backing type
- ✅ Arithmetic operators (`+`, `-`, `*`, `/`, `%`) with truncation division
- ✅ Comparison operators (`<`, `>`, `<=`, `>=`, `===`, `!==`)
- ✅ Bitwise operators (`&`, `|`, `^`, `~`, `<<`, `>>`)
- ✅ Exponentiation operator (`**`) for bigint values
- ✅ Unary negation (`-bigint`) and bitwise NOT (`~bigint`)
- ✅ Strict type checking: no implicit mixing with `number` type
- ✅ Type error for `>>>` operator on bigint (TypeScript restriction)
- ✅ `typeof bigint` returns `"bigint"`
- ✅ `toString()` outputs with `n` suffix (e.g., `123n`)
- ✅ Hex string parsing in `BigInt("0xFF")` constructor
- ✅ `SharpTSBigInt` runtime wrapper class
- ✅ Full interpreter and IL compiler support with 62 test cases

### Phase 22 Features (Async/Await)
- ✅ `async` function declarations with `Promise<T>` return type
- ✅ `await` expressions with automatic state machine compilation
- ✅ Async arrow functions (both expression and block body)
- ✅ Nested async arrow functions with by-reference capture
- ✅ Async class methods with full `this` capture
- ✅ `this.property` assignment in async methods
- ✅ Try/catch inside async functions (segmented exception handling)
- ✅ Finally blocks with awaits (flag-based execution)
- ✅ Nested await in call arguments (`await fn(await getValue())`)
- ✅ Non-async arrows inside async arrows (display class capture)
- ✅ Return inside try with await in finally (pending return tracking)
- ✅ Multiple await points with correct state machine transitions
- ✅ Super method calls in async methods
- ✅ Full interpreter and IL compiler support with 108 async-related test cases

### Phase 23 Features (Number Methods)
- ✅ `Number.parseInt(string, radix?)` with radix support (2-36)
- ✅ `Number.parseFloat(string)` with JavaScript parsing semantics
- ✅ `Number.isNaN(value)` - strict NaN check (no coercion)
- ✅ `Number.isFinite(value)` - strict finite check (no coercion)
- ✅ `Number.isInteger(value)` - integer detection
- ✅ `Number.isSafeInteger(value)` - safe integer range check (±2^53-1)
- ✅ Static constants: `MAX_VALUE`, `MIN_VALUE`, `NaN`, `POSITIVE_INFINITY`, `NEGATIVE_INFINITY`, `MAX_SAFE_INTEGER`, `MIN_SAFE_INTEGER`, `EPSILON`
- ✅ Instance method `toFixed(digits)` - fixed-point notation
- ✅ Instance method `toPrecision(precision)` - precision notation
- ✅ Instance method `toExponential(digits)` - exponential notation
- ✅ Instance method `toString(radix)` - base conversion (2-36)
- ✅ Global `parseInt()` and `parseFloat()` functions
- ✅ Global `isNaN()` and `isFinite()` with coercion behavior
- ✅ Full interpreter and IL compiler support

### Phase 24 Features (Date Object)
- ✅ `new Date()` - creates current date/time
- ✅ `new Date(milliseconds)` - creates from epoch milliseconds
- ✅ `new Date(isoString)` - parses ISO 8601 format strings
- ✅ `new Date(year, month, day?, hours?, minutes?, seconds?, ms?)` - component constructor
- ✅ `Date()` function call returns current date as string
- ✅ `Date.now()` returns current timestamp in milliseconds
- ✅ Getter methods: `getTime`, `getFullYear`, `getMonth` (0-indexed), `getDate`, `getDay`, `getHours`, `getMinutes`, `getSeconds`, `getMilliseconds`, `getTimezoneOffset`
- ✅ Setter methods: `setTime`, `setFullYear`, `setMonth`, `setDate`, `setHours`, `setMinutes`, `setSeconds`, `setMilliseconds` (all mutate and return timestamp)
- ✅ Conversion methods: `toString`, `toISOString`, `toDateString`, `toTimeString`, `valueOf`
- ✅ JavaScript Date quirks: 0-indexed months, 2-digit year mapping (0-99 → 1900-1999), overflow handling
- ✅ Invalid date handling (returns NaN from getTime, "Invalid Date" from toString)
- ✅ Local timezone support (no UTC variants)
- ✅ Full interpreter and IL compiler support with 42 test cases
