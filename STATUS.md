# SharpTS Implementation Status

This document tracks TypeScript language features and their implementation status in SharpTS.

**Last Updated:** 2026-06-23 (Tier-1 tech-debt cleanup — NaN-guard parity in RuntimeTypes equality, async-function suspension-walker reconciliation, dead-code removal, and the "Known regression" entry below corrected per PR [#906](https://github.com/nickna/SharpTS/issues/906). Prior: Perf epic [#856](https://github.com/nickna/SharpTS/issues/856) — compiled output now meets or beats Node.js on 5 of 7 cross-runtime workloads, the other two within ~1.2×; loop-backedge cancellation now emits `throw` instead of a returning `call`, recovering ~1.8× on tight numeric loops — see §18)

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
| Variance annotations (`in`, `out`, `in out`) | ✅ | Explicit variance control for generic type parameters (TS 4.7+) |
| Union Types (`string \| number`) | ✅ | With type narrowing support |
| Intersection Types (`A & B`) | ✅ | For combining types with full TypeScript semantics |
| Literal Types (`"success" \| "error"`) | ✅ | String, number, and boolean literals |
| Type Aliases (`type Name = ...`) | ✅ | Including function types |
| Tuple Types (`[string, number]`) | ✅ | Fixed-length typed arrays with optional, rest, and named elements |
| `unknown` type | ✅ | Safer alternative to `any` |
| `never` type | ✅ | For exhaustive checking |
| Type Assertions (`as`, `<Type>`) | ✅ | Both `as` and angle-bracket syntax |
| `as const` assertions | ✅ | Deep readonly inference for literals |
| Type Guards (`is`, `typeof` narrowing) | ✅ | `typeof` narrowing, user-defined type guards (`x is T`), assertion functions, property access narrowing |
| `readonly` modifier | ✅ | Compile-time enforcement |
| Optional Properties (`prop?:`) | ✅ | Partial object shapes |
| Index Signatures (`[key: string]: T`) | ✅ | String, number, and symbol key types |
| `object` type | ✅ | Non-primitive type (excludes string, number, boolean, bigint, symbol, null, undefined) |
| `unique symbol` type | ✅ | Nominally-typed symbols for const declarations |
| Type predicates (`is`, `asserts`) | ✅ | User-defined type guards (`x is T`), assertion functions (`asserts x is T`, `asserts x`) |
| `satisfies` operator | ✅ | Validates expression matches type without widening (TS 4.9+) |
| Variadic tuple types | ✅ | `[...T]` spread in tuples, Prepend/Append/Concat patterns |
| Definite assignment assertion | ✅ | `let x!: number` syntax for variables and class fields |

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
| Private fields (`#field`) | ✅ | ES2022 hard private fields with ConditionalWeakTable isolation; full interpreter and IL compiler support |
| Static blocks | ✅ | `static { }` for static initialization; executes in declaration order with static fields; `this` binds to class |
| `accessor` keyword | ✅ | Auto-accessor class fields (TS 4.9+); full interpreter and IL compiler support with deferred boxing optimization |
| `declare` field modifier | ✅ | Ambient field declarations for external initialization (decorators, DI); full interpreter and IL compiler support |

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
| Named function expressions | ✅ | `const f = function myFunc() {}` with self-reference for recursion |
| Constructor signatures | ✅ | `new (params): T` in interfaces, `new` on expressions |
| Call signatures | ✅ | `(params): T` in interfaces, callable interface types |

---

## 5. ASYNC/PROMISES

| Feature | Status | Notes |
|---------|--------|-------|
| Promises | ✅ | `Promise<T>` type with await support, `new Promise((resolve, reject) => { })` executor constructor |
| Promise instance methods | ✅ | `.then()`, `.catch()`, `.finally()` with chaining support |
| `async` functions | ✅ | Full state machine compilation |
| `await` keyword | ✅ | Pause and resume via .NET Task infrastructure |
| Async arrow functions | ✅ | Including nested async arrows |
| Async class methods | ✅ | Full `this` capture support |
| Try/catch in async | ✅ | Await inside try/catch/finally blocks |
| Nested await in args | ✅ | `await fn(await getValue())` |
| `Promise.all/race/any/allSettled` | ✅ | Full interpreter support; IL compiler: all/race/allSettled as pure IL state machines, any delegates to runtime |
| `Promise.resolve/reject` | ✅ | Static factory methods with Promise flattening |
| `Promise.withResolvers` | ✅ | Returns `{promise, resolve, reject}` for external promise resolution (ES2024) |

---

## 6. MODULES

| Feature | Status | Notes |
|---------|--------|-------|
| `import` statements | ✅ | `import { x } from './file'` |
| `export` statements | ✅ | `export function/class/const` |
| Default exports | ✅ | `export default` |
| Namespace imports | ✅ | `import * as X from './file'` |
| Re-exports | ✅ | `export { x } from './file'`, `export * from './file'` |
| TypeScript namespaces | ✅ | `namespace X { }` with declaration merging, dotted syntax, functions, variables, enums, nested namespaces, classes with `new Namespace.Class()` instantiation |
| Namespace import alias | ✅ | `import X = Namespace.Member`, `export import X = Namespace.Member` |
| Dynamic imports | ✅ | `await import('./file')` with module registry for compiled mode, `typeof import()` typing for literal paths |
| `import type` | ✅ | Statement-level (`import type { T }`) and inline (`import { type T }`) type-only imports |
| `import.meta` | ✅ | `import.meta.url` for module metadata |
| `export =` / `import =` | ✅ | CommonJS interop: `export = value`, `import x = require('path')`, `export import x = require()` (class exports have known limitation) |
| Ambient module declarations | ✅ | `declare module 'x' { }` - type-only declarations for external packages |
| Module augmentation | ✅ | `declare module './path' { }` extends existing modules, `declare global { }` extends global types |
| Triple-slash references | ✅ | `/// <reference path="...">` for script-style file merging |
| `package.json` exports | ✅ | Subpath exports, conditional exports (`types`/`import`/`default`), wildcard patterns, null restrictions, array fallbacks |
| Subpath imports (`#`) | ✅ | `"imports"` field in package.json with `#`-prefixed specifiers |
| Self-referencing | ✅ | Package imports itself by name through its own `exports` |

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
| `typeof` | ✅ | Returns `"undefined"` for undeclared variables (no ReferenceError) |
| Assignment (`=`, `+=`, `-=`, `*=`, `/=`, `%=`) | ✅ | |
| Increment/Decrement (`++`, `--`) | ✅ | Pre and post |
| Bitwise (`&`, `\|`, `^`, `~`, `<<`, `>>`, `>>>`) | ✅ | Including compound assignments |
| Strict equality (`===`, `!==`) | ✅ | Same behavior as `==`/`!=` |
| `instanceof` | ✅ | With inheritance chain support |
| `in` operator | ✅ | Property existence check |
| Exponentiation (`**`) | ✅ | Right-associative |
| Spread operator (`...`) | ✅ | In arrays/objects/calls |
| Non-null assertion (`x!`) | ✅ | Postfix operator to assert non-null |
| Logical assignment (`&&=`, `\|\|=`, `??=`) | ✅ | Compound logical assignment operators with short-circuit evaluation |
| `keyof` operator | ✅ | Extract keys as union type |
| `typeof` in type position | ✅ | Extract type from value |
| Comma operator (`,`) | ✅ | Sequence expression: evaluates left-to-right, returns last value |

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
| Optional catch binding | ✅ | `catch { }` without parameter (ES2019) |

---

## 10. BUILT-IN APIS

| Feature | Status | Notes |
|---------|--------|-------|
| `console.log` | ✅ | Multiple arguments, printf-style format specifiers (%s, %d, %i, %f, %o, %O, %j, %%) |
| `Math` object | ✅ | PI, E, abs, floor, ceil, round, sqrt, sin, cos, tan, log, exp, sign, trunc, pow, min, max, random |
| String methods | ✅ | length, charAt, substring, indexOf, toUpperCase, toLowerCase, trim, replace, split, includes, startsWith, endsWith, slice, repeat, padStart, padEnd, charCodeAt, codePointAt, concat, lastIndexOf, trimStart, trimEnd, replaceAll, at, matchAll |
| Array methods | ✅ | push, pop, shift, unshift, reverse, slice, concat, map, filter, forEach, find, findIndex, findLast, findLastIndex, some, every, reduce, reduceRight, includes, indexOf, join, sort, toSorted, toReversed, with, flat, flatMap, splice, toSpliced, at, fill, copyWithin, entries, keys, values |
| `JSON.parse`/`stringify` | ✅ | With reviver, replacer, indentation, class instances, toJSON(), BigInt TypeError |
| `Object.keys`/`values`/`entries`/`fromEntries`/`hasOwn` | ✅ | Full support for object literals and class instances |
| `Array.isArray` | ✅ | Type guard for array detection |
| `Number` methods | ✅ | parseInt, parseFloat, isNaN, isFinite, isInteger, isSafeInteger, toFixed, toPrecision, toExponential, toString(radix); constants: MAX_VALUE, MIN_VALUE, NaN, POSITIVE_INFINITY, NEGATIVE_INFINITY, MAX_SAFE_INTEGER, MIN_SAFE_INTEGER, EPSILON |
| `Date` object | ✅ | Full local timezone support with constructors, getters, setters, conversion methods |
| `Map`/`Set` | ✅ | Full API (get, set, has, delete, clear, size, keys, values, entries, forEach); for...of iteration; reference equality for object keys; `Map.groupBy()` (ES2024); ES2025 Set operations (union, intersection, difference, symmetricDifference, isSubsetOf, isSupersetOf, isDisjointFrom) |
| `WeakMap`/`WeakSet` | ✅ | Full API (get, set, has, delete for WeakMap; add, has, delete for WeakSet); object-only keys/values; no iteration or size |
| `RegExp` | ✅ | Full API (test, exec, source, flags, global, ignoreCase, multiline, lastIndex); `/pattern/flags` literal and `new RegExp()` constructor; string methods (match, replace, search, split, matchAll) with regex support; named capture groups (`(?<name>...)`) with `.groups` property on match results |
| `Array.from()` | ✅ | Create array from iterable with optional map function |
| `Array.of()` | ✅ | Create array from arguments |
| `Object.assign()` | ✅ | Merge objects - copies properties from one or more source objects to a target object, returns the target |
| `Object.fromEntries()` | ✅ | Inverse of `Object.entries()` - converts iterable of [key, value] pairs to object |
| `Object.hasOwn()` | ✅ | Safer `hasOwnProperty` check - returns true for own properties, false for methods |
| `Object.freeze()`/`seal()`/`isFrozen()`/`isSealed()` | ✅ | Object immutability - freeze prevents all changes, seal allows modification but prevents adding/removing properties; shallow freeze/seal (nested objects unaffected); works on objects, arrays, class instances |
| `Error` class | ✅ | Error, TypeError, RangeError, ReferenceError, SyntaxError, URIError, EvalError, AggregateError with name, message, stack, cause (ES2022) properties |
| Strict mode (`"use strict"`) | ✅ | File-level and function-level strict mode; frozen/sealed object mutations throw TypeError in strict mode |
| `setTimeout`/`clearTimeout` | ✅ | Timer functions with Timeout handle, ref/unref support |
| `setInterval`/`clearInterval` | ✅ | Repeating timer functions with Timeout handle, no overlap between executions |
| `setImmediate`/`clearImmediate` | ✅ | Immediate execution timer (runs after current event loop iteration) |
| `globalThis` | ✅ | ES2020 global object reference with property access and method calls |
| `structuredClone` | ✅ | Deep clone of values (objects, arrays, Map, Set, etc.) |
| `AbortController`/`AbortSignal` | ✅ | `new AbortController()`, `signal.aborted`, `abort(reason?)`, `addEventListener`/`removeEventListener`, `throwIfAborted`, `onabort`, `AbortSignal.abort()`/`timeout()`/`any()`, fetch `signal` option |
| `fetch()` API | ✅ | `fetch(url, options?)` returns `Promise<Response>`; Response: `status`, `statusText`, `ok`, `url`, `headers`, `body` (Readable stream), `bodyUsed`, `json()`, `text()`, `arrayBuffer()`, `clone()`; Headers: `get()`, `set()`, `has()`, `delete()`, `append()`, `forEach()`, `entries()`, `keys()`, `values()`; options: `method`, `headers`, `body`, `signal`, `redirect` (`follow`/`manual`/`error`) |
| Web Streams API | ✅ | `ReadableStream`, `WritableStream`, `TransformStream` with default controllers/readers/writers; `pipeTo()`, `pipeThrough()`, `tee()`, `cancel()`, `getReader()`/`getWriter()`, `closed`/`ready` promises, `desiredSize` backpressure; `ByteLengthQueuingStrategy`, `CountQueuingStrategy`; `ReadableStream.from(iterable)` (eager array/string/Set forms); also exported from `node:stream/web`. **Deferred:** BYOB readers, transferable streams, `Symbol.asyncIterator` on ReadableStream, `Response.body` migration. |
| `URL`/`URLSearchParams` | ✅ | Full WHATWG URL API: constructor, `href`, `protocol`, `host`, `hostname`, `port`, `pathname`, `search`, `hash`, `origin`, `username`, `password`, `searchParams`, `toString()`; URLSearchParams: `get()`, `set()`, `has()`, `delete()`, `append()`, `entries()`, `keys()`, `values()`, `forEach()`, `toString()`, `sort()` |
| `TextEncoder`/`TextDecoder` | ✅ | `TextEncoder.encode(string)` → `Uint8Array`; `TextDecoder.decode(buffer)` → `string`; UTF-8 encoding/decoding |
| `console` methods | ✅ | `log`, `error`, `warn`, `info`, `debug`, `clear`, `time`/`timeEnd`/`timeLog`, `assert`, `count`/`countReset`, `table`, `dir`, `group`/`groupCollapsed`/`groupEnd`, `trace` |
| `Intl.NumberFormat` | ✅ | Locale-aware number/currency/percent formatting; `format()`, `resolvedOptions()`; options: style, currency, minimumFractionDigits, maximumFractionDigits, minimumIntegerDigits, useGrouping |
| `Intl.DateTimeFormat` | ✅ | Locale-aware date/time formatting; `format()`, `resolvedOptions()`; options: dateStyle, timeStyle, year, month, day, weekday, hour, minute, second, hour12, timeZone, timeZoneName, era, fractionalSecondDigits |
| `Intl.Collator` | ✅ | Locale-aware string comparison; `compare()`, `resolvedOptions()`; options: usage, sensitivity (base/accent/case/variant), ignorePunctuation, numeric, caseFirst |
| `Intl.PluralRules` | ✅ | Plural category selection (CLDR rules); `select()`, `resolvedOptions()`; options: type (cardinal/ordinal); categories: zero, one, two, few, many, other |
| `Intl.RelativeTimeFormat` | ✅ | Locale-aware relative time formatting; `format()`, `formatToParts()`, `resolvedOptions()`; options: style (long/short/narrow), numeric (always/auto); units: year, quarter, month, week, day, hour, minute, second |
| `Intl.ListFormat` | ✅ | Locale-aware list formatting; `format()`, `formatToParts()`, `resolvedOptions()`; options: style (long/short/narrow), type (conjunction/disjunction/unit) |
| `Intl.Segmenter` | ✅ | Unicode text segmentation; `segment()` returns iterable Segments with `containing()`; options: granularity (grapheme/word/sentence); segment data: segment, index, input, isWordLike |
| `Intl.DisplayNames` | ✅ | Locale-aware display names; `of()`, `resolvedOptions()`; types: language, region, script, currency, calendar, dateTimeField; options: style, fallback (code/none), languageDisplay |

---

## 11. SYNTAX

| Feature | Status | Notes |
|---------|--------|-------|
| `let` / `const` declarations | ✅ | Block-scoped per spec |
| `var` declarations | ✅ | Function-scoped semantics via parser-time hoisting; supports declarations in nested blocks (if/for/while/try) referenced in the enclosing function scope. Multi-declarator (`var a = 1, b = 2`) supported. |
| Multi-declarator `let`/`const` | ✅ | `let a = 1, b = 2` and `const x = 1, y = 2` |
| Line comments (`//`) | ✅ | |
| Double-quoted strings | ✅ | |
| Template literals | ✅ | With interpolation |
| Object literals | ✅ | |
| Array literals | ✅ | |
| Block comments (`/* */`) | ✅ | |
| Single-quoted strings | ✅ | |
| Object method shorthand | ✅ | `{ fn() {} }` |
| Object literal accessors | ✅ | `{ get x() {}, set x(v) {} }` with proper `this` binding |
| Computed property names | ✅ | `{ [expr]: value }`, `{ "key": v }`, `{ 123: v }` |
| Class expressions | ✅ | `const C = class { }` - interpreter and IL compiler full support |
| Shorthand properties | ✅ | `{ x }` instead of `{ x: x }` |
| Tagged template literals | ✅ | `` tag`template` `` syntax with TemplateStringsArray and raw property |
| Numeric separators | ✅ | `1_000_000` for readability |

---

## 12. ADVANCED FEATURES

| Feature | Status | Notes |
|---------|--------|-------|
| Decorators (`@decorator`) | ✅ | Legacy & TC39 Stage 3, class/method/property/parameter decorators, Reflect API, `@Namespace` for .NET namespaces |
| Generators (`function*`) | ✅ | `yield`, `yield*`, `.next()`, `.return()`, `.throw()`; for...of integration |
| Async Generators (`async function*`) | ✅ | `yield`, `yield*`, `.next()`, `.return()`, `.throw()`; `for await...of`; full IL compiler support |
| Well-known Symbols | ✅ | `Symbol.iterator`, `Symbol.asyncIterator`, `Symbol.toStringTag`, `Symbol.hasInstance`, `Symbol.isConcatSpreadable`, `Symbol.toPrimitive`, `Symbol.species`, `Symbol.unscopables`, `Symbol.dispose`, `Symbol.asyncDispose` |
| Iterator Protocol | ✅ | Custom iterables via `[Symbol.iterator]()` method (interpreter and compiler) |
| Iterator Helpers (ES2025) | ✅ | `.map()`, `.filter()`, `.take()`, `.drop()`, `.flatMap()`, `.reduce()`, `.toArray()`, `.forEach()`, `.some()`, `.every()`, `.find()`, `.next()` protocol; lazy evaluation; chaining; works on arrays, generators, Map/Set iterators |
| Async Iterator Protocol | ✅ | Custom async iterables via `[Symbol.asyncIterator]()` method |
| `for await...of` | ✅ | Async iteration over async iterators and generators |
| `Symbol.for`/`Symbol.keyFor` | ✅ | Global symbol registry |
| Symbols | ✅ | Unique identifiers via `Symbol()` constructor |
| `bigint` type | ✅ | Arbitrary precision integers with full operation support |
| Mapped types | ✅ | `{ [K in keyof T]: ... }`, `keyof`, indexed access `T[K]`, modifiers (+/-readonly, +/-?) |
| Conditional types | ✅ | `T extends U ? X : Y`, `infer` keyword, distribution over unions |
| Template literal types | ✅ | `` `prefix${string}` ``, union expansion, pattern matching, `infer` support |
| Utility types | ✅ | `Partial<T>`, `Required<T>`, `Readonly<T>`, `Record<K, V>`, `Pick<T, K>`, `Omit<T, K>`, `Uppercase<S>`, `Lowercase<S>`, `Capitalize<S>`, `Uncapitalize<S>` |
| Additional utility types | ✅ | `ReturnType<T>`, `Parameters<T>`, `ConstructorParameters<T>`, `InstanceType<T>`, `ThisType<T>`, `Awaited<T>`, `NonNullable<T>`, `Extract<T, U>`, `Exclude<T, U>` |
| `using`/`await using` | ✅ | Explicit resource management (TS 5.2+); `Symbol.dispose`/`Symbol.asyncDispose`; automatic disposal at scope exit; SuppressedError for disposal errors |
| Const type parameters | ✅ | `<const T>` syntax (TS 5.0+) for preserving literal types during inference; readonly semantics for objects/arrays |
| Variance annotations | ✅ | `in`/`out`/`in out` modifiers on type parameters (TS 4.7+) |
| Recursive type aliases | ✅ | Self-referential type definitions like `type Node = { next: Node | null }` and generic `type Tree<T> = { children: Tree<T>[] }` |

---

## 13. BINARY DATA & THREADING

| Feature | Status | Notes |
|---------|--------|-------|
| **TypedArrays** | | |
| `Int8Array` | ✅ | Signed 8-bit integer array |
| `Uint8Array` | ✅ | Unsigned 8-bit integer array |
| `Int16Array` | ✅ | Signed 16-bit integer array |
| `Uint16Array` | ✅ | Unsigned 16-bit integer array |
| `Int32Array` | ✅ | Signed 32-bit integer array |
| `Uint32Array` | ✅ | Unsigned 32-bit integer array |
| `Float32Array` | ✅ | 32-bit float array |
| `Float64Array` | ✅ | 64-bit float array |
| `BigInt64Array` | ✅ | Signed 64-bit BigInt array |
| `BigUint64Array` | ✅ | Unsigned 64-bit BigInt array |
| `Uint8ClampedArray` | ✅ | Clamped unsigned 8-bit integer array |
| **Shared Memory** | | |
| `SharedArrayBuffer` | ✅ | Shared memory for worker threads |
| `Atomics` | ✅ | load, store, add, sub, and, or, xor, exchange, compareExchange, wait, notify |
| `ArrayBuffer` | ✅ | Non-shared binary buffer: constructor, byteLength, slice(), isView() |
| **DataView** | | |
| `DataView` | ✅ | Full API: constructor, properties (buffer, byteLength, byteOffset), getter/setter methods with endianness support |

---

## 14. EXTENDED BUILT-IN APIS

This section tracks JavaScript/TypeScript APIs that were historically unimplemented in SharpTS. **Most are now supported** (✅) and are kept here for status visibility; the rows below reflect current state. The only fully-unimplemented item is the `Function` constructor (❌); `eval` is partial (⚠️).

### Objects & Types

| Feature | Status | Notes |
|---------|--------|-------|
| `Proxy` | ✅ | `new Proxy(target, handler)`, `Proxy.revocable()`, traps: get/set/has/deleteProperty/apply/construct |
| `WeakRef` | ✅ | `new WeakRef(target)`, `.deref()` |
| `FinalizationRegistry` | ✅ | `new FinalizationRegistry(callback)`, `.register(target, heldValue, token?)`, `.unregister(token)` |
| `Intl.NumberFormat` | ✅ | See Section 10 |
| `Intl.DateTimeFormat` | ✅ | See Section 10 |
| `Intl.Collator` | ✅ | See Section 10 |
| `Intl.PluralRules` | ✅ | See Section 10 |
| `Intl.RelativeTimeFormat` | ✅ | See Section 10 |
| `Intl.ListFormat` | ✅ | See Section 10 |
| `Intl.Segmenter` | ✅ | See Section 10 |
| `Intl.DisplayNames` | ✅ | See Section 10 |

### Global Functions

| Feature | Status | Notes |
|---------|--------|-------|
| `eval()` | ⚠️ | Typed as `(s: string) => any`. **Interpreted:** direct eval — source runs against the caller's scope chain. **Compiled:** indirect eval via the SharpTS runtime (`EvalBridge`) — global builtins resolve but compiled locals are not visible. The build auto-copies SharpTS.dll next to the output when eval is used (see "Standalone DLL Constraint" in CLAUDE.md); with `--standalone` it is not copied and eval throws "eval not supported" at runtime. Eval'd source is not type-checked. Non-string args returned unchanged (ECMA-262 §19.2.1). |
| `Function` constructor | ❌ | Cannot create functions from strings |
| `queueMicrotask()` | ✅ | Schedules microtask for execution |

### Object Static Methods

| Feature | Status | Notes |
|---------|--------|-------|
| `Object.create()` | ✅ | Creates new object with prototype; supports propertiesObject argument for defining properties via descriptors |
| `Object.is()` | ✅ | Same-value comparison; handles NaN and +0/-0 edge cases |
| `Object.getOwnPropertyDescriptor()` | ✅ | Full support in both interpreter and compiled mode |
| `Object.defineProperty()` | ✅ | Full support including accessor properties (get/set) and descriptor flags in both modes |
| `Object.getPrototypeOf()` | ✅ | Returns prototype of an object |
| `Object.setPrototypeOf()` | ✅ | Sets prototype of an object |
| `Object.getOwnPropertyNames()` | ✅ | Returns all own property names including non-enumerable |
| `Object.getOwnPropertySymbols()` | ✅ | Returns array of symbol-keyed properties |
| `Object.preventExtensions()` | ✅ | Prevents adding new properties to an object |
| `Object.isExtensible()` | ✅ | Returns whether object allows new properties |
| `Object.groupBy()` | ✅ | Groups iterable elements by callback return value (ES2024); returns plain object with string keys |
| `Object.defineProperties()` | ✅ | Batch version of defineProperty; defines multiple properties from a descriptors object |
| `Object.getOwnPropertyDescriptors()` | ✅ | Returns all own property descriptors as an object; works with defineProperties for proper object cloning |

### Array Methods

| Feature | Status | Notes |
|---------|--------|-------|
| `fill()` | ✅ | |
| `reduceRight()` | ✅ | Iterates right-to-left |
| `entries()` | ✅ | Returns iterator of [index, value] pairs |
| `keys()` | ✅ | Returns iterator of indices |
| `values()` | ✅ | Returns iterator of values |
| `copyWithin()` | ✅ | Copies array elements within the array |

### String Methods & Static

| Feature | Status | Notes |
|---------|--------|-------|
| `normalize()` | ✅ | Unicode normalization (NFC, NFD, NFKC, NFKD) |
| `localeCompare()` | ✅ | Locale-aware comparison via string.Compare with CurrentCulture |
| `codePointAt()` | ✅ | Full Unicode code point at position; handles surrogate pairs for supplementary characters |
| `String.fromCharCode()` | ✅ | Creates string from UTF-16 code units |
| `String.fromCodePoint()` | ✅ | Creates string from Unicode code points; handles supplementary characters (> U+FFFF) via surrogate pairs |
| `String.raw` | ✅ | Tagged template for raw string access without escape processing |

### Reflect API (Standard)

| Feature | Status | Notes |
|---------|--------|-------|
| `Reflect.get()` | ✅ | Property access on target object |
| `Reflect.set()` | ✅ | Returns `bool`; `false` for frozen objects |
| `Reflect.has()` | ✅ | Equivalent to `in` operator |
| `Reflect.deleteProperty()` | ✅ | Returns `bool`; `false` for frozen objects |
| `Reflect.apply()` | ✅ | Calls function with thisArg and args array |
| `Reflect.construct()` | ✅ | Creates instance; interpreter mode only for classes |
| `Reflect.ownKeys()` | ✅ | Returns string keys + symbol keys |
| `Reflect.getPrototypeOf()` | ✅ | Returns object prototype |
| `Reflect.setPrototypeOf()` | ✅ | Returns `bool`; `false` for non-extensible objects |
| `Reflect.isExtensible()` | ✅ | Returns `bool` |
| `Reflect.preventExtensions()` | ✅ | Returns `true` |
| `Reflect.getOwnPropertyDescriptor()` | ✅ | Returns property descriptor or undefined |
| `Reflect.defineProperty()` | ✅ | Returns `bool`; `false` on failure (unlike `Object.defineProperty` which throws) |

---

## 15. NODE.JS BUILT-IN MODULES

SharpTS implements 20+ Node.js built-in modules accessible via `import ... from "node:..."` or bare specifiers.

| Module | Status | Notes |
|--------|--------|-------|
| `fs` / `fs/promises` | ✅ | readFileSync, writeFileSync, existsSync, mkdirSync, readdirSync, statSync, unlinkSync, renameSync, copyFileSync, appendFileSync, readFile, writeFile, mkdir, readdir, stat, unlink, rename, rm, access, lstat, realpath, createReadStream, createWriteStream, watch, watchFile, unwatchFile |
| `path` | ✅ | join, resolve, dirname, basename, extname, normalize, isAbsolute, relative, parse, format, sep, delimiter, posix, win32. **SharpTS divergence:** `path.win32.isAbsolute('/foo')` returns `false` (requires drive-letter + separator or UNC double-separator). Node returns `true` for any leading separator. |
| `os` | ✅ | platform, arch, cpus, hostname, homedir, tmpdir, type, release, uptime, totalmem, freemem, EOL, networkInterfaces, loadavg, userInfo |
| `process` | ✅ | Module facade === global (same live object; `import process from 'process'` is the global, epic #1078). argv, env (live), cwd(), chdir(), exit() (fires 'exit'), pid, ppid, platform, arch, version (`v24.15.0` Node-compat), versions, title (get/set), execPath, execArgv, argv0, config, release, features, debugPort, allowedNodeEnvironmentFlags, stdout, stderr, stdin, hrtime + hrtime.bigint(), nextTick, memoryUsage + memoryUsage.rss(), cpuUsage, resourceUsage, availableMemory, constrainedMemory, getActiveResourcesInfo, exitCode (get/set), kill (signal 0 check, self-signal dispatch; cross-process termination maps to Process.Kill), abort, umask, emitWarning + throwDeprecation/traceDeprecation/noDeprecation, sourceMapsEnabled/setSourceMapsEnabled, report.getReport/writeReport + config props; full EventEmitter surface; lifecycle events 'exit'/'beforeExit' (natural drain, re-entrant) + 'warning'; signal events SIGINT/SIGTERM/SIGHUP/SIGBREAK/SIGQUIT/SIGWINCH via PosixSignalRegistration; 'unhandledRejection'/'rejectionHandled' (interpreted; compiled deferred); POSIX getuid/geteuid/getgid/getegid/getgroups/setuid/setgid (POSIX interp; undefined on Windows like Node; compiled POSIX deferred); IPC send/disconnect/connected/channel when forked. Compiled `--standalone` ceilings: ppid → 0 (late-binds to SharpTS.dll when co-located) |
| `crypto` | ✅ | createHash, createHmac, randomBytes, randomUUID, randomInt, randomFillSync, createCipheriv/Decipheriv, pbkdf2/pbkdf2Sync, scrypt/scryptSync, timingSafeEqual, generateKeyPair/generateKeyPairSync, createSign/Verify, createDiffieHellman, createECDH, hkdf/hkdfSync, getHashes, getCiphers, getCurves, constants |
| `events` | ✅ | EventEmitter: on, once, emit, removeListener, removeAllListeners, listenerCount, listeners, prependListener, prependOnceListener, off, setMaxListeners, getMaxListeners, eventNames |
| `stream` | ✅ | Readable, Writable, Duplex, Transform, PassThrough; `finished(stream, opts?, cb)`, `pipeline(source, ...transforms, dest, cb?)`, `addAbortSignal(signal, stream)`; `Readable.from(iterable)`, `Readable.isReadable(stream)`, `Writable.isWritable(stream)`; instance: `toArray()`, `forEach(fn)`, `map(fn)`, `filter(fn)`; `pause`/`resume`/`prefinish` events, `autoDestroy` option, `highWaterMark` enforcement; object mode support; `stream/promises` sub-module (`pipeline`, `finished`) |
| `buffer` | ✅ | Buffer.from, Buffer.alloc, Buffer.allocUnsafe, Buffer.concat, Buffer.isBuffer, Buffer.byteLength; instance methods: toString, slice, copy, write, fill, includes, indexOf, compare, equals, readUInt/Int, writeUInt/Int, toJSON |
| `http` / `https` | ✅ | createServer, request, get; Agent class with constructor, destroy, getName, globalAgent; Server: listen, close; IncomingMessage extends Readable; ServerResponse extends Writable; full event lifecycle |
| `net` | ✅ | createServer (options: highWaterMark, allowHalfOpen, blockList), createConnection/connect, Socket (EventEmitter + Duplex; write backpressure + 'drain', writableLength/writableHighWaterMark/writableNeedDrain, allowHalfOpen half-close with readOnly/writeOnly readyState, localFamily/pending), Server (EventEmitter; maxConnections + 'drop' event, live getConnections); BlockList (addAddress/addRange/addSubnet/check/rules) + SocketAddress; isIP, isIPv4, isIPv6; get/setDefaultAutoSelectFamily(AttemptTimeout) (best-effort — .NET connects try all resolved addresses); IPC sockets (named pipes on Windows, Unix domain sockets on Linux/macOS) (epic #1067) |
| `tls` | ✅ | createServer, connect, createSecureContext, TLSSocket (extends Socket), Server; DEFAULT_MIN_VERSION/MAX_VERSION; ALPNProtocols, SNICallback, servername; secureConnect/secureConnection/tlsClientError events |
| `dgram` | ✅ | createSocket, Socket; bind, send (destination validation: ERR_SOCKET_DGRAM_IS_CONNECTED/NOT_CONNECTED), close, address, setBroadcast, setTTL, setMulticastTTL, setMulticastLoopback, setMulticastInterface, addMembership, dropMembership, add/dropSourceSpecificMembership (IPv4; udp6 SSM throws — no portable .NET mapping); connect, disconnect, remoteAddress, get/setRecvBufferSize, get/setSendBufferSize; message/listening/close/error/connect events (epic #1067) |
| `cluster` | ✅ | isPrimary/isWorker/isMaster, fork, live workers/worker/settings, worker.send/disconnect/kill/destroy/isDead/isConnected, worker.process handle, process.send/on('message') IPC, cluster events incl. 'listening'/'setup', disconnect, setupPrimary (full settings: exec/args/silent/… honored by fork), schedulingPolicy + SCHED_RR/SCHED_NONE (+ NODE_CLUSTER_SCHED_POLICY), shared-port round-robin. Thread model (workers are in-process threads; worker.process.pid is the host pid). Compiled: fork runs the entry script interpreted — SharpTS.dll co-located, entry .ts must be deployed; `--standalone` throws |
| `child_process` | ✅ | execSync, spawnSync, exec, spawn, execFileSync, execFile, fork (IPC via named pipes); ChildProcess: pid, exitCode, killed, stdout, stderr, stdin, connected, kill, send, disconnect |
| `vm` | ✅ | runInNewContext, runInThisContext, runInContext, createContext (name/origin/codeGeneration/microtaskMode), isContext, compileFunction, measureMemory, constants (USE_MAIN_CONTEXT_DEFAULT_LOADER/DONT_CONTEXTIFY); Script (runIn*Context, createCachedData, filename/offsets, cachedData); ESM-in-vm: SourceTextModule + SyntheticModule (link/evaluate/status/namespace/setExport), importModuleDynamically |
| `url` | ✅ | URL, URLSearchParams, fileURLToPath, pathToFileURL, format, parse |
| `util` | ✅ | promisify, deprecate, types (isDate, isRegExp, isMap, isSet, etc.), format, inspect, TextEncoder, TextDecoder |
| `querystring` | ✅ | parse, stringify, escape, unescape |
| `zlib` | ✅ | Sync: gzipSync, gunzipSync, deflateSync, inflateSync, deflateRawSync, inflateRawSync, brotliCompressSync, brotliDecompressSync; Streaming: createGzip, createGunzip, createDeflate, createInflate, createDeflateRaw, createInflateRaw, createBrotliCompress, createBrotliDecompress, createUnzip; Async callback: gzip, gunzip, deflate, inflate, deflateRaw, inflateRaw, brotliCompress, brotliDecompress, unzip |
| `dns` | ✅ | lookup (family/all/order options), lookupService, resolve, resolve4, resolve6 (CNAME-chain following, bounded 8), reverse, resolveMx, resolveTxt, resolveSrv, resolveCname, resolveNs, resolveSoa, resolvePtr, resolveCaa, resolveNaptr (callback + dns/promises); set/getDefaultResultOrder; Resolver with setServers/getServers, setLocalAddress, cancel() (prompt ECANCELLED interpreted; compiled resolver callbacks run inline-sync — documented deviation) (epic #1067) |
| `assert` | ✅ | ok, equal, notEqual, deepEqual, notDeepEqual, strictEqual, notStrictEqual, deepStrictEqual, throws, doesNotThrow, rejects, doesNotReject, fail, match, doesNotMatch, assert.strict |
| `readline` | ✅ | createInterface (extends EventEmitter), question, close, prompt, pause, resume, write, setPrompt, getPrompt, questionSync |
| `string_decoder` | ✅ | StringDecoder: write, end, encoding |
| `timers` | ✅ | setTimeout, clearTimeout, setInterval, clearInterval, setImmediate, clearImmediate |
| `timers/promises` | ✅ | Promise-based setTimeout, setImmediate, setInterval |
| `perf_hooks` | ✅ | performance.now(), timeOrigin, mark(), measure(), getEntries(), getEntriesByName(), getEntriesByType(), clearMarks(), clearMeasures(); PerformanceObserver |
| `worker_threads` | ✅ | Worker, isMainThread, parentPort, workerData, MessageChannel, MessagePort. Worker `stdin`/`stdout`/`stderr` and `resourceLimits` options are not supported (workers share the parent's console; resourceLimits has no .NET equivalent) |
| `async_hooks` | ✅ | AsyncLocalStorage: run, getStore, enterWith, exit, disable; async context propagation via .NET AsyncLocal |

### Module Implementation Source

14 modules are implemented in TypeScript under `stdlib/node/*.ts`, embedded in `SharpTS.dll` as resources and compiled alongside user code at `--compile` time. The remaining modules stay C#-backed for host-I/O reasons. See `docs/plans/embedded-stdlib.md` for the provider chain, `primitive:*` layer, and migration rationale.

| Implementation | Modules |
|---|---|
| **TypeScript stdlib** | `assert`, `async_hooks`, `events`, `os`, `path`, `perf_hooks`, `process`, `querystring`, `readline`, `string_decoder`, `timers`, `timers/promises`, `tty`, `url`, `util` |
| **C# / IL (host-I/O)** | `fs`, `fs/promises`, `crypto`, `stream`, `stream/promises`, `stream/web`, `buffer`, `http`, `https`, `net`, `tls`, `dgram`, `cluster`, `child_process`, `vm`, `zlib`, `dns`, `dns/promises`, `worker_threads` |

---

## 16. .NET INTEROP (`dotnet:` imports + `@DotNetType`)

.NET types can be consumed two ways: **`dotnet:` import specifiers** (recommended — zero boilerplate, type surface synthesized from reflection; epic #1195) and **`@DotNetType` declare classes** (manual, curated surface, supports `@DotNetOverload` hints). **Both work in interpreter and compiled modes.**

| Feature | Status | Notes |
|---------|--------|-------|
| `import { X } from "dotnet:Ns.Type"` (single-type form) | ✅ | Static types synthesized from reflection via `DotNetTypeSynthesizer`; members filtered by `DotNetInteropClassifier`; fluent chains stay typed (epic #1195) |
| `import { X, Y } from "dotnet:Namespace"` (namespace form) | ✅ | Each named import resolves as `Namespace.Name`; nested types resolve through their declaring type's specifier; aliasing supported |
| `dotnet:` scope | — | Named imports only (no default/star/re-export/require/dynamic import); no generic types (use `@DotNetType` for closed generics); no paths in specifiers (use `sharpts.json`) |
| `sharpts.json` reference manifest | ✅ | `{ "references": [dll paths], "packages": { id: version } }` discovered by upward walk from the entry script; applies uniformly to interpreter, compiler, LSP, and `--gen-decl`; global `-r/--reference` flag augments per-invocation (issue #1197) |
| NuGet packages in manifest | ✅ | Restored via `dotnet restore` on a generated project under `.sharpts/` (hash-gated — skipped when the package set is unchanged; offline after first restore); transitive dependencies resolved, loaded, and co-located with compiled output |
| `@DotNetType("Namespace.Type")` on `declare class` | ✅ | Instance methods, static methods, constructors, properties, fields |
| `@DotNetOverload("type1,type2,...")` overload hints | ✅ | Pin a specific overload when argument-based resolution is ambiguous |
| Overload resolution | ✅ | Identity → widening → narrowing → object cost scale; same semantics in both modes |
| Generic type instantiation | ✅ | Closed generics via string specifier (e.g., `"System.Collections.Generic.List<string>"`) |
| Delegate parameters | ✅ | TS functions auto-marshaled to .NET delegates (`Action`, `Func<...>`, custom delegates) via `DotNetDelegateShim` |
| Event subscription (`+=` / `-=`) | ✅ | `obj.on(e)` / `obj.off(e)` style plus direct `+=` compound assignment in compiled mode; main-thread-only |
| Exception mapping | ✅ | .NET exceptions surface as JS-catchable errors with message preservation (`DotNetExceptionMapper`) |
| Value / reference type marshaling | ✅ | Primitives, strings, arrays, dictionaries; `DotNetMarshaller` centralizes conversion |
| External assembly discovery | ✅ | `TryResolveExternalType` scans loaded AppDomain assemblies; `sharpts.json`/`-r` assemblies load at startup so every seam sees them; compiled output co-locates used reference DLLs (+ closure) next to the output |
| `--gen-decl` discovery tool | ✅ | Inspect a type/namespace/assembly: faithful CLR signatures + per-member usable/unsupported marker + `dotnet:` import line + `--json` (issue #1194) |

Compiled mode uses late-bound reflection to the shim (`DotNetDelegateShim` / `DotNetEventBinder`) so the output DLL stays standalone. See `docs/dotnet-types.md` for the full guide.

`--gen-decl` (`DiscoveryGenerator` / `DiscoveryEmitter`) reports which members are usable from interop using `DotNetInteropClassifier` — the same by-ref/pointer/ref-struct/open-generic rules the runtime marshaller and the `dotnet:` import resolver enforce, so the tool and the runtime never disagree. It reports faithfully rather than emitting pasteable TypeScript (realistic BCL surfaces have `Span<T>`/`ref`/pointer members with no valid TS spelling). The `dotnet:` import line it prints is the primary consumption path; hand-written `@DotNetType(...) export declare class` declarations remain the manual alternative.

---

## 17. CONFORMANCE TEST SUITES

Two external corpora pin SharpTS against canonical references. Both run as standalone projects (not in `SharpTS.sln`); see each project's README for full details. Pass rates here are subset-relative — neither suite runs the full corpus today.

### TC39 Test262 (ECMA-262 / JavaScript spec)

`SharpTS.Test262/` runs a configurable subset of [test262](https://github.com/tc39/test262) in both interpreter and compiled-IL modes. Diff harness is committed-baseline-vs-current; hard-fails on regression or new-pass. As of 2026-04-20 the suite is at 10,132/10,132 pass on the configured subset (zero skips) — see `feedback_test_perf_changes.md` and the various `project_*_2026_04_*.md` memory entries for context.

### Microsoft TypeScript conformance (TS type-checker spec)

`SharpTS.TypeScriptConformance/` runs a subset of [microsoft/TypeScript's conformance corpus](https://github.com/microsoft/TypeScript/tree/main/tests/cases/conformance) and diffs our type-checker diagnostics against `tsc`'s `*.errors.txt` baselines. Pinned to TS v5.5.4. Pass classification is on `(line, tsCode)` tuples — see [#80](https://github.com/nickna/SharpTS/issues/80) for the tracking epic.

| Subset | Tests | Pass | Fail | ParseError | Skipped |
|---|---:|---:|---:|---:|---:|
| `types/typeRelationships/assignmentCompatibility/` | 70 | 16 (22.9%) | 50 | 4 | 0 |
| `types/conditional/` | 9 | 0 | 5 | 3 | 1 |
| **Total** | **79** | **16 (20.3%)** | **55** | **7** | **1** |

The parser is no longer the bottleneck. An extended parser sweep took the subset's `ParseError` count from 57 → 7 (≈86%): ambient `declare` of non-class declarations, `declare function`, generic/this/conditional/mapped/indexed-access/constructor/leading-operator types, `module Foo {}` namespaces, call/construct/index signatures, keyword & string/numeric property names, function-type and arrow optional/rest parameters, and more. Negative-test matching itself was unlocked by propagating canonical `TSnnnn` codes through the type-checker's error-recovery path ([#125](https://github.com/nickna/SharpTS/issues/125)), which also added a `strictNullChecks` option (default on; the runner follows each test's `@strict`/`@strictNullChecks` directive).

The dominant bucket is now `Fail` — tests that parse and reach the type checker but whose diagnostic set doesn't yet match `tsc`'s. These are mostly the `assignmentCompatibility` cases, which require deeper callable / constructable / structural assignability. The largest remaining levers are structural class-to-class assignability ([#129](https://github.com/nickna/SharpTS/issues/129) — SharpTS is nominal by design) and loading `tsc`'s `lib.*.d.ts` ([#99](https://github.com/nickna/SharpTS/issues/99)).

`Skipped` includes:
- **Multi-file tests** (`Skipped:multi-file-deferred`) — cross-file resolution into the runner is follow-up work.
- **Lib-drift skips** (`Skipped:lib-drift`) — tests where `tsc` expects "method missing" diagnostics our checker doesn't reproduce because we have the surface always-available regardless of `@lib`. Conservative filter (only fires when our diagnostic set is empty AND every expected code is one of `TS2339`/`TS2304`/`TS2551`/`TS7053`). See [#83](https://github.com/nickna/SharpTS/issues/83) for the design and [#99](https://github.com/nickna/SharpTS/issues/99) for the deferred Phase-1.5 work that would eliminate the drift entirely (load `tsc`'s `lib.*.d.ts` files into the type checker).
- **Directive skips** — tests with directives like `@experimentalDecorators`, `@jsx`, `@isolatedModules` we don't intend to honor in Phase 1.

---

## 18. PERFORMANCE (compiled output vs Node.js)

Epic [#856](https://github.com/nickna/SharpTS/issues/856) tracks closing the compiled-IL gap to Node.js on the cross-runtime benchmark suite (`benchmarks/scripts/`, run via `benchmarks/run-benchmarks.ps1`), **without** regressing .NET interop or language conformance (Test262 + `microsoft/TypeScript`). Warm steady-state, compiled vs Node at the largest input size:

| Workload | Status | vs Node |
|---|---|---|
| fibonacci | ✅ | **~2.4× faster** — recursion/call core |
| array-methods | ✅ | **~2× faster** — typed `List<double>` HOF pipeline ([#872](https://github.com/nickna/SharpTS/issues/872)) |
| strings | ✅ | **faster** (~0.9×) — `StringBuilder` accumulator promotion ([#870](https://github.com/nickna/SharpTS/issues/870)) + `charCodeAt` box-elision ([#873](https://github.com/nickna/SharpTS/issues/873)) |
| objects | ✅ | **parity** (1.00×) — object literals as shape structs ([#862](https://github.com/nickna/SharpTS/issues/862)) + cancel-throw codegen (below) |
| closures | ✅ | **parity** (~1.02×) — non-escaping local arrows de-virtualized to direct calls ([#858](https://github.com/nickna/SharpTS/issues/858)) |
| count-primes | ✅ | ~1.13× (sieve; `List<bool>` index-write bounds checks are the residual) |
| factorial | ✅ | ~1.22× (tight numeric loop at the codegen floor; V8 is ~0.2 ns/iter tighter; µs-scale) |

The original catastrophic gaps (14–117× slower) are closed and the suite now meets-or-beats Node on 5 of 7 workloads, with the other two within ~1.2×. Every win came from **re-exposing static types that the naive lowering erased** — boxing, `object`/`List<object>` representations, reflective dispatch, O(n²) string concat — so RyuJIT can optimize typed code. The emitter's job is to choose the algorithm/representation/dispatch and not erase known types; the JIT optimizes the typed ops it's given.

**Loop-backedge cancellation: throw, don't call (2026-06-21).** Every compiled loop polls a cooperative-cancellation flag at its backedge so the runner can unwind runaway loops (issue [#74](https://github.com/nickna/SharpTS/issues/74)). The flag test is an inlined `volatile.` field read ([#874](https://github.com/nickna/SharpTS/pull/874)); the **cold path** used to be `call $Runtime.CheckCancellation()` (a helper that throws internally). That was the dominant remaining gap on tight loops — **not** the flag read, which is free. From the JIT's flow-graph view `CheckCancellation()` is a *returning* call (its `throw` is conditional and internal), so the register allocator must assume control returns. On SysV x64 **every XMM register is caller-saved**, so a returning call inside a loop forces the loop-carried doubles (and counter) to be stack-resident across *every* iteration — a load/store per use. The backedge now emits `call $Runtime.BuildCancellationException(); throw` — a factory that only *constructs* the exception, then a real `throw` opcode. Because `throw` does not return, the loop vars are dead on the cancel path and stay in registers on the hot path. Measured **~1.8× on tight numeric loops**: objects 2.5×→parity, strings 1.26×→faster, factorial 2.27×→1.22×, count-primes 1.45×→1.13×. Cancellation semantics are unchanged (same `OperationCanceledException`, same message, thrown at the same point). This benefits **all** compiled loops, not just the benchmark suite.

Why earlier attempts plateaued: the inline-volatile form (#874) removed the unconditional *call overhead* but left the returning call in the loop's flow graph, so the XMM spill remained. A throttle-every-N-iterations variant ties it — reducing read frequency doesn't remove the call from the flow graph. The fix is structural: make the cancel path *non-returning* so the loop body carries no call at all.

The two remaining sub-parity workloads (count-primes ~1.13×, factorial ~1.22×) are at the codegen floor: factorial's loop already runs at the no-cancellation-check speed (V8 generates a marginally tighter multiply loop); count-primes' residual is `List<bool>` indexed-write bounds checking vs V8's packed-array elision.

---

## Breaking Changes (2026-04-18)

The embedded-stdlib migration removed implicit global bindings for several classes previously created as compile-time fallbacks. User code must now `import` these from the owning module explicitly (matches ESM-strict semantics and Node's own behavior):

| Class | Previous behavior | Required now |
|---|---|---|
| `URL`, `URLSearchParams` | Available as globals, backed by a pattern-matched `$URL` / `$URLSearchParams` emitter over `System.Uri`. | `import { URL, URLSearchParams } from 'url'` |
| `PerformanceObserver` | Available as a global, backed by `$Runtime.PerfHooksCreateObserver`. | `import { PerformanceObserver } from 'perf_hooks'` |
| `AsyncLocalStorage` | Available as a global, backed by `$AsyncLocalStorage`. | `import { AsyncLocalStorage } from 'async_hooks'` |

The underlying implementations are unchanged — only the import requirement is new. Behavior at the import site is identical to the previous global.

Additional behavioral divergence: `path.win32.isAbsolute('/foo')` returns `false` (matches WHATWG-like strictness — requires drive-letter + separator or UNC double-separator). Node returns `true` for any leading separator.

### Known stdlib workarounds (tracked debt)

The following stdlib TS files carry workarounds for compiler gaps that surfaced during migration. Behavior is correct at the documented API surface; listed here so future fixes can remove them:

- ~~`stdlib/node/process.ts` (`nextTick`) and `stdlib/node/timers.ts` — arity-dispatch across 8 positional args~~ (resolved: the built-in module emitters expand a trailing `Expr.Spread` at runtime via `EmitArgsArrayWithSpread`, #1149; both facades forward `...args` directly).
- `stdlib/node/async_hooks.ts` (`run`, `exit`) — drops the optional `...args` parameter; the underlying `SharpTSAsyncLocalStorage` still supports it. No current tests exercise this path.

(Resolved #925: parameter defaults — reference **and** value type — and unset optional reference params now round-trip faithfully through `$TSFunction.Invoke`. The old `param?: T` + `??` and `!= null` authoring workarounds have been retired from `stdlib/CONTRIBUTING.md`.)

### Resolved: guest-error identity (2026-04-18 regression → fully resolved 2026-06-24)

- Guest `throw` of a *string* value previously lost its `Error` identity when it crossed a host
  function frame: `ThrowException.FromResult` flattened a guest string throw to a plain CLR
  `Exception`, so a downstream `catch (e)` re-typed it (an error-prefixed string was bound as a
  typed `Error` instead of the verbatim string). The *same-scope* half was fixed in PR #906
  (`TimersPromises_SetInterval_AbortSignal_PreAborted` no longer skipped). The *cross-boundary*
  residual is now fixed: `ExecutionResult` carries a `FromGuestThrow` origin bit, threaded so
  `ThrowException.FromResult` keeps a guest string throw as an identity-carrying `ThrowException`
  (only genuinely host-translated strings stay a plain `Exception`), and the re-catch derives
  `fromHostException` from the exception kind. Guest string throws now survive plain function-call,
  host-callback, and Promise-executor boundaries verbatim in the interpreter — matching compiled
  mode and Node. See `SharpTS.Tests/SharedTests/CaughtErrorIdentityTests.cs`.

---

## Known Bugs

### IL Compiler Limitations

- ~~**Inner function declarations**~~ ✅ Inner function declarations (`function inner() {}` inside another function) are now fully supported with hoisting, closure capture, and recursion.

### Type Checker Limitations

- Type alias declarations are lazily validated - errors in type alias definitions (e.g., `type R = ReturnType<string, number>;` with wrong arg count) are only caught when the alias is used, not at declaration time. TypeScript catches these at declaration.

### Recently Fixed Bugs (2026-02-10)
- ~~`typeof` on undeclared variables throws~~ - Fixed: `typeof undeclaredVar` now returns `"undefined"` instead of throwing, matching JavaScript spec. Works in type checker, interpreter, and all compiler emitters (including async/generator state machines).

### Recently Fixed Bugs (2026-02-05)
- ~~Property descriptors in compiled mode~~ - Fixed: `Object.defineProperty()` and `Object.getOwnPropertyDescriptor()` now fully support accessor properties (get/set) and descriptor flags (writable, enumerable, configurable) in compiled mode via `PropertyDescriptorStore`.

### Recently Fixed Bugs (2026-02-04)
- ~~Spreading iterators~~ - Fixed: Type checker now allows spreading any iterable type (`[...arr.entries()]`, `[...mySet]`, `[...myMap]`, `[..."hello"]`, `[...generator()]`).
- ~~Destructuring in for...of declarations~~ - Fixed: Parser now supports `for (const [i, val] of arr.entries())` and `for (const {x, y} of items)`. Desugars to temp variable with body destructuring.
- ~~Method chaining on `new` expressions~~ - Fixed: Parser now correctly allows method chaining directly after `new` expressions (e.g., `new Date().toISOString()`)
- ~~String concatenation optimizer incorrect stringification~~ - Fixed: IL compiler's string concat optimizer now calls `Stringify()` instead of relying on .NET's `ToString()`, ensuring JavaScript-style output (`null` → "null", `true` → "true" not "True")

### Recently Fixed Bugs (2026-01-21)
- ~~Object literal accessor `this` type inference~~ - Fixed: `this` in getter/setter bodies now correctly infers the object's type instead of defaulting to `any`; uses two-pass type checking with literal type widening
- ~~`any + any` returning `bigint`~~ - Fixed: Binary operators now return `any` when either operand is `any`, not `bigint`
- ~~Spread properties ignored in compiled object literals with accessors~~ - Fixed: `MergeIntoTSObject` runtime method properly merges spread properties into `$Object` instances

### Recently Fixed Bugs (2026-01-13)
- ~~`yield await expr` NullReferenceException~~ - Fixed: State analyzer now assigns yield state before visiting nested await, matching emitter execution order
- ~~Generator variable capture for module-level variables~~ - Fixed: Generators correctly capture and use module-level variables including with `yield*`
- ~~Class expression constructors with default parameters~~ - Fixed: IL compiler now uses direct `newobj` with constructor builder instead of `Activator.CreateInstance`

### Recently Fixed Bugs (2026-01-12)
- ~~Generic types with array suffix~~ - Fixed: `ParseGenericTypeReference()` now properly finds matching `>` and handles array suffixes (`Partial<T>[]`, `Promise<number>[][]`, etc.)

### Recently Fixed Bugs (2026-01-06)
- ~~Math.round() JS parity~~ - Fixed: Now uses `Math.Floor(x + 0.5)` for JavaScript-compatible rounding (half-values toward +∞)
- ~~Object method `this` binding~~ - Fixed: `{ fn() { return this.x; } }` now correctly binds `this` in compiled code via `__this` parameter

---
