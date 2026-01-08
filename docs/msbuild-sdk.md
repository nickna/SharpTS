# MSBuild SDK Guide

SharpTS provides an MSBuild SDK that integrates TypeScript-to-.NET compilation directly into your build process. Instead of running `sharpts --compile` manually, the SDK compiles your TypeScript automatically when you run `dotnet build`.

## Quick Start

Create a project file:

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

Build and run:

```bash
dotnet build
dotnet bin/Debug/net10.0/MyProject.dll
```

---

## Installation

### NuGet Package Reference

The SDK is distributed as a NuGet package. Reference it in your project file:

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
```

### Version Pinning with global.json

Pin the SDK version across your solution:

```json
{
  "msbuild-sdks": {
    "SharpTS.Sdk": "1.0.0"
  }
}
```

Then use the SDK without a version number:

```xml
<Project Sdk="SharpTS.Sdk">
```

---

## Project File Configuration

### Minimal Configuration

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

### Full Configuration

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>

    <!-- Required: Entry point TypeScript file -->
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>

    <!-- Output configuration -->
    <SharpTSOutputPath>$(OutputPath)</SharpTSOutputPath>
    <SharpTSOutputFileName>$(AssemblyName).dll</SharpTSOutputFileName>

    <!-- Compiler options -->
    <SharpTSPreserveConstEnums>false</SharpTSPreserveConstEnums>
    <SharpTSExperimentalDecorators>false</SharpTSExperimentalDecorators>
    <SharpTSDecorators>false</SharpTSDecorators>
    <SharpTSEmitDecoratorMetadata>false</SharpTSEmitDecoratorMetadata>
    <SharpTSVerifyIL>false</SharpTSVerifyIL>
    <SharpTSUseReferenceAssemblies>false</SharpTSUseReferenceAssemblies>

    <!-- tsconfig.json path (auto-detected by default) -->
    <SharpTSTsConfigPath>$(MSBuildProjectDirectory)\tsconfig.json</SharpTSTsConfigPath>
  </PropertyGroup>
</Project>
```

---

## MSBuild Properties Reference

### Required Properties

| Property | Description |
|----------|-------------|
| `SharpTSEntryPoint` | Path to the entry point TypeScript file. Can be absolute or relative to the project directory. If not specified, the SDK attempts to read from `tsconfig.json`'s `files` array. |

### Output Properties

| Property | Default | Description |
|----------|---------|-------------|
| `SharpTSOutputPath` | `$(OutputPath)` | Directory where the compiled DLL is written |
| `SharpTSOutputFileName` | `$(AssemblyName).dll` | Name of the output assembly |

### Compiler Options

| Property | Default | CLI Equivalent | Description |
|----------|---------|----------------|-------------|
| `SharpTSPreserveConstEnums` | `false` | `--preserveConstEnums` | Keep const enum declarations in output |
| `SharpTSExperimentalDecorators` | `false` | `--experimentalDecorators` | Enable legacy (Stage 2) decorators |
| `SharpTSDecorators` | `false` | `--decorators` | Enable TC39 Stage 3 decorators |
| `SharpTSEmitDecoratorMetadata` | `false` | `--emitDecoratorMetadata` | Emit design-time type metadata |
| `SharpTSVerifyIL` | `false` | `--verify` | Verify generated IL after compilation |
| `SharpTSUseReferenceAssemblies` | `false` | `--ref-asm` | Emit reference-assembly-compatible output |

### Configuration Properties

| Property | Default | Description |
|----------|---------|-------------|
| `SharpTSTsConfigPath` | `$(MSBuildProjectDirectory)\tsconfig.json` | Path to tsconfig.json for reading compiler options |

---

## tsconfig.json Integration

The SDK automatically reads `tsconfig.json` if present in the project directory. This provides IDE compatibility and allows sharing configuration between SharpTS and standard TypeScript tooling.

### Supported Settings

| tsconfig.json Path | Maps To | Notes |
|--------------------|---------|-------|
| `compilerOptions.preserveConstEnums` | `SharpTSPreserveConstEnums` | |
| `compilerOptions.experimentalDecorators` | `SharpTSExperimentalDecorators` | |
| `compilerOptions.decorators` | `SharpTSDecorators` | TC39 Stage 3 |
| `compilerOptions.emitDecoratorMetadata` | `SharpTSEmitDecoratorMetadata` | |
| `files[0]` | `SharpTSEntryPoint` | First file used as entry point |

### Priority Order

MSBuild properties take precedence over tsconfig.json values:

1. **Explicit MSBuild property** (highest priority)
2. **tsconfig.json value**
3. **Default value** (lowest priority)

This allows you to use tsconfig.json for IDE compatibility while overriding specific settings in MSBuild.

### Example tsconfig.json

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "strict": true,
    "preserveConstEnums": true,
    "experimentalDecorators": true,
    "emitDecoratorMetadata": true
  },
  "files": ["src/main.ts"]
}
```

With this tsconfig.json, you can simplify your project file:

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Entry point and options read from tsconfig.json -->
  </PropertyGroup>
</Project>
```

---

## Build Targets

The SDK defines these MSBuild targets:

| Target | Description |
|--------|-------------|
| `SharpTSCompile` | Main compilation target. Runs before `CoreCompile`. |
| `SharpTSClean` | Removes compiled output. Runs before `Clean`. |
| `_SharpTSReadTsConfig` | Reads tsconfig.json settings. |
| `_SharpTSValidateInputs` | Validates entry point exists. |

### Extending Build Behavior

You can hook into the build process:

```xml
<Target Name="BeforeSharpTSCompile" BeforeTargets="SharpTSCompile">
  <Message Importance="high" Text="About to compile TypeScript..." />
</Target>

<Target Name="AfterSharpTSCompile" AfterTargets="SharpTSCompile">
  <Message Importance="high" Text="TypeScript compilation complete!" />
</Target>
```

---

## Project Structures

### Single-File Project

```
MyProject/
├── MyProject.csproj
└── src/
    └── main.ts
```

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

### Multi-Module Project

The SDK uses SharpTS's ModuleResolver to automatically discover and compile imported modules:

```
MyProject/
├── MyProject.csproj
├── tsconfig.json
└── src/
    ├── main.ts          # Entry point
    ├── utils/
    │   └── helpers.ts   # Imported by main.ts
    └── models/
        └── person.ts    # Imported by main.ts
```

```typescript
// src/main.ts
import { formatName } from './utils/helpers';
import { Person } from './models/person';

const p = new Person("Alice", 30);
console.log(formatName(p.name));
```

All imported modules are compiled into a single DLL automatically.

### With tsconfig.json

```
MyProject/
├── MyProject.csproj
├── tsconfig.json
└── src/
    └── main.ts
```

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Entry point read from tsconfig.json -->
  </PropertyGroup>
</Project>
```

---

## Build Commands

Standard MSBuild commands work transparently:

```bash
# Build in Debug mode
dotnet build

# Build in Release mode
dotnet build -c Release

# Clean output
dotnet clean

# Rebuild (clean + build)
dotnet build --no-incremental

# Publish
dotnet publish -c Release
```

### Verbose Output

For debugging build issues:

```bash
dotnet build -v detailed
```

---

## Error Handling

### Error Format

The SDK outputs errors in MSBuild-compatible format for IDE integration:

```
src/main.ts(15,10): error SHARPTS001: Type 'string' is not assignable to type 'number'
```

### Error Codes

| Code | Category | Description |
|------|----------|-------------|
| SHARPTS000 | General | Unclassified error |
| SHARPTS001 | Type Error | Type mismatch, invalid assignment |
| SHARPTS002 | Parse Error | Syntax error, unexpected token |
| SHARPTS003 | Module Error | Import not found, circular dependency |
| SHARPTS004 | Compile Error | IL emission failure |
| SHARPTS005 | Config Error | Invalid configuration |

### Common Errors

**"SharpTSEntryPoint must be specified"**
- Set `<SharpTSEntryPoint>` in your project file, or
- Add a `files` array to your tsconfig.json

**"Entry point file 'X' does not exist"**
- Check the path is correct relative to the project directory
- Ensure the file exists

**"SharpTS compilation failed with exit code 1"**
- Check the build output for specific error messages
- Use `dotnet build -v detailed` for more information

---

## CI/CD Integration

### GitHub Actions

```yaml
name: Build

on: [push, pull_request]

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '10.0.x'

      - name: Build
        run: dotnet build -c Release

      - name: Test
        run: dotnet test -c Release
```

### Azure DevOps

```yaml
trigger:
  - main

pool:
  vmImage: 'ubuntu-latest'

steps:
  - task: UseDotNet@2
    inputs:
      version: '10.0.x'

  - task: DotNetCoreCLI@2
    inputs:
      command: 'build'
      arguments: '-c Release'

  - task: DotNetCoreCLI@2
    inputs:
      command: 'test'
      arguments: '-c Release'
```

---

## Migration from Manual Targets

If you're using manual pre-build targets, migrate to the SDK:

### Before (Manual Target)

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <Target Name="CompileTypeScript" BeforeTargets="Build">
    <Exec Command="sharpts --compile src/main.ts -o $(OutputPath)app.dll" />
  </Target>
</Project>
```

### After (SDK)

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

### Benefits of Migration

- Automatic tsconfig.json integration
- Proper Clean target support
- MSBuild-compatible error output
- Simplified project file
- Version management via global.json

---

## Troubleshooting

### SDK Not Found

**Error:** `The SDK 'SharpTS.Sdk/1.0.0' could not be resolved`

- Ensure the NuGet package is available (nuget.org or private feed)
- Check your NuGet.config includes the correct package source
- Try `dotnet restore` before building

### Build Hangs

- Check for infinite loops in TypeScript code
- Use `--timeout` if available in future versions

### IL Verification Fails

**Error:** IL verification errors with `SharpTSVerifyIL=true`

- This may indicate a compiler bug - please report with source code
- Disable verification as a workaround: `<SharpTSVerifyIL>false</SharpTSVerifyIL>`

### tsconfig.json Not Read

- Ensure the file is valid JSON (comments and trailing commas are supported)
- Check `SharpTSTsConfigPath` points to the correct location
- Use `dotnet build -v detailed` to see what values were read

---

## Examples

### Console Application

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

```typescript
// src/main.ts
console.log("Hello from SharpTS!");

function fibonacci(n: number): number {
    if (n <= 1) return n;
    return fibonacci(n - 1) + fibonacci(n - 2);
}

console.log("Fibonacci(10) =", fibonacci(10));
```

### Library with Decorators

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/index.ts</SharpTSEntryPoint>
    <SharpTSDecorators>true</SharpTSDecorators>
    <SharpTSEmitDecoratorMetadata>true</SharpTSEmitDecoratorMetadata>
    <SharpTSUseReferenceAssemblies>true</SharpTSUseReferenceAssemblies>
  </PropertyGroup>
</Project>
```

### With Custom Namespace

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/library.ts</SharpTSEntryPoint>
    <SharpTSDecorators>true</SharpTSDecorators>
  </PropertyGroup>
</Project>
```

```typescript
// src/library.ts
@Namespace("MyCompany.Libraries")
class Calculator {
    static add(a: number, b: number): number {
        return a + b;
    }
}
```

The `Calculator` class will be emitted in the `MyCompany.Libraries` namespace.

---

## See Also

- [Execution Modes](execution-modes.md) - Interpreted vs compiled mode
- [.NET Integration](dotnet-integration.md) - Consuming compiled TypeScript from C#
- [Code Samples](code-samples.md) - TypeScript to C# mappings
