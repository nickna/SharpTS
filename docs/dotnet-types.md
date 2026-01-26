# Using .NET Types from TypeScript

This guide covers using existing .NET types directly from TypeScript code. This enables you to leverage the full .NET Base Class Library (BCL) and third-party .NET libraries from your TypeScript programs.

## Overview

SharpTS supports two forms of .NET interop:

| Direction | Description | Use Case |
|-----------|-------------|----------|
| **Outbound** | Compile TypeScript to .NET DLLs | Consume TS libraries from C# |
| **Inbound** | Use .NET types from TypeScript | Access BCL and .NET libraries |

This guide covers **inbound interop** - calling .NET code from TypeScript using the `@DotNetType` decorator.

## Prerequisites

- .NET 10.0 SDK
- Compilation mode (`--compile`) - this feature is available only in compiled execution

> **Note:** Decorators are enabled by default (TC39 Stage 3). Use `--experimentalDecorators` for Legacy (Stage 2) decorators, or `--noDecorators` to disable decorator support.

## Quick Start

```typescript
// Declare an external .NET type
@DotNetType("System.Text.StringBuilder")
declare class StringBuilder {
    constructor();
    append(value: string): StringBuilder;
    toString(): string;
}

// Use it like a native TypeScript class
let sb = new StringBuilder();
sb.append("Hello, ");
sb.append("World!");
console.log(sb.toString());  // Output: Hello, World!
```

Compile and run:
```bash
sharpts --compile example.ts
dotnet example.dll
```

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

## Generating Declarations

SharpTS can auto-generate TypeScript declarations from .NET types using the `DeclarationGenerator`:

### From Individual Types

```csharp
var generator = new DeclarationGenerator();
string declaration = generator.GenerateForType("System.Text.StringBuilder");
Console.WriteLine(declaration);
```

Output:
```typescript
@DotNetType("System.Text.StringBuilder")
export declare class StringBuilder {
    constructor();
    constructor(capacity: number);
    constructor(value: string);
    append(value: string): StringBuilder;
    append(value: number): StringBuilder;
    append(value: boolean): StringBuilder;
    appendLine(): StringBuilder;
    appendLine(value: string): StringBuilder;
    insert(index: number, value: string): StringBuilder;
    remove(startIndex: number, length: number): StringBuilder;
    replace(oldValue: string, newValue: string): StringBuilder;
    clear(): StringBuilder;
    toString(): string;
    readonly length: number;
    readonly capacity: number;
}
```

### Type Mapping in Generated Declarations

The generator automatically maps .NET types to TypeScript:

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
| Events | Not supported | Cannot subscribe to .NET events |
| Delegates | Not supported | Cannot pass callbacks to .NET |
| Indexers | Not supported | Cannot use `obj[index]` syntax |
| Operators | Not supported | Operator overloads not accessible |
| Extension methods | Not supported | Must call as static methods |
| Nullable value types | Partial | Generated as `T \| null` but runtime behavior varies |

### Workarounds

For unsupported features, consider:
1. Creating a C# wrapper class that exposes a simpler API
2. Using reflection-based interop via compiled TypeScript (see [.NET Integration Guide](dotnet-integration.md))

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

### Compilation Only

**Error:** Feature not available in interpreted mode

- The `@DotNetType` feature requires compilation (`--compile` flag)
- Interpreted mode does not support external .NET type bindings

---

## See Also

- [.NET Integration Guide](dotnet-integration.md) - Compiling TypeScript for C# consumption
- [Execution Modes](execution-modes.md) - Interpreted vs compiled mode details
- [Code Samples](code-samples.md) - TypeScript to C# mappings
