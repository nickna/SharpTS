# Using .NET Types from TypeScript

This guide covers using existing .NET types directly from TypeScript code. This enables you to leverage the full .NET Base Class Library (BCL) and third-party .NET libraries from your TypeScript programs.

## Overview

SharpTS supports two forms of .NET interop:

| Direction | Description | Use Case |
|-----------|-------------|----------|
| **Outbound** | Compile TypeScript to .NET DLLs | Consume TS libraries from C# |
| **Inbound** | Use .NET types from TypeScript | Access BCL and .NET libraries |

This guide covers **inbound interop** — calling .NET code from TypeScript. There are two ways to bind a .NET type:

| Binding | Description | When to use |
|---------|-------------|-------------|
| **`dotnet:` imports** (recommended) | `import { StringBuilder } from "dotnet:System.Text.StringBuilder"` — the type surface is synthesized from reflection automatically | The default. No boilerplate; can't drift from the real CLR type |
| **`@DotNetType` declarations** | Hand-written `declare class` bound to a CLR type | When you want a curated, hand-checked static surface, or need `@DotNetOverload` hints |

## Prerequisites

- .NET 10.0 SDK
- Decorators enabled (default, or pass `--experimentalDecorators` for Legacy mode)

`@DotNetType` works in **both** execution modes:

| Mode | Notes |
|------|-------|
| **Compiled** (`--compile`) | Overload resolution runs at compile time using TypeScript static types. IL-level `Callvirt`/`Newobj` directly invoke the resolved method. |
| **Interpreted** (default) | Overload resolution runs per call against the actual runtime argument types. Resolved `MethodInfo`s are cached on the wrapper so repeated calls avoid reflection lookup. |

Use `@DotNetOverload(...)` when runtime resolution can't pick the overload you want — see [Overload Hints](#overload-hints).

> **Note:** Decorators are enabled by default (TC39 Stage 3). Use `--experimentalDecorators` for Legacy (Stage 2) decorators, or `--noDecorators` to disable decorator support.

## Quick Start

```typescript
// Import a .NET type directly — no declaration needed
import { StringBuilder } from "dotnet:System.Text.StringBuilder";

// Use it like a native TypeScript class
let sb = new StringBuilder();
sb.append("Hello, ").append("World!");   // fluent chaining is statically typed
console.log(sb.toString());  // Output: Hello, World!
console.log(sb.length);      // .NET properties surface in camelCase: 13
```

Compile and run:
```bash
sharpts --compile example.ts
dotnet example.dll
```

---

## Importing .NET Types (`dotnet:` imports)

A `dotnet:` import specifier binds .NET types as first-class module imports. The static type
surface is synthesized directly from reflection metadata, so it always matches the real CLR
type, and members the interop layer cannot marshal (the same four categories `--gen-decl`
marks `[unsupported]`) are simply absent from the surface.

### Two specifier forms

```typescript
// Single-type form: the specifier is a fully-qualified type name
import { StringBuilder } from "dotnet:System.Text.StringBuilder";

// Namespace form: each named import resolves as Namespace.Name
import { Guid, DayOfWeek } from "dotnet:System";

// Aliasing works like any ES import (useful to avoid collisions, e.g. with global Math)
import { Math as SysMath } from "dotnet:System";

// Nested types resolve through their declaring type's specifier
import { SpecialFolder } from "dotnet:System.Environment";
```

### What you get

- **Construction, instance/static members, fields, enums** — `new Guid()`, `Guid.newGuid()`,
  `Guid.empty`, `DayOfWeek.Monday`. Member names follow the same camelCase convention as
  `@DotNetType` (both `append` and `Append` resolve).
- **Static typing** — primitives map to `number`/`string`/`boolean`, `void` maps to `void`,
  methods returning the type itself keep fluent chains typed; other .NET types surface as
  `any` (they still work — the runtime marshaller handles them dynamically).
- **Both execution modes** — the interpreter binds the same runtime wrapper `@DotNetType`
  uses; compiled mode resolves members at compile time and emits direct IL calls, so the
  output DLL stays fully standalone (no SharpTS.dll dependency).
- **Overload resolution** — cost-based, same as `@DotNetType`. If you need to force a
  specific overload with `@DotNetOverload`, use a `@DotNetType` declaration for that type
  instead; the two binding styles compose freely in one program.

### v1 scope and restrictions

- **Named imports only.** Default imports, `import * as ns`, `export … from "dotnet:…"`,
  `import x = require("dotnet:…")`, and dynamic `import("dotnet:…")` are rejected with a
  clear error — a .NET namespace is not an enumerable module object.
- **Loaded assemblies only.** Types resolve from the BCL and any assembly already loaded in
  the process (matching `@DotNetType`). A project-level assembly-reference story for
  third-party DLLs is tracked separately.
- **No generic types.** `dotnet:System.Collections.Generic.List<number>` is rejected; use an
  `@DotNetType` declaration with a closed-generic CLR name for those.
- Missing-member accesses follow SharpTS's standard class-instance semantics (same as
  hand-written classes).

Use `sharpts --gen-decl <Type|Namespace>` to discover what's available and copy the exact
import line — see [Discovering .NET Types](#discovering-net-types---gen-decl).

---

## Basic Usage

### The `@DotNetType` Decorator

The `@DotNetType` decorator binds a TypeScript class declaration to an existing .NET type:

```typescript
@DotNetType("Fully.Qualified.TypeName")
declare class TypeScriptName {
    // Method and property signatures
}
```

- **First argument**: The fully-qualified .NET type name (e.g., `System.Text.StringBuilder`)
- **`declare class`**: Indicates this is an external type with no implementation in TypeScript

### Declaring External Types

Use `declare class` to define the TypeScript interface for a .NET type. You only need to declare the members you intend to use:

```typescript
@DotNetType("System.Guid")
declare class Guid {
    static newGuid(): Guid;
    static parse(input: string): Guid;
    toString(): string;
}

// You don't need to declare every method - just what you use
let id = Guid.newGuid();
console.log(id.toString());
```

### Supported Member Types

| Member Type | TypeScript Syntax | Example |
|-------------|------------------|---------|
| Constructor | `constructor(params)` | `constructor(capacity: number)` |
| Instance method | `methodName(params): ReturnType` | `append(value: string): StringBuilder` |
| Static method | `static methodName(params): ReturnType` | `static newGuid(): Guid` |
| Instance property | `propertyName: Type` | `length: number` |
| Readonly property | `readonly propertyName: Type` | `readonly length: number` |
| Static property | `static propertyName: Type` | `static readonly now: DateTime` |

---

## Type Mapping

### TypeScript to .NET Type Conversion

When calling .NET methods, SharpTS automatically converts TypeScript types:

| TypeScript Type | .NET Type | Notes |
|-----------------|-----------|-------|
| `number` | `double` | Default numeric mapping |
| `number` | `int`, `long`, `float`, `byte` | Narrowing conversion when method expects it |
| `string` | `string` | Direct mapping |
| `boolean` | `bool` | Direct mapping |
| `object` | `object` | Dynamic fallback |

### Naming Conventions

TypeScript uses camelCase while .NET uses PascalCase. SharpTS handles this automatically:

| .NET Method | TypeScript Declaration |
|-------------|----------------------|
| `Append()` | `append()` |
| `GetValue()` | `getValue()` |
| `ToString()` | `toString()` |
| `NewGuid()` | `newGuid()` |

When you declare methods, use camelCase names. SharpTS resolves them to the PascalCase .NET equivalents.

### Overload Resolution

.NET methods often have multiple overloads. SharpTS uses cost-based resolution to select the best match:

```typescript
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    // Declare the overloads you need
    append(value: string): StringBuilder;
    append(value: number): StringBuilder;
    append(value: boolean): StringBuilder;
    toString(): string;
}

let sb = new StringBuilder();
sb.append("text");   // Calls Append(string)
sb.append(42);       // Calls Append(double)
sb.append(true);     // Calls Append(bool)
```

**Resolution priority** (lower cost = preferred):
1. Exact type match (e.g., `number` → `double`)
2. Lossless conversion (e.g., `number` → `float`)
3. Narrowing conversion (e.g., `number` → `int`)
4. Object fallback (any → `object`)

The same cost scale is used in both compiled and interpreted modes, so an overload
that resolves in one mode resolves the same way in the other — as long as the
argument types are unambiguous.

### Overload Hints

When a method has multiple overloads that a TypeScript call can't distinguish
(e.g., you want `ToInt32(int)` instead of the default `ToInt32(double)`), use
`@DotNetOverload("<signature>")` to pin the target signature:

```typescript
@DotNetType("System.Convert")
declare class Convert {
    // Without the hint, runtime picks ToInt32(double) for a TS number.
    // The hint narrows to ToInt32(int) — truncates 3.7 to 3 instead of
    // rounding to 4.
    @DotNetOverload("int")
    static toInt32(value: number): number;
}

console.log(Convert.toInt32(3.7)); // 3
```

The hint value is a comma-separated list of parameter types matching the
overload's signature. Recognized aliases: `int`, `long`, `short`, `byte`, `sbyte`,
`uint`, `ulong`, `ushort`, `float`/`single`, `double`, `decimal`, `bool`/`boolean`,
`char`, `string`, `object`, plus their `System.*` equivalents. Other types can
be named by their fully-qualified CLR name.

Use `@DotNetOverload("constructor-sig")` on a declared constructor to pin the
constructor overload similarly.

---

## Examples

### StringBuilder (Instance Methods and Chaining)

```typescript
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    append(value: string): StringBuilder;
    append(value: number): StringBuilder;
    append(value: boolean): StringBuilder;
    readonly length: number;
    toString(): string;
}

let sb = new StringBuilder();
sb.append("Name: ");
sb.append("Alice");
sb.append(", Age: ");
sb.append(30);
sb.append(", Active: ");
sb.append(true);

console.log(sb.toString());  // Name: Alice, Age: 30, Active: True
console.log(sb.length);      // 34
```

### Guid (Static Methods)

```typescript
@DotNetType("System.Guid")
declare class Guid {
    static newGuid(): Guid;
    static parse(input: string): Guid;
    static readonly empty: Guid;
    toString(): string;
}

let id = Guid.newGuid();
console.log(id.toString());  // e.g., "a1b2c3d4-..."

let parsed = Guid.parse("00000000-0000-0000-0000-000000000000");
console.log(parsed.toString());  // 00000000-0000-0000-0000-000000000000
```

### DateTime (Static Properties)

```typescript
@DotNetType("System.DateTime")
declare class DateTime {
    static readonly now: DateTime;
    static readonly utcNow: DateTime;
    static readonly today: DateTime;
    readonly year: number;
    readonly month: number;
    readonly day: number;
    readonly hour: number;
    readonly minute: number;
    toString(): string;
}

let now = DateTime.now;
console.log(now.year);   // e.g., 2024
console.log(now.month);  // e.g., 12
console.log(now.day);    // e.g., 25
```

### TimeSpan (Value Types)

```typescript
@DotNetType("System.TimeSpan")
declare class TimeSpan {
    static fromSeconds(value: number): TimeSpan;
    static fromMinutes(value: number): TimeSpan;
    static fromHours(value: number): TimeSpan;
    static fromDays(value: number): TimeSpan;
    add(ts: TimeSpan): TimeSpan;
    readonly totalSeconds: number;
    readonly totalMinutes: number;
    readonly totalHours: number;
    toString(): string;
}

let duration = TimeSpan.fromMinutes(90);
console.log(duration.totalHours);    // 1.5
console.log(duration.totalSeconds);  // 5400

let extra = TimeSpan.fromMinutes(30);
let total = duration.add(extra);
console.log(total.totalMinutes);     // 120
```

### Convert (Type Conversion)

```typescript
@DotNetType("System.Convert")
declare class Convert {
    static toInt32(value: number): number;
    static toInt32(value: string): number;
    static toDouble(value: string): number;
    static toBoolean(value: number): boolean;
    static toString(value: boolean): string;
}

let rounded = Convert.toInt32(42.7);      // 43
let parsed = Convert.toDouble("3.14159"); // 3.14159
let flag = Convert.toBoolean(1);          // true
let text = Convert.toString(true);        // "True"
```

### String.Format (Params Arrays)

```typescript
@DotNetType("System.String")
declare class String {
    static format(format: string, ...args: object[]): string;
    static concat(str0: string, str1: string): string;
    static isNullOrEmpty(value: string): boolean;
}

let message = String.format("Hello {0}, you have {1} messages!", "Alice", 5);
console.log(message);  // Hello Alice, you have 5 messages!

let formatted = String.format("{0} + {1} = {2}", 10, 20, 30);
console.log(formatted);  // 10 + 20 = 30
```

### Mixing External and Local Types

```typescript
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    append(value: string): StringBuilder;
    toString(): string;
}

// Regular TypeScript class
class Person {
    name: string;
    age: number;

    constructor(name: string, age: number) {
        this.name = name;
        this.age = age;
    }

    toFormattedString(): string {
        // Use .NET StringBuilder inside TypeScript class
        let sb = new StringBuilder();
        sb.append("Person { name: ");
        sb.append(this.name);
        sb.append(", age: ");
        sb.append(this.age.toString());
        sb.append(" }");
        return sb.toString();
    }
}

let person = new Person("Bob", 25);
console.log(person.toFormattedString());  // Person { name: Bob, age: 25 }
```

---

## Advanced Features

### Method Chaining

Methods that return `this` or the same type support chaining:

```typescript
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    append(value: string): StringBuilder;
    appendLine(): StringBuilder;
    appendLine(value: string): StringBuilder;
    toString(): string;
}

let result = new StringBuilder()
    .append("Line 1")
    .appendLine()
    .append("Line 2")
    .toString();
```

### Multiple External Types

You can declare and use multiple .NET types in the same file:

```typescript
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    append(value: string): StringBuilder;
    toString(): string;
}

@DotNetType("System.Guid")
declare class Guid {
    static newGuid(): Guid;
    toString(): string;
}

// Use both together
let sb = new StringBuilder();
sb.append("ID: ");
sb.append(Guid.newGuid().toString());
console.log(sb.toString());
```

### Properties vs Methods

.NET properties are accessed without parentheses, methods require them:

```typescript
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    readonly length: number;        // Property - access as sb.length
    toString(): string;             // Method - call as sb.toString()
}

let sb = new StringBuilder();
console.log(sb.length);      // Property access (no parentheses)
console.log(sb.toString());  // Method call (parentheses required)
```

---

## Discovering .NET Types (`--gen-decl`)

`--gen-decl` is an **interop discovery/inspection tool**: point it at a .NET type, namespace, or
assembly and it reports the real CLR signatures and which members SharpTS can actually use today.
It does *not* emit pasteable TypeScript source — realistic BCL types have `Span<T>`, `ref`/`out`,
and pointer members with no valid TypeScript spelling, so faithful description beats lossy codegen.
To bind a type in your program, hand-write a `@DotNetType` declaration (below) using the usable
members it reports.

### Inspect a type

```
sharpts --gen-decl System.Text.StringBuilder
```

```
System.Text.StringBuilder — class

  import { StringBuilder } from "dotnet:System.Text.StringBuilder";

  Constructors:
    [usable]      constructor()
    [usable]      constructor(capacity: int)
    [usable]      constructor(value: string)
  ...
  Instance methods:
    [usable]      append(value: string): StringBuilder
    [usable]      append(value: char[], startIndex: int, charCount: int): StringBuilder
    [unsupported] copyTo(sourceIndex: int, destination: Span<char>, count: int): void   — ref struct (Span/ReadOnlySpan) cannot cross the interop boundary
```

Each member is marked `[usable]` or `[unsupported]` using the **same rules the runtime interop
marshaller enforces**, so the tool and your program can never disagree about what's callable. The
four unsupported categories are by-ref (`ref`/`out`/`in`) parameters, pointer types, ref structs
(`Span<T>`/`ReadOnlySpan<T>`), and open generics. Signatures are shown with faithful .NET types
(`int`, `char[]`, `ReadOnlySpan<char>`, `out Guid`), not coerced into TypeScript.

> The `import { … } from "dotnet:…";` line is ready to copy into your program — see
> [Importing .NET Types](#importing-net-types-dotnet-imports). It resolves with the exact same
> usable/unsupported rules this tool reports.

### List a namespace or assembly

Passing a namespace or an assembly path prints a table of contents instead of member detail:

```
sharpts --gen-decl System.Text            # every loaded type in the namespace
sharpts --gen-decl ./MyLibrary.dll        # every public type in the assembly
```

### JSON output

Add `--json` for machine-readable output (e.g. to feed editor tooling):

```
sharpts --gen-decl System.Guid --json
sharpts --gen-decl System.Guid -o guid.txt   # or write to a file
```

### Type mapping for hand-written declarations

When you hand-write a `@DotNetType` declaration, map .NET types to TypeScript as follows:

| .NET Type | TypeScript Type |
|-----------|-----------------|
| `void` | `void` |
| `string` | `string` |
| `bool` | `boolean` |
| `int`, `long`, `double`, `float`, `decimal` | `number` |
| `object` | `unknown` |
| `DateTime` | `Date` |
| `Task` | `Promise<void>` |
| `Task<T>` | `Promise<T>` |
| `List<T>`, `T[]` | `T[]` |
| `Dictionary<K,V>` | `Map<K, V>` |
| `HashSet<T>` | `Set<T>` |
| `Nullable<T>` | `T \| null` |

---

## Limitations

The following .NET features are not currently supported:

| Feature | Status | Notes |
|---------|--------|-------|
| Generic types | Not supported | Cannot declare `List<T>` directly |
| `ref` / `out` parameters | Not supported | Methods with ref/out params cannot be called |
| Events | Supported (both modes) | Use `addEventListener`/`removeEventListener` — see [Events](#events) |
| Delegates | Supported (both modes) | TS functions auto-convert to delegate params — see [Delegates](#delegates-and-callbacks) |
| Indexers | Not supported | Cannot use `obj[index]` syntax |
| Operators | Not supported | Operator overloads not accessible |
| Extension methods | Not supported | Must call as static methods |
| Nullable value types | Partial | Generated as `T \| null` but runtime behavior varies |

### Workarounds

For unsupported features, consider:
1. Creating a C# wrapper class that exposes a simpler API
2. Using reflection-based interop via compiled TypeScript (see [.NET Integration Guide](dotnet-integration.md))

---

## Delegates and Callbacks

Any TypeScript function can be passed where a .NET method expects a delegate —
works in both **interpreter** and **compiled** modes.
The interpreter builds a shim on demand so the delegate's Invoke signature
round-trips through the TS callable:

```typescript
@DotNetType("System.Collections.Generic.List`1")
declare class IntList {
    constructor();
    add(item: number): void;
    forEach(action: (item: number) => void): void;  // Action<int>
    findAll(predicate: (item: number) => boolean): IntList;  // Predicate<int>
}

let items = new IntList();
items.add(1); items.add(2); items.add(3);
items.forEach((n) => console.log(n));
```

Supported delegate shapes:

| .NET type | TS shape |
|-----------|----------|
| `Action` | `() => void` |
| `Action<T1…>` | `(a: T1, …) => void` |
| `Func<TResult>` | `() => TResult` |
| `Func<T1…, TResult>` | `(a: T1, …) => TResult` |
| `Predicate<T>` | `(a: T) => boolean` |
| `EventHandler` | `(sender: any, args: any) => void` |
| `EventHandler<T>` | `(sender: any, payload: T) => void` |

Incoming .NET values are normalized for TypeScript on entry to the callback
(integral numerics → `number`, complex objects → wrapped instance). The return
value is converted back to the delegate's declared return type; a `throw` inside
the TS callback propagates synchronously to the .NET caller.

### Threading contract

> **Main-thread only.** Delegate shims run the TS callable *on whatever thread
> invoked the delegate*. The interpreter is not thread-safe, so invoking a shim
> off the SharpTS event-loop thread (e.g., from a `Timer`, a `Task` continuation,
> or a background thread) is **undefined behavior** — races, corrupted state, or
> crashes are possible.
>
> A future release may introduce an opt-in marshalling hint (e.g.,
> `@DotNetCallback("marshal")`) that hops off-thread invocations back to the
> event loop. Today, keep delegate sinks synchronous and on-thread.

---

## Events

Works in both **interpreter** and **compiled** modes.

TypeScript has no syntax for `+=` on .NET events, so SharpTS exposes a DOM-style
API on any `@DotNetType`-wrapped instance or class:

```typescript
@DotNetType("System.Timers.Timer")
declare class Timer {
    constructor(interval: number);
    start(): void;
    stop(): void;
    addEventListener(
        name: string,
        handler: (sender: any, args: any) => void
    ): void;
    removeEventListener(
        name: string,
        handler: (sender: any, args: any) => void
    ): void;
}
```

- Event names use the PascalCase .NET name (e.g., `"Elapsed"`, `"StringReceived"`).
- `addEventListener` looks up the `EventInfo` by name and wires a delegate shim.
- `removeEventListener` must receive the **same function reference** originally
  passed to `addEventListener` — the subscription is keyed by that reference so
  the underlying `RemoveEventHandler` call can find the matching shim.
- Static events work the same way: `ClassName.addEventListener("Name", handler)`.
- Subscribing the same `(name, handler)` pair twice is idempotent.

The threading contract above applies: if the .NET event fires from a background
thread, the handler will be invoked on that thread. Prefer event sources that
fire on the event-loop thread, or wrap `.NET` APIs in a helper that re-raises
events on the main thread.

---

## Exception Mapping

When a .NET method called through `@DotNetType` throws, SharpTS translates the
exception to a JavaScript-style error so `try`/`catch` in TypeScript works naturally.
The original `.NET` exception is preserved on `e.cause` for diagnostics.

| .NET exception | JS error (`e.name`) |
|----------------|---------------------|
| `ArgumentNullException` | `TypeError` |
| `ArgumentException` | `TypeError` |
| `InvalidCastException` | `TypeError` |
| `NullReferenceException` | `TypeError` |
| `ArgumentOutOfRangeException` | `RangeError` |
| `IndexOutOfRangeException` | `RangeError` |
| `OverflowException` | `RangeError` |
| `DivideByZeroException` | `RangeError` |
| `FormatException` | `SyntaxError` |
| *(everything else)* | `Error` |

`TargetInvocationException` is unwrapped before classification, so the mapped
error reflects the actual failure, not the reflection wrapper.

```typescript
@DotNetType("System.Guid")
declare class Guid {
    static parse(input: string): Guid;
}

try {
    Guid.parse("not-a-guid");
} catch (e) {
    console.log(e.name);     // "SyntaxError" (FormatException → SyntaxError)
    console.log(e.message);  // .NET's original message
    // e.cause holds the original System.FormatException
}
```

Currently the mapping is applied in interpreter mode. Compiled mode propagates
the raw .NET exception — `DotNetExceptionMapper.ClassifyAsJsErrorName` is a
public entry point so compiled-mode callers can opt into the same classification.

---

## Troubleshooting

### Type Not Found

**Error:** `.NET type 'X' not found`

- Ensure the type name is fully qualified (e.g., `System.Text.StringBuilder`, not `StringBuilder`)
- The type must be in an assembly loaded by the runtime (BCL types are always available)

### Method Not Found

**Error:** Method resolution fails at runtime

- Check that your camelCase declaration matches the PascalCase .NET method
- Verify the parameter types match what the .NET method expects
- Some .NET methods may have different overloads than expected

### Decorator Not Recognized

**Error:** `Unknown decorator: DotNetType`

- Decorators are enabled by default. If you used `--noDecorators`, remove that flag.
- `@DotNetType` is a built-in compiler decorator, not a user-defined one

### Type Not Found at Runtime

**Error:** `@DotNetType: .NET type '…' not found in any loaded assembly.`

In interpreted mode the type is resolved at the point the `declare class`
statement executes, from the set of assemblies currently loaded into the
process. If your type lives in a third-party assembly, make sure it's
loaded before the script runs — e.g., reference it from the host app or
`Assembly.LoadFrom` it up front.

---

## See Also

- [.NET Integration Guide](dotnet-integration.md) - Compiling TypeScript for C# consumption
- [Execution Modes](execution-modes.md) - Interpreted vs compiled mode details
- [Code Samples](code-samples.md) - TypeScript to C# mappings
