# SharpTS.Sdk

MSBuild SDK for compiling TypeScript directly to .NET assemblies using SharpTS.

## Quick Start

Create a new project file:

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

Build your project:

```bash
dotnet build
```

## Features

- Compiles TypeScript directly to .NET IL (no JavaScript intermediate)
- Automatic import discovery via ModuleResolver
- tsconfig.json integration
- Standard MSBuild commands (`dotnet build`, `dotnet clean`)
- MSBuild-compatible error output for IDE integration

## MSBuild Properties

| Property | Description | Default |
|----------|-------------|---------|
| `SharpTSEntryPoint` | Entry point TypeScript file (required) | _(none)_ |
| `SharpTSOutputPath` | Output directory for compiled DLL | `$(OutputPath)` |
| `SharpTSOutputFileName` | Output filename | `$(AssemblyName).dll` |
| `SharpTSTsConfigPath` | Path to tsconfig.json | `$(MSBuildProjectDirectory)\tsconfig.json` |
| `SharpTSPreserveConstEnums` | Preserve const enum declarations | `false` |
| `SharpTSExperimentalDecorators` | Enable legacy (Stage 2) decorators | `false` |
| `SharpTSDecorators` | Enable TC39 Stage 3 decorators | `false` |
| `SharpTSEmitDecoratorMetadata` | Emit decorator metadata | `false` |
| `SharpTSVerifyIL` | Verify generated IL | `false` |
| `SharpTSUseReferenceAssemblies` | Use reference assembly mode | `false` |

## tsconfig.json Integration

The SDK automatically reads `tsconfig.json` if present. Supported options:

| tsconfig.json | Maps to |
|---------------|---------|
| `compilerOptions.preserveConstEnums` | `SharpTSPreserveConstEnums` |
| `compilerOptions.experimentalDecorators` | `SharpTSExperimentalDecorators` |
| `compilerOptions.decorators` | `SharpTSDecorators` |
| `compilerOptions.emitDecoratorMetadata` | `SharpTSEmitDecoratorMetadata` |
| `files[0]` | `SharpTSEntryPoint` (if not set) |

MSBuild properties take precedence over tsconfig.json values.

## Example Projects

### Minimal Project

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

### With Decorators

```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/app.ts</SharpTSEntryPoint>
    <SharpTSDecorators>true</SharpTSDecorators>
    <SharpTSEmitDecoratorMetadata>true</SharpTSEmitDecoratorMetadata>
  </PropertyGroup>
</Project>
```

### With tsconfig.json

Project structure:
```
myproject/
├── myproject.csproj
├── tsconfig.json
└── src/
    └── main.ts
```

`myproject.csproj`:
```xml
<Project Sdk="SharpTS.Sdk/1.0.0">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- Entry point read from tsconfig.json files array -->
  </PropertyGroup>
</Project>
```

`tsconfig.json`:
```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "ESNext",
    "strict": true,
    "preserveConstEnums": true
  },
  "files": ["src/main.ts"]
}
```

## Version Specification

You can pin the SDK version in `global.json`:

```json
{
  "msbuild-sdks": {
    "SharpTS.Sdk": "1.0.0"
  }
}
```

## Build Commands

```bash
# Build the project
dotnet build

# Clean output files
dotnet clean

# Publish
dotnet publish

# Build in Release mode
dotnet build -c Release
```

## Error Codes

| Code | Category | Description |
|------|----------|-------------|
| SHARPTS000 | General | Unclassified error |
| SHARPTS001 | Type Error | Type mismatch, invalid assignment |
| SHARPTS002 | Parse Error | Syntax error, unexpected token |
| SHARPTS003 | Module Error | Import not found, circular dependency |
| SHARPTS004 | Compile Error | IL emission failure |
| SHARPTS005 | Config Error | Invalid configuration |

## System Requirements

- **.NET 10 SDK or later** (required for building projects)
- **Visual Studio 2026 18.0+** (recommended for IDE support)
- **MSBuild 18.0+** (included with .NET 10 SDK)

SharpTS.Sdk targets .NET 10 directly for optimal performance and modern features.

## License

MIT License - see the main SharpTS repository for details.
