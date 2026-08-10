# MSBuild SDK guide

`SharpTS.Sdk` is the canonical MSBuild integration for compiling a TypeScript entry module directly
to a .NET assembly. It composes with `Microsoft.NET.Sdk`, runs the compiler bundled in the selected
package, participates in normal build/clean/publish targets, and does not require a global SharpTS
tool.

## Select and pin the SDK

MSBuild SDK packages must resolve to a NuGet version. Either put a placeholder pin in the project:

```xml
<Project Sdk="SharpTS.Sdk/<version>">
```

or keep project files versionless and centralize the pin in `global.json`:

```xml
<Project Sdk="SharpTS.Sdk">
```

```json
{
  "msbuild-sdks": {
    "SharpTS.Sdk": "<version>"
  }
}
```

Replace `<version>` with a published package version selected by your application. The repository
documentation deliberately does not couple releases to one copyable package number.

## Minimal project

```xml
<Project Sdk="SharpTS.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

```bash
dotnet build
dotnet run
dotnet publish
dotnet clean
```

`SharpTSEntryPoint` can instead come from the first `files` entry in `tsconfig.json`. The SDK emits
`$(AssemblyName).dll` and its runtimeconfig under the normal configuration output directory.

## Public properties

This table is derived from `SharpTS.Sdk/Sdk/Sdk.props` and `Sdk.targets`. It lists the supported
consumer properties; underscore-prefixed values and tool/task paths are implementation details.

| Property | Default | Effect |
| --- | --- | --- |
| `SharpTSEntryPoint` | Empty; then `tsconfig` `files[0]` | Runtime entry TypeScript/TSX file. Required after configuration is read. |
| `SharpTSTsConfigPath` | `$(MSBuildProjectDirectory)\tsconfig.json` | Configuration passed to the compiler when the file exists. A missing path causes the SDK to pass `--no-tsconfig`. |
| `SharpTSOutputPath` | `$(OutputPath)` | Directory for compiler output. Evaluated after the base SDK finalizes `OutputPath`. |
| `SharpTSOutputFileName` | `$(AssemblyName).dll` | Output assembly name. The runtimeconfig name is derived from it. |
| `SharpTSPreserveConstEnums` | `false` | Pass `--preserveConstEnums` when true. |
| `SharpTSExperimentalDecorators` | `false` | Pass `--experimentalDecorators` for legacy decorators when true. |
| `SharpTSDecorators` | `false` | Pass `--decorators` to select TC39 Stage 3 decorators when true. |
| `SharpTSEmitDecoratorMetadata` | `false` | Pass `--emitDecoratorMetadata` when true. |
| `SharpTSGenerateDeclarations` | `false` | Pass `--declaration` when true. |
| `SharpTSEmitDeclarationOnly` | `false` | Pass `--emitDeclarationOnly`; also forces declaration generation. |
| `SharpTSDeclarationDir` | Empty | Pass `--declarationDir` when nonempty. Otherwise compiler `rootDir`/`outDir` rules apply. |
| `SharpTSVerifyIL` | `false` | Pass `--verify` when true. |
| `SharpTSUseReferenceAssemblies` | `false` | Pass `--ref-asm` for C#-reference-compatible output when true. |

Choose decorator behavior with `SharpTSDecorators`, `SharpTSExperimentalDecorators`, and the
corresponding `tsconfig` settings.

The SDK also defines `UsingSharpTSSdk=true` as an identification marker and resolves
`SharpTSToolPath`, `SharpTSCompilerExe`, and `SharpTSTasksAssembly` from its own package. Consumers
should not pin or override those internal locations.

## `tsconfig.json` mapping and precedence

Before compilation, the SDK task reads these values:

| `tsconfig.json` value | MSBuild property |
| --- | --- |
| `compilerOptions.preserveConstEnums` | `SharpTSPreserveConstEnums` |
| `compilerOptions.experimentalDecorators` | `SharpTSExperimentalDecorators` |
| `compilerOptions.decorators` | `SharpTSDecorators` |
| `compilerOptions.emitDecoratorMetadata` | `SharpTSEmitDecoratorMetadata` |
| `compilerOptions.declaration` | `SharpTSGenerateDeclarations` |
| `compilerOptions.emitDeclarationOnly` | `SharpTSEmitDeclarationOnly` |
| `compilerOptions.declarationDir` | `SharpTSDeclarationDir` |
| `files[0]` | `SharpTSEntryPoint` |

The actual merge rules are:

1. A nonempty `SharpTSEntryPoint` or `SharpTSDeclarationDir` wins; configuration fills only an
   empty value.
2. For the mapped boolean switches, `true` from either MSBuild or `tsconfig` enables the feature.
   The current targets treat the property default `false` as a fallback, so an explicit MSBuild
   `false` does not override `true` in `tsconfig`.
3. `SharpTSEmitDeclarationOnly=true` also sets `SharpTSGenerateDeclarations=true`.
4. The compiler receives `--project` as well as the explicit resolved switches. Its normal project
   model handles `extends`, strictness, `baseUrl`/`paths`, module resolution, JSX, libraries,
   ambient packages, `rootDir`, and `outDir`.
5. Explicit arguments assembled by the SDK are later than configuration and therefore determine
   the final compiler value for those switches.

If a project needs to force a mapped option off, set it to `false` in the selected `tsconfig` (or
use a separate config via `SharpTSTsConfigPath`) instead of relying on an MSBuild false override.

Example:

```json
{
  "compilerOptions": {
    "strict": true,
    "decorators": true,
    "declaration": true,
    "declarationDir": "types"
  },
  "files": ["src/main.ts"]
}
```

## Full project example

```xml
<Project Sdk="SharpTS.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>Acme.Scripts</AssemblyName>
    <SharpTSEntryPoint>src/library.ts</SharpTSEntryPoint>
    <SharpTSTsConfigPath>config/tsconfig.build.json</SharpTSTsConfigPath>
    <SharpTSDecorators>true</SharpTSDecorators>
    <SharpTSEmitDecoratorMetadata>true</SharpTSEmitDecoratorMetadata>
    <SharpTSGenerateDeclarations>true</SharpTSGenerateDeclarations>
    <SharpTSDeclarationDir>$(MSBuildProjectDirectory)/types</SharpTSDeclarationDir>
    <SharpTSUseReferenceAssemblies>true</SharpTSUseReferenceAssemblies>
    <SharpTSVerifyIL>true</SharpTSVerifyIL>
  </PropertyGroup>
</Project>
```

Standard resolved .NET references from `@(ReferencePath)` are forwarded as repeatable `-r`
arguments, so project/package references can participate in TypeScript-to-.NET interop.

## Build lifecycle

`SharpTSCompile` runs before `CoreCompile` and depends on configuration reading, input validation,
entry-point resolution, and `ResolveAssemblyReferences`. The SDK disables Roslyn build-product
copying, points `IntermediateAssembly` at the SharpTS output, and adds the compiler-generated
runtimeconfig to publish output. `SharpTSClean` removes the assembly and runtimeconfig.

Extend the build with ordinary target ordering instead of replacing `SharpTSCompile`:

```xml
<Target Name="AfterSharpTS" AfterTargets="SharpTSCompile">
  <Message Text="Built $(SharpTSOutputPath)$(SharpTSOutputFileName)" />
</Target>
```

## Troubleshooting

- **SDK cannot be resolved:** ensure the selected `<version>` exists on configured NuGet sources,
  or confirm the versionless SDK has a `global.json` `msbuild-sdks` entry.
- **Entry point is empty:** set `SharpTSEntryPoint` or provide a `tsconfig.json` with a nonempty
  `files` array.
- **Configuration is ignored:** confirm `SharpTSTsConfigPath` points to an existing file; otherwise
  the SDK intentionally compiles with `--no-tsconfig`.
- **An option will not turn off:** mapped booleans are additive; set the value false in the selected
  `tsconfig` as described above.
- **C# cannot reference the result:** enable `SharpTSUseReferenceAssemblies` and keep the public
  TypeScript boundary CLR-consumable; see [.NET integration](dotnet-integration.md).
- **IL verification fails:** treat it as a compiler defect and retain the minimal TypeScript input
  and build log when reporting it.

For CLI-only workflows and project-reference checking, see
[Execution modes](execution-modes.md). Runnable projects live in the
[Examples cookbook](../Examples/README.md).
