# SharpTS C# Interop Example

This example demonstrates how to consume SharpTS-compiled TypeScript assemblies from C# projects using runtime reflection.

## What This Example Shows

1. **Loading Assemblies**: Using `Assembly.LoadFrom()` to load compiled TypeScript
2. **Class Instantiation**: Creating TypeScript class instances with `Activator.CreateInstance()`
3. **Property Access**: Reading/writing properties via accessor methods (`get_X()`/`set_X()`)
4. **Method Calls**: Invoking instance and static methods
5. **Static Members**: Accessing static fields and methods
6. **Inheritance**: Working with class hierarchies and method overriding
7. **Top-level Functions**: Calling functions on the `$Program` class

## Building and Running

### Prerequisites

- .NET 10.0 SDK
- PowerShell (for the build script)

### Build and Run

```powershell
# From the SharpTS.Example.Interop directory:
.\build.ps1
```

The build script will:
1. Build SharpTS
2. Compile `TypeScript/Library.ts` to `CompiledTS/Library.dll`
3. Copy runtime dependencies
4. Build the C# consumer project
5. Run the example

### Expected Output

```
=== SharpTS C# Interop Example ===
Loading compiled TypeScript via reflection

Loaded assembly: Library, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null

--- 1. Class Instantiation and Methods ---
Created Person: Alice, age 30
Greeting: Hello, my name is Alice and I am 30 years old.
After birthday: age is now 31

--- 2. Property Access ---
Original name: Bob
After rename: Robert
After age update: 26

--- 3. Static Members ---
Calculator.PI = 3.14159
Calculator.add(10, 20) = 30
Calculator.multiply(5, 6) = 30

... (more output)

=== All demonstrations completed successfully! ===
```

## Project Structure

```
SharpTS.Example.Interop/
├── TypeScript/
│   └── Library.ts        # TypeScript source with example classes
├── CompiledTS/
│   ├── Library.dll       # Compiled TypeScript (generated)
│   └── SharpTS.dll       # Runtime dependency (copied)
├── Program.cs            # C# consumer demonstrating interop
├── SharpTS.Example.Interop.csproj
├── build.ps1             # Build automation script
└── README.md
```

## Usage Patterns

### Loading the Assembly

```csharp
var libraryPath = Path.Combine(AppContext.BaseDirectory, "Library.dll");
var assembly = Assembly.LoadFrom(libraryPath);
```

### Creating Instances

```csharp
var personType = assembly.GetType("Person")!;
var person = Activator.CreateInstance(personType, "Alice", 30.0)!;
```

### Calling Methods

```csharp
var greet = personType.GetMethod("greet")!;
string greeting = (string)greet.Invoke(person, null)!;
```

### Property Access (via accessor methods)

```csharp
// Get property
var getName = personType.GetMethod("get_name")!;
string name = (string)getName.Invoke(person, null)!;

// Set property
var setName = personType.GetMethod("set_name")!;
setName.Invoke(person, ["Robert"]);
```

### Static Members

```csharp
var calcType = assembly.GetType("Calculator")!;

// Static field
var piField = calcType.GetField("PI", BindingFlags.Public | BindingFlags.Static)!;
double pi = (double)piField.GetValue(null)!;

// Static method
var addMethod = calcType.GetMethod("add", BindingFlags.Public | BindingFlags.Static)!;
object sum = addMethod.Invoke(null, [10.0, 20.0])!;
```

### Top-level Functions

```csharp
// Top-level functions live on the $Program class
var programType = assembly.GetType("$Program")!;
var formatMethod = programType.GetMethod("formatMessage", BindingFlags.Public | BindingFlags.Static)!;
object result = formatMethod.Invoke(null, ["INFO", "Hello"])!;
```

## Type Mapping

SharpTS compiles TypeScript types to .NET types as follows:

| TypeScript | .NET Runtime Type |
|------------|-------------------|
| `number`   | `double`          |
| `string`   | `string`          |
| `boolean`  | `bool`            |
| `Promise<T>` | `Task<object>`  |
| `T[]`      | `List<object>`    |

**Note:** Method parameters and return values are typed as `object` for flexibility, so casting is required.

## Compiled Assembly Structure

When SharpTS compiles TypeScript, it produces:

- **Classes**: .NET classes in the root namespace with original names
- **Properties**: Accessor methods (`get_X()` and `set_X(value)`)
- **Methods**: Instance/static methods with `object` signatures
- **Top-level functions**: Static methods on `$Program` class
- **Async functions**: Return `Task<object>`
- **Static fields**: Public static fields on the class

## Exploring the Compiled DLL

You can inspect the compiled DLL with tools like:
- ILSpy
- dotPeek
- `ildasm` (IL Disassembler)

This reveals the exact structure of the compiled TypeScript for debugging or advanced interop scenarios.

## Current Limitations

1. **Reflection Required**: Types must be accessed via reflection rather than direct compile-time references
2. **Object Return Types**: Methods return `object`, requiring casts
3. **Accessor Methods for Properties**: Use `get_X()`/`set_X()` instead of direct property syntax
4. **`$Program` Class**: Top-level functions use `$` which requires reflection to access

## Future: Typed Assembly Output

SharpTS is working on a `--ref-asm` flag that will produce assemblies compatible with compile-time references, enabling natural C# syntax:

```csharp
// Future with --ref-asm:
var person = new Person("Alice", 30.0);    // Direct instantiation
Console.WriteLine(person.name);              // Direct property access
string greeting = person.greet();            // Typed return values
```

This feature has known limitations with async code and is under development.
