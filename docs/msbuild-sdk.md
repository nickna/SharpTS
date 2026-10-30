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

This table is derived from `src/SharpTS.Sdk/Sdk/Sdk.props` and `Sdk.targets`. It lists the supported
consumer properties; underscore-prefixed values and tool/task paths are implementation details.

| Property | Default | Effect |
| --- | --- | --- |
| `SharpTSEntryPoint` | Empty; then `tsconfig` `files[0]` | Runtime entry TypeScript/TSX file. Required after configuration is read. |
| `SharpTSTarget` | `dll`; `native-aot` when `PublishAot=true` | Select direct managed IL output or the experimental native guest SDK publish graph. |
| `SharpTSTsConfigPath` | `$(MSBuildProjectDirectory)\tsconfig.json` | Configuration passed to the compiler when the file exists. A missing path causes the SDK to pass `--no-tsconfig`. |
| `SharpTSOutputPath` | `$(OutputPath)` | Managed compiler output directory. Native output uses standard `OutputPath` and `PublishDir`. |
| `SharpTSOutputFileName` | `$(AssemblyName).dll` | Managed output assembly name. Native wrapper/executable names use standard `AssemblyName`. |
| `SharpTSPreserveConstEnums` | `false` | Pass `--preserveConstEnums` when true. |
| `SharpTSExperimentalDecorators` | `false` | Pass `--experimentalDecorators` for legacy decorators when true. |
| `SharpTSDecorators` | `false` | Pass `--decorators` to select TC39 Stage 3 decorators when true. |
| `SharpTSEmitDecoratorMetadata` | `false` | Pass `--emitDecoratorMetadata` when true. |
| `SharpTSGenerateDeclarations` | `false` | Pass `--declaration` when true. |
| `SharpTSEmitDeclarationOnly` | `false` | Pass `--emitDeclarationOnly`; also forces declaration generation. |
| `SharpTSDeclarationDir` | Empty | Pass `--declarationDir` when nonempty. Otherwise compiler `rootDir`/`outDir` rules apply. |
| `SharpTSVerifyIL` | `false` | Pass `--verify` when true. |
| `SharpTSUseReferenceAssemblies` | `false` | Pass `--ref-asm` for C#-reference-compatible output when true. |
| `SharpTSGcProfile` | `workstation` | Deployment GC policy: `workstation`, `adaptive`, or `throughput`. Propagated to build, publish, package, and bundle runtime configuration. |

Choose decorator behavior with `SharpTSDecorators`, `SharpTSExperimentalDecorators`, and the
corresponding `tsconfig` settings.

The SDK also defines `UsingSharpTSSdk=true` as an identification marker and resolves
`SharpTSToolPath`, `SharpTSCompilerExe`, and `SharpTSTasksAssembly` from its own package. Consumers
should not pin or override those internal locations.

### NativeAOT guest publish

The experimental native target publishes the generated TypeScript program itself through the
standard .NET NativeAOT SDK pipeline. The SDK package and bundled compiler remain managed and
RID-neutral. Set the native target and keep `PublishAot` enabled in the consumer project:

```xml
<Project Sdk="SharpTS.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <AssemblyName>MyProgram</AssemblyName>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
    <SharpTSTarget>native-aot</SharpTSTarget>
    <PublishAot>true</PublishAot>
  </PropertyGroup>
</Project>
```

```bash
dotnet publish -c Release -r win-x64
```

Supply an explicit matching host RID: `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`,
`osx-x64`, or `osx-arm64`. Install the .NET 10 SDK and the target platform's NativeAOT compiler/linker
prerequisites. Native compilation happens during `publish`; `build` produces the managed wrapper
and generated guest assembly. Without native settings, the ordinary direct managed build and
publish behavior is unchanged. `SharpTSTarget=native-aot` enables `PublishAot` when it is unset;
`PublishAot=true` selects the native target when `SharpTSTarget` is unset.

Before reference resolution, the SDK exports a real inspectable native project under
`$(IntermediateOutputPath)/sharpts-native/<attempt>/`. Roslyn compiles its actual `Program.cs`
wrapper and references `guest/Guest.dll`; the base SDK then owns the single NativeAOT publish.
Each invocation uses a fresh attempt directory. Prior exports are retained for diagnosis and
inspection, including after `dotnet clean`; no existing export directory is emptied. Standard
clean targets remove the consumer's managed build products and copied dependencies.

Use `AssemblyName` for the wrapper/native executable name, `OutputPath` for build products, and
`PublishDir` or `dotnet publish -o <directory>` for native publication. The default native publish
directory is the base SDK's configuration/TFM/RID publish directory. `SharpTSOutputPath` and
`SharpTSOutputFileName` remain managed-only settings. `AssemblyName=Guest` is reserved because it
would shadow the generated guest assembly. Names matching a resolved framework reference, such as
`System.Console`, are also refused with `SHARPTSSDK001` before wrapper compilation and native
linking. These identity checks are case-insensitive.

The native profile requires workstation GC and reference-assembly emission. Explicit `Reference`
items with executable net10.0 AnyCPU DLL `HintPath` values can supply a complete managed dependency
graph. Every non-framework assembly dependency must be supplied explicitly with its exact name,
version, culture and public-key token. The compiler snapshots those bytes, exports them under
`references/`, and substitutes the captured DLLs into the SDK reference graph. Before wrapper
compilation, the SDK checks every selected implementation and framework reference against the
compiler's captured byte hashes. Original inputs can change after capture without changing an attempt.

`ProjectReference`, `PackageReference`, missing or name-only references, reference-only assemblies,
architecture-specific DLLs and unproved native/resource/module assets fail with `SHARPTS402`.
The profile also refuses duplicate simple names and names that shadow Guest, the wrapper,
framework or SharpTS assemblies. Framework identities use the compiler's reference-assembly provider. Runtime-dependent source features fail with their native
compatibility diagnostics before linking; see [the native guest contract](native-guest-projects.md).
`emitDeclarationOnly`, including inherited `tsconfig` settings, cannot produce a native program.

Trim/AOT diagnostics stay visible and fatal through `IlcTreatWarningsAsErrors=true` and
`ILLinkTreatWarningsAsErrors=true`; the SDK adds no warning suppressions or broad assembly roots.
`DebugSymbols`/`DebugType` control guest portable PDB generation and the base SDK's wrapper/native
symbol behavior. With symbols enabled, native PDBs, `.dbg`, or `.dSYM` artifacts follow platform
publish conventions; the guest portable PDB is retained for inspection. The separate
[Windows x64 Debug native debugger acceptance](debugging-typescript.md#nativeaot-guest-debugging-windows-x64)
verifies TypeScript breakpoints, caller frames and source-line Step Over through `cppvsdbg`.
It uses inspect export plus Debug SDK publication; symbol retention alone does not certify
other debugger/platform/optimization combinations or managed hoisted-local projection.

### GC deployment policy

Use `SharpTSGcProfile=adaptive` for a measured, sustained allocation-heavy service:

```xml
<PropertyGroup>
  <SharpTSGcProfile>adaptive</SharpTSGcProfile>
</PropertyGroup>
```

The default `workstation` profile is intentionally conservative for interactive and unknown
workloads. `adaptive` enables concurrent server GC with DATAS. `throughput` disables DATAS and is
an expert opt-in because fixed server heaps can reserve substantially more memory. Profile names
are validated during the build; an invalid value does not fall back silently. See the
[cross-platform decision record](../benchmarks/gc-profiles/decision.md) for timing, startup, and
peak-working-set measurements.

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
    <SharpTSGcProfile>adaptive</SharpTSGcProfile>
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
[Examples cookbook](../samples/README.md).
