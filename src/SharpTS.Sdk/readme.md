# SharpTS.Sdk

`SharpTS.Sdk` compiles a TypeScript entry module directly to a .NET assembly during normal MSBuild
build, clean, and publish operations. It composes with `Microsoft.NET.Sdk`, bundles the matching
SharpTS compiler, and does not require a global `sharpts` tool.

Use versionless SDK syntax with a centrally selected package version:

```xml
<Project Sdk="SharpTS.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>src/main.ts</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

```json
{
  "msbuild-sdks": {
    "SharpTS.Sdk": "<version>"
  }
}
```

Replace `<version>` with the package version selected by your application, then run `dotnet build`.
You may instead use `<Project Sdk="SharpTS.Sdk/<version>">` for a project-local pin.

Key properties are `SharpTSEntryPoint`, `SharpTSTsConfigPath`, `SharpTSOutputPath`,
`SharpTSOutputFileName`, `SharpTSPreserveConstEnums`, `SharpTSExperimentalDecorators`,
`SharpTSDecorators`, `SharpTSEmitDecoratorMetadata`, `SharpTSGenerateDeclarations`,
`SharpTSEmitDeclarationOnly`, `SharpTSDeclarationDir`, `SharpTSVerifyIL`, and
`SharpTSUseReferenceAssemblies`.

The canonical [MSBuild SDK guide](../../docs/msbuild-sdk.md) documents exact defaults, `tsconfig`
mapping and precedence, output behavior, extension points, and troubleshooting.

Requires the .NET 10 SDK or later. SharpTS is licensed under the MIT License.
