# Consume compiled TypeScript from C#

SharpTS can emit a normal .NET assembly whose public TypeScript classes and functions are callable
from C#. This guide covers the consumer boundary. For command-line flags use
[Execution modes](execution-modes.md); for repeatable project builds use the canonical
[MSBuild SDK guide](msbuild-sdk.md).

## Compile the library

Given `Library.ts`:

```typescript
@Namespace("Acme.TypeScript")
class Calculator {
  static add(left: number, right: number): number {
    return left + right;
  }
}

function formatMessage(prefix: string, message: string): string {
  return `[${prefix}] ${message}`;
}
```

emit reference-compatible output:

```bash
sharpts --compile Library.ts -o CompiledTS/Library.dll --ref-asm --verify
```

`--ref-asm` rewrites framework references to the SDK reference-assembly shape so a C# compiler can
reference the result. It is unnecessary when the consumer only loads the assembly with reflection.
For build automation, configure the same behavior with `SharpTSUseReferenceAssemblies` in
`SharpTS.Sdk`; do not duplicate a custom pre-build command.

## Understand the output

| Output | Purpose |
| --- | --- |
| `Library.dll` | Compiled TypeScript classes, `$Program`, and the emitted JavaScript runtime |
| `Library.runtimeconfig.json` | .NET runtime selection when the assembly is launched directly |
| `Library.pdb` | Optional TypeScript-source debug symbols when compiled with `--debug` |
| External reference DLLs | Copied when the emitted program has hard references to selected external assemblies |
| `SharpTS.dll` | Copied only when emitted soft-dependent features need the managed SharpTS runtime |

Ordinary compiled TypeScript is self-contained inside `Library.dll`; `SharpTS.dll` is not an
unconditional runtime dependency. Compiled `eval`, selected Proxy/Intl/`vm`/DNS paths, and dynamic
.NET event support can create a soft dependency. The compiler records those requirements and
co-locates `SharpTS.dll` only when needed. `--standalone` suppresses the copy but does not make such
a feature independent; the application must provide the runtime or accept the documented runtime
error.

External assemblies referenced through `sharpts.json` or `-r` are different: the emitted metadata
can hard-reference them. Deploy the copied dependency closure with the TypeScript assembly.

## Public assembly shape

- TypeScript classes become .NET classes in the root namespace by default.
- `@Namespace("Acme.TypeScript")` applies a .NET namespace to the file's emitted classes.
- Top-level functions are public static methods on `$Program`.
- Class methods retain their TypeScript spelling.
- TypeScript fields can surface as CLR properties or fields according to their emitted shape;
  inspect the produced assembly rather than depending on private `$`-prefixed implementation
  types.

Common boundary types are `number` → `double`, `string` → `string`, `boolean` → `bool`, `bigint` →
`BigInteger`, and `Promise<T>` → task-shaped runtime values. Complex JavaScript values may use
emitted runtime types and are better hidden behind a small primitive/string DTO surface.

## Reference the assembly directly

Add the generated DLL to the C# project:

```xml
<ItemGroup>
  <Reference Include="Library">
    <HintPath>..\CompiledTS\Library.dll</HintPath>
    <Private>true</Private>
  </Reference>
</ItemGroup>
```

Then call public classes using normal C# syntax where their signatures are CLS-consumable:

```csharp
double total = Acme.TypeScript.Calculator.add(10.0, 20.0);
Console.WriteLine(total);
```

Direct references give C# compile-time checking and IntelliSense, but expose the exact emitted CLR
surface. Keep the TypeScript-facing library boundary simple and rebuild the C# consumer whenever
that surface changes.

Top-level functions live on an IL type named `$Program`. `$` is valid in IL but cannot be written as
a normal C# identifier, so call those functions through reflection or place the public entry points
on an exported class.

## Load the assembly with reflection

Reflection is useful for plugin-style loading or top-level functions:

```csharp
using System.Reflection;

string path = Path.Combine(AppContext.BaseDirectory, "Library.dll");
Assembly library = Assembly.LoadFrom(path);

Type calculator = library.GetType("Acme.TypeScript.Calculator", throwOnError: true)!;
MethodInfo add = calculator.GetMethod(
    "add",
    BindingFlags.Public | BindingFlags.Static)!;

double total = (double)add.Invoke(null, [10.0, 20.0])!;

Type program = library.GetType("$Program", throwOnError: true)!;
MethodInfo format = program.GetMethod(
    "formatMessage",
    BindingFlags.Public | BindingFlags.Static)!;

string text = (string)format.Invoke(null, ["INFO", "Loaded from C#"])!;
```

Use `double` arguments for TypeScript `number`; passing an `int` through reflection does not perform
the same conversions as a TypeScript call. Cache `Type`, `MethodInfo`, and accessor objects on hot
paths.

## Deployment checklist

1. Copy the compiled TypeScript assembly into the consumer output.
2. Copy its external managed dependency closure, if one was emitted.
3. Copy `SharpTS.dll` only when it was produced beside the assembly or the compile log named a soft
   runtime requirement.
4. Keep the PDB and TypeScript source available when source debugging is required.
5. Run the C# consumer in the same target-framework family used to build the SharpTS output.
6. Test public calls with the exact reflected CLR parameter types, especially `double` for numbers.

An assembly-load error is usually a missing hard dependency; inspect `FileNotFoundException.FileName`
and the consumer's dependency graph. Do not assume every failure means `SharpTS.dll` is missing.

## API design guidance

- Prefer public TypeScript classes with primitive/string parameters over exposing `$Program`.
- Use `@Namespace` to avoid collisions in a larger .NET solution.
- Wrap dynamic JavaScript object graphs behind typed methods or serialize DTOs at the boundary.
- Use reflection for discovery and direct references for stable, tightly coupled consumers.
- Treat generated `$`-prefixed runtime types as implementation details.
- Exercise the compiled assembly from C# in integration tests; interpreter tests do not validate
  the emitted CLR surface.

The complete runnable solution is in [`Examples/Interop`](../Examples/Interop/README.md). To call
.NET libraries in the opposite direction—from TypeScript—use
[.NET types from TypeScript](dotnet-types.md).
