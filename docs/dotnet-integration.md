# .NET Integration Guide

This guide covers compiling TypeScript to .NET DLLs and consuming them from C# projects. Whether you're building TypeScript libraries for .NET applications or integrating TypeScript logic into existing C# codebases, this document provides practical workflows and patterns.

> **Looking for the reverse?** To use existing .NET types (like `StringBuilder`, `Guid`, `DateTime`) from TypeScript, see [Using .NET Types from TypeScript](dotnet-types.md).

## Prerequisites

- .NET 10.0 SDK
- SharpTS CLI (installed globally or built from source)

## Quick Reference

| Compilation Mode | Command | C# Access Pattern | Best For |
|-----------------|---------|-------------------|----------|
| Standard | `sharpts --compile lib.ts` | Reflection (`Assembly.LoadFrom`) | Dynamic loading, plugins |
| Reference Assembly | `sharpts --compile lib.ts --ref-asm` | Direct compile-time reference | Strong typing, IntelliSense |

---

## Understanding Compiled Output

### Generated Files

When you compile a TypeScript file, SharpTS produces:

| File | Purpose |
|------|---------|
| `<name>.dll` | The .NET assembly containing your compiled TypeScript |
| `<name>.runtimeconfig.json` | Runtime configuration for .NET 10.0 |
| `SharpTS.dll` | Runtime dependency (automatically copied to output directory) |

### Type Mapping

TypeScript types map to .NET types as follows:

| TypeScript | .NET Type | Notes |
|------------|-----------|-------|
| `number` | `double` | All numbers are IEEE 754 doubles |
| `string` | `string` | Standard .NET strings |
| `boolean` | `bool` | Boolean type |
| `bigint` | `BigInteger` | Arbitrary precision integers |
| `void` | `void` | No return value |
| `any`, `unknown` | `object` | Dynamic types |
| `null` | `object` | Null reference |
| `T[]` | `List<T>` | Typed lists (with `--ref-asm`) |
| `Promise<T>` | `Task<T>` | Async support |
| `Map<K,V>` | `Dictionary<K,V>` | Key-value collections |
| `Set<T>` | `HashSet<T>` | Unique collections |
| `Date` | `DateTime` | Date/time values |
| `RegExp` | `Regex` | Regular expressions |
| Classes | .NET classes | Same name as TypeScript class |

### Assembly Structure

The compiled assembly organizes code as follows:

- **Classes**: Emitted as .NET classes (root namespace by default, or custom namespace via `@Namespace`)
- **Top-level functions**: Static methods on the `$Program` class
- **Properties**: Accessor methods `get_X()` and `set_X(value)`
- **Static members**: .NET static fields and methods
- **Constructors**: Standard .NET constructors matching TypeScript signatures

### Custom .NET Namespaces

Use the `@Namespace` decorator to place compiled types in a specific .NET namespace:

```typescript
@Namespace("MyCompany.Libraries")
class Person {
    name: string;
    constructor(name: string) {
        this.name = name;
    }
}

class Employee extends Person {
    department: string;
    constructor(name: string, department: string) {
        super(name);
        this.department = department;
    }
}
```

Both `Person` and `Employee` will be emitted in the `MyCompany.Libraries` namespace.

**Key points:**
- The decorator applies file-wide (all classes in the file use the same namespace)
- Requires `--decorators` flag during compilation
- Nested namespaces supported: `@Namespace("MyCompany.Libraries.Data")`
- Without the decorator, classes are emitted at the root namespace (backward compatible)

---

## Project Organization

### Recommended Solution Structure

```
MySolution/
├── MyApp.TypeScript/           # TypeScript source files
│   ├── src/
│   │   ├── models/
│   │   │   └── Person.ts
│   │   └── utils/
│   │       └── Calculator.ts
│   └── compiled/               # Compilation output
│       ├── Library.dll
│       ├── Library.runtimeconfig.json
│       └── SharpTS.dll
├── MyApp.Consumer/             # C# consumer project
│   ├── MyApp.Consumer.csproj
│   └── Program.cs
└── MySolution.sln
```

### Single-File vs Multi-Module

- **Single-file**: One `.ts` file compiles to one `.dll`
- **Multi-module**: Entry point file with `import`/`export` compiles all dependencies into a single `.dll`

---

## Manual CLI Workflow

### Basic Compilation

```bash
# Basic compilation
sharpts --compile Library.ts

# Custom output path
sharpts --compile Library.ts -o bin/Library.dll

# With IL verification (recommended for catching issues early)
sharpts --compile Library.ts --verify
```

### Reference Assembly Mode

For compile-time C# references with IntelliSense support:

```bash
# Enable reference-assembly-compatible output
sharpts --compile Library.ts --ref-asm

# Full production build
sharpts --compile Library.ts --ref-asm --verify -o dist/Library.dll
```

### CLI Options Reference

| Option | Description |
|--------|-------------|
| `--compile` / `-c` | Enable compilation mode |
| `-o <path>` | Set output path (default: `<input>.dll`) |
| `--ref-asm` | Emit reference-assembly-compatible output |
| `--sdk-path <path>` | Explicit path to .NET SDK reference assemblies |
| `--verify` | Verify emitted IL using Microsoft.ILVerification |
| `--preserveConstEnums` | Keep const enum declarations in output |
| `--pack` | Generate NuGet package after compilation |
| `--push <source>` | Push package to NuGet feed |
| `--api-key <key>` | API key for NuGet push |
| `--package-id <id>` | Override package ID |
| `--version <ver>` | Override package version |

---

## NuGet Package Distribution

SharpTS can generate NuGet packages from compiled TypeScript libraries, making it easy to distribute TypeScript code for .NET consumption.

### Package Metadata

Package metadata is read from `package.json` in the source directory:

```json
{
  "name": "my-typescript-lib",
  "version": "1.0.0",
  "description": "My TypeScript library for .NET",
  "author": "Your Name",
  "license": "MIT",
  "keywords": ["typescript", "library"],
  "repository": {
    "url": "https://github.com/user/my-typescript-lib"
  }
}
```

### Creating Packages

```bash
# Basic package creation (uses package.json metadata)
sharpts --compile Library.ts --pack

# Output: Library.1.0.0.nupkg + Library.1.0.0.snupkg (symbols)

# Override version for pre-release
sharpts --compile Library.ts --pack --version 2.0.0-beta

# Custom package ID
sharpts --compile Library.ts --pack --package-id "MyCompany.Library"
```

### Publishing to NuGet

```bash
# Push to nuget.org
sharpts --compile Library.ts --pack \
  --push https://api.nuget.org/v3/index.json \
  --api-key $NUGET_API_KEY

# Push to private feed
sharpts --compile Library.ts --pack \
  --push https://pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json \
  --api-key $AZURE_PAT
```

### Package Contents

Generated packages include:

| Path | Content |
|------|---------|
| `lib/net10.0/<name>.dll` | Compiled assembly |
| `lib/net10.0/<name>.runtimeconfig.json` | Runtime configuration |
| `README.md` | Package readme (if present in source directory) |

### CI/CD Integration

```yaml
# GitHub Actions example
- name: Build and Publish
  run: |
    sharpts --compile src/Library.ts \
      --pack \
      --version ${{ github.ref_name }} \
      --push https://api.nuget.org/v3/index.json \
      --api-key ${{ secrets.NUGET_API_KEY }}
```

---

## MSBuild Integration

### Recommended: SharpTS.Sdk

The easiest way to integrate SharpTS into your build is using the MSBuild SDK:

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

This provides automatic compilation, tsconfig.json integration, and proper Clean support. See the [MSBuild SDK Guide](msbuild-sdk.md) for full documentation.

### Alternative: Manual Pre-Build Target

If you need more control or can't use the SDK, add a pre-build target to your `.csproj`:

```xml
<Target Name="CompileTypeScript" BeforeTargets="Build">
  <Exec Command="sharpts --compile $(ProjectDir)TypeScript\Library.ts -o $(ProjectDir)CompiledTS\Library.dll" />
</Target>
```

### Multiple TypeScript Files

```xml
<ItemGroup>
  <TypeScriptFile Include="TypeScript\**\*.ts" />
</ItemGroup>

<Target Name="CompileTypeScript" BeforeTargets="Build"
        Inputs="@(TypeScriptFile)"
        Outputs="$(ProjectDir)CompiledTS\%(TypeScriptFile.Filename).dll">
  <MakeDir Directories="$(ProjectDir)CompiledTS" />
  <Exec Command="sharpts --compile %(TypeScriptFile.Identity) -o $(ProjectDir)CompiledTS\%(TypeScriptFile.Filename).dll" />
</Target>
```

### Referencing Compiled DLLs

**For reflection-based loading** (copy to output):

```xml
<ItemGroup>
  <None Include="CompiledTS\Library.dll" CopyToOutputDirectory="PreserveNewest" />
  <None Include="CompiledTS\SharpTS.dll" CopyToOutputDirectory="PreserveNewest" />
</ItemGroup>
```

**For compile-time reference** (requires `--ref-asm`):

```xml
<ItemGroup>
  <Reference Include="Library">
    <HintPath>CompiledTS\Library.dll</HintPath>
  </Reference>
</ItemGroup>
```

### Complete .csproj Example

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <!-- Pre-build: Compile TypeScript -->
  <Target Name="CompileTypeScript" BeforeTargets="Build">
    <MakeDir Directories="$(ProjectDir)CompiledTS" />
    <Exec Command="sharpts --compile $(ProjectDir)TypeScript\Library.ts -o $(ProjectDir)CompiledTS\Library.dll --ref-asm" />
  </Target>

  <!-- Copy compiled DLLs to output -->
  <ItemGroup>
    <None Include="CompiledTS\Library.dll"
          CopyToOutputDirectory="PreserveNewest"
          Condition="Exists('CompiledTS\Library.dll')" />
    <None Include="CompiledTS\SharpTS.dll"
          CopyToOutputDirectory="PreserveNewest"
          Condition="Exists('CompiledTS\SharpTS.dll')" />
  </ItemGroup>
</Project>
```

---

## Consuming from C# - Reflection API

The reflection API works with standard compilation and provides maximum flexibility.

### Loading the Assembly

```csharp
using System.Reflection;

var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Library.dll");
var assembly = Assembly.LoadFrom(assemblyPath);
```

### Creating Instances

```csharp
var personType = assembly.GetType("Person")!;
var person = Activator.CreateInstance(personType, "Alice", 30.0)!;
```

### Property Access

Properties are emitted as real .NET properties with PascalCase names:

```csharp
// Get property value
var nameProp = personType.GetProperty("Name")!;
string name = (string)nameProp.GetValue(person)!;

// Set property value
nameProp.SetValue(person, "Robert");

// Or access multiple properties
var ageProp = personType.GetProperty("Age")!;
double age = (double)ageProp.GetValue(person)!;
ageProp.SetValue(person, 31.0);
```

**Note:** TypeScript property names are converted to PascalCase (e.g., `firstName` becomes `FirstName`).

### Method Invocation

**Instance methods:**

```csharp
var greet = personType.GetMethod("greet")!;
string greeting = (string)greet.Invoke(person, null)!;
```

**Static methods:**

```csharp
var calcType = assembly.GetType("Calculator")!;
var addMethod = calcType.GetMethod("add", BindingFlags.Public | BindingFlags.Static)!;
object sum = addMethod.Invoke(null, [10.0, 20.0])!;
```

### Static Fields

```csharp
var piField = calcType.GetField("PI", BindingFlags.Public | BindingFlags.Static)!;
double pi = (double)piField.GetValue(null)!;
```

### Top-Level Functions

Top-level functions are compiled to static methods on the `$Program` class:

```csharp
var programType = assembly.GetType("$Program")!;
var formatMessage = programType.GetMethod("formatMessage", BindingFlags.Public | BindingFlags.Static)!;
object formatted = formatMessage.Invoke(null, ["INFO", "Test message"])!;
```

### Working with Inheritance

```csharp
// Base class
var animalType = assembly.GetType("Animal")!;
var animal = Activator.CreateInstance(animalType, "Generic Animal")!;

// Derived class with overridden methods
var dogType = assembly.GetType("Dog")!;
var dog = Activator.CreateInstance(dogType, "Rex", "Golden Retriever")!;

var speak = dogType.GetMethod("speak")!;
string sound = (string)speak.Invoke(dog, null)!;  // "Rex barks!"
```

---

## Consuming from C# - Direct Reference

When compiled with `--ref-asm`, the DLL can be referenced at compile-time.

### When to Use

- You need IntelliSense support in your IDE
- You want compile-time type checking
- You're building a tightly-coupled integration

### How It Works

Standard compilation references `System.Private.CoreLib` (runtime-only). The `--ref-asm` flag rewrites references to SDK assemblies (`System.Runtime`, `System.Collections`, etc.), enabling compile-time usage.

### Current Limitations

- Top-level functions are on `$Program` (the `$` is valid in IL)
- Some async patterns may have edge cases

---

## Best Practices

### Create Wrapper Classes

For cleaner APIs, wrap reflection calls:

```csharp
public class PersonWrapper
{
    private readonly object _instance;
    private readonly Type _type;
    private readonly PropertyInfo _nameProp;
    private readonly PropertyInfo _ageProp;
    private readonly MethodInfo _greet;

    public PersonWrapper(Assembly assembly, string name, double age)
    {
        _type = assembly.GetType("Person")!;
        _instance = Activator.CreateInstance(_type, name, age)!;
        _nameProp = _type.GetProperty("Name")!;
        _ageProp = _type.GetProperty("Age")!;
        _greet = _type.GetMethod("greet")!;
    }

    public string Name
    {
        get => (string)_nameProp.GetValue(_instance)!;
        set => _nameProp.SetValue(_instance, value);
    }

    public double Age
    {
        get => (double)_ageProp.GetValue(_instance)!;
        set => _ageProp.SetValue(_instance, value);
    }

    public string Greet() => (string)_greet.Invoke(_instance, null)!;
}
```

### Cache Reflection Metadata

```csharp
private static readonly Dictionary<string, Type> _typeCache = new();
private static readonly Dictionary<(Type, string), MethodInfo> _methodCache = new();

public static Type GetCachedType(Assembly asm, string name)
{
    if (!_typeCache.TryGetValue(name, out var type))
    {
        type = asm.GetType(name)!;
        _typeCache[name] = type;
    }
    return type;
}
```

### Handle Errors Properly

```csharp
try
{
    var assembly = Assembly.LoadFrom(dllPath);
    var type = assembly.GetType("MyClass")
        ?? throw new TypeLoadException("MyClass not found");
}
catch (FileNotFoundException ex)
{
    Console.WriteLine($"Assembly not found: {ex.FileName}");
}
catch (TargetInvocationException ex)
{
    // TypeScript runtime error wrapped in TargetInvocationException
    Console.WriteLine($"TypeScript error: {ex.InnerException?.Message}");
}
```

### Working with Async Methods

```csharp
var asyncMethod = type.GetMethod("fetchData")!;
var task = (Task<object>)asyncMethod.Invoke(instance, null)!;
var result = await task;
```

---

## Build Automation

### PowerShell Script

```powershell
$ErrorActionPreference = "Stop"

# Compile TypeScript
$tsInput = "TypeScript\Library.ts"
$dllOutput = "CompiledTS\Library.dll"

Write-Host "Compiling TypeScript..."
sharpts --compile $tsInput -o $dllOutput --ref-asm --verify

# Build C# consumer
Write-Host "Building C# project..."
dotnet build

# Run
Write-Host "Running application..."
dotnet run
```

### Bash Script

```bash
#!/bin/bash
set -e

echo "Compiling TypeScript..."
sharpts --compile TypeScript/Library.ts -o CompiledTS/Library.dll --ref-asm --verify

echo "Building C# project..."
dotnet build

echo "Running application..."
dotnet run
```

---

## Troubleshooting

### Assembly Load Failures

**Error:** `FileNotFoundException: Could not load file or assembly`

- Ensure `SharpTS.dll` is in the same directory as the compiled TypeScript DLL
- Check that `<name>.runtimeconfig.json` exists alongside the DLL

### Type Not Found

**Error:** `GetType()` returns `null`

- Classes are in the root namespace by default (no namespace prefix needed)
- If `@Namespace("X.Y")` was used, include the namespace: `assembly.GetType("X.Y.ClassName")`
- Top-level functions are on `$Program` class
- Multi-module compilation uses qualified names: `$M_ModuleName_ClassName`

### Method/Property Not Found

**Error:** `GetMethod()` or `GetProperty()` returns `null`

- Properties use PascalCase names: `name` becomes `Name`, `firstName` becomes `FirstName`
- Check `BindingFlags.Static` vs `BindingFlags.Instance`
- Include `BindingFlags.Public` for public members

### IL Verification Errors

**Error:** IL verification fails with `--verify`

- This indicates a compiler issue - please report with source code
- Compile without `--verify` as a workaround

### Reference Assembly Errors

**Error:** `System.Private.CoreLib not found` when referencing DLL

- Use `--ref-asm` flag during compilation
- This rewrites assembly references to SDK assemblies

### Parameter Type Mismatches

**Error:** `ArgumentException` when calling methods

- All `number` parameters expect `double`, not `int`
- Pass `30.0` instead of `30`
- Cast appropriately before invoking

---

## Complete Example

### TypeScript Source (`Library.ts`)

```typescript
class Person {
    name: string;
    age: number;

    constructor(name: string, age: number) {
        this.name = name;
        this.age = age;
    }

    greet(): string {
        return "Hello, my name is " + this.name + " and I am " + this.age + " years old.";
    }

    haveBirthday(): void {
        this.age = this.age + 1;
    }
}

class Calculator {
    static PI: number = 3.14159;

    static add(a: number, b: number): number {
        return a + b;
    }

    static multiply(a: number, b: number): number {
        return a * b;
    }
}

function formatMessage(prefix: string, message: string): string {
    return "[" + prefix + "] " + message;
}
```

### Compilation

```bash
sharpts --compile Library.ts -o CompiledTS/Library.dll --ref-asm --verify
```

### C# Consumer (`Program.cs`)

```csharp
using System.Reflection;

Console.WriteLine("=== SharpTS C# Interop Example ===");

// Load the compiled TypeScript assembly
var assemblyPath = Path.Combine(AppContext.BaseDirectory, "Library.dll");
var assembly = Assembly.LoadFrom(assemblyPath);

// 1. Create a Person instance
var personType = assembly.GetType("Person")!;
var person = Activator.CreateInstance(personType, "Alice", 30.0)!;

// 2. Access properties (using PascalCase names)
var nameProp = personType.GetProperty("Name")!;
var ageProp = personType.GetProperty("Age")!;
Console.WriteLine($"Person: {nameProp.GetValue(person)}, age {ageProp.GetValue(person)}");

// 3. Call instance method
var greet = personType.GetMethod("greet")!;
Console.WriteLine(greet.Invoke(person, null));

// 4. Call static method
var calcType = assembly.GetType("Calculator")!;
var add = calcType.GetMethod("add", BindingFlags.Public | BindingFlags.Static)!;
Console.WriteLine($"Calculator.add(10, 20) = {add.Invoke(null, [10.0, 20.0])}");

// 5. Access static field
var piField = calcType.GetField("PI", BindingFlags.Public | BindingFlags.Static)!;
Console.WriteLine($"Calculator.PI = {piField.GetValue(null)}");

// 6. Call top-level function
var programType = assembly.GetType("$Program")!;
var format = programType.GetMethod("formatMessage", BindingFlags.Public | BindingFlags.Static)!;
Console.WriteLine(format.Invoke(null, ["INFO", "Integration complete!"]));
```

### Expected Output

```
=== SharpTS C# Interop Example ===
Person: Alice, age 30
Hello, my name is Alice and I am 30 years old.
Calculator.add(10, 20) = 30
Calculator.PI = 3.14159
[INFO] Integration complete!
```

---

## See Also

- [Using .NET Types from TypeScript](dotnet-types.md) - Use BCL and .NET libraries directly from TypeScript
- [MSBuild SDK Guide](msbuild-sdk.md) - Integrate SharpTS into your .NET build process
- [Execution Modes](execution-modes.md) - Interpreted vs compiled mode details
- [Code Samples](code-samples.md) - TypeScript to C# mappings
- [Examples/Interop](../Examples/Interop/) - Complete working example project
