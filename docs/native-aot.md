# Native AOT

SharpTS publishes two command-line distributions and also supports application-specific native
hosts. Choose the managed distribution unless startup, single-file deployment, or a closed native
type universe is more important than open-ended reflection and tooling.

## Choose a distribution

| Distribution | Use it when | Important boundaries |
| --- | --- | --- |
| Managed self-contained | You need the full CLI and arbitrary managed interop without installing .NET. | Supports external DLL/NuGet references, `dotnet:` and `@DotNetType`, `--verify`, `--gen-decl`, and compiled `child_process.fork`. |
| Native AOT | You want a native executable with fast startup and no extracted managed runtime. | Uses a closed, generated interop catalog. Verification, declaration discovery, open-world reflection, and compiled `child_process.fork` require a managed build. |
| Custom `SharpTS.Hosting` executable | A native application must expose a known set of application or third-party .NET types. | The allowed closed types and their assemblies are declared at build time. |

Release archives are named `sharpts-<version>-<rid>` for managed self-contained builds and
`sharpts-native-<version>-<rid>` for Native AOT builds. Built-in `--target exe` output is supported
on Windows and Linux; DLL output remains portable to compatible .NET hosts.

## Native command-line usage

The native executable accepts the normal interpreter, project, and compiler commands where they
do not require managed-only capabilities:

```bash
sharpts-native app.ts
sharpts-native --compile app.ts -o app.dll
sharpts-native -p ./tsconfig.json
```

The official native build includes a curated BCL interop catalog for strings, numeric conversion
and math, dates and times, GUIDs, environment and console APIs, tasks, selected enums, and selected
closed collection shapes. A `dotnet:` import outside that catalog fails with a named diagnostic;
the runtime does not attempt unrestricted reflection.

## Build a custom native host

Reference `SharpTS.Hosting`, then declare each allowed type. Add `Assembly` for types from an
application library so the managed DLL and its non-framework copy-local dependency closure can be
embedded for compiled guest output:

```xml
<ItemGroup>
  <PackageReference Include="SharpTS.Hosting" Version="<version>" />
  <ProjectReference Include="MyCompany.Library.csproj" />
  <SharpTSNativeInteropType Include="MyCompany.Widget"
                            Assembly="MyCompany.Library"
                            Alias="Widget" />
</ItemGroup>
```

Start the CLI with the generated catalog:

```csharp
return SharpTSCli.Run(
    args,
    SharpTS.Generated.GeneratedNativeDotNetCatalog.Instance);
```

The generated catalog roots constructors, methods, properties, fields, events, and declared
closed generics. See [`samples/NativeInteropHost`](../samples/NativeInteropHost/README.md) and
the [`SharpTS.Hosting` package guide](../src/SharpTS.Hosting/README.md) for a complete project.

## Operational guidance

- Test the exact runtime identifier on native hardware before publishing it. Cross-publishing
  proves artifact construction, not runtime compatibility.
- Treat the interop catalog as an allow-list. Add only the types and closed generic shapes the
  application needs.
- Keep external managed dependencies available to the host so the compiler can embed their
  copy-local closure when guest code references them.
- Use the managed distribution for `--gen-decl` and `--verify`; apply those checks before the
  native release build.
- A Native AOT compiler host rejects emitted features that require the managed SharpTS runtime
  instead of silently producing output that cannot run.

Native AOT constrains host implementation mechanisms, not TypeScript language semantics. The
interpreter/compiled behavior contract and documented deviations remain those in
[Execution modes](execution-modes.md) and [STATUS.md](../STATUS.md).
