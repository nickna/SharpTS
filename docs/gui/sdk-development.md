# SharpTS GUI SDK development workflow

`SharpTS.Gui.Sdk` is the supported application entry point for the Windows desktop preview and the
experimental Apple Silicon candidate. It materializes the matching `@sharpts/gui` package under
`obj`, type-checks and compiles the guest, writes the versioned application manifest, and assembles
the Avalonia host. Applications do not need a separate global SharpTS installation, repository
checkout, C# source file, or AXAML file.

## Choose a project workflow

### SharpTS CLI

The SharpTS CLI creates a TypeScript-only application and drives the SDK through a generated
project:

```powershell
sharpts new avalonia -n CounterApp
cd CounterApp
sharpts app run
sharpts app run --mode compiled
sharpts app build
sharpts app publish --rid win-x64 --self-contained true --single-file true
```

`sharpts.json` records `application.type`, the entry module, and the pinned GUI SDK version. Host
selection uses an explicit `--host avalonia|console`, then the manifest, conservative import
inference, and finally the console default. Mixed GUI and another JSX runtime require an explicit
host.

The CLI writes `.sharpts-gui.generated.csproj`, keeps generated build state under `.sharpts/gui`,
and invokes `SharpTS.Gui.Sdk` for restore, compilation, launcher generation, native asset
selection, and publishing. `--source` selects a local or HTTP(S) SDK feed; `--output`,
`--configuration`, and `--sdk-version` override their corresponding defaults.

`--rid`, `--self-contained`, and `--single-file` are independent options, although single-file
output requires a self-contained deployment. A directory deployment retains interpreted mode. A
self-contained single file contains only the compiled guest.

### .NET SDK template

Install the selected template and create an explicit SDK project. Keep any required NuGet pin in
application configuration rather than copying a package release number from this guide:

```powershell
dotnet new install SharpTS.Gui.Sdk
dotnet new sharpts-gui -n CounterApp
cd CounterApp
dotnet restore
dotnet build
dotnet run -- --mode interpreted
dotnet run -- --mode compiled
```

Both workflows produce the same application manifest, host, and guest payload.

## Develop and test

Use interpreted watch mode for development remounts:

```powershell
dotnet run -- --mode interpreted --watch
```

A changed module graph is validated before the existing UI is removed. Valid changes start a new
runtime and intentionally reset component state, hooks, effects, subscriptions, refs, and timers.
Invalid changes leave the last good UI mounted. Compiled and embedded single-file applications do
not support watch mode.

The template includes `headless.tests.tsx`. Run it against either execution mode:

```powershell
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode interpreted --headless
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode compiled --headless
```

See [GUI testing and developer tools](testing-and-devtools.md) for the supported test driver,
structural inspector, and visual snapshots.

`dotnet clean` removes the generated guest, materialized package, manifest, and copied host output.
Incremental compilation tracks project TS/TSX files, the inherited tsconfig, generated GUI package
files, the project file, compiler and bridge assemblies, the descriptor contract, and compilation
properties such as entry point and IL verification.

## Publish

Framework-dependent directory output retains interpreted and compiled modes:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:SharpTSGuiPublishMode=Directory
```

Set `-p:SharpTSGuiIncludeSourcePayload=false` to omit `Guest`, the generated tsconfig, and the
materialized TypeScript package. That directory is compiled-only and defaults to compiled mode.

A RID publish defaults to a self-contained compiled single executable:

```powershell
dotnet publish -c Release -r win-x64
```

For warning-clean compiled-only Native AOT output, use:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

Use `win-arm64` to cross-publish for Windows ARM64. The experimental Apple Silicon candidate uses
`osx-arm64`; see [macOS GUI distribution](macos-distribution.md). Cross-publishing does not replace
the native certification requirements in the [platform status](README.md#platform-status).

Applications published to a read-only directory do not write beside the executable. Fatal logs
and opt-in traces use the per-user application-data location. Installer identity, signing,
SBOM/provenance, updates, enterprise deployment, and support bundles are documented in
[Windows GUI distribution](windows-distribution.md). Performance and artifact limits are in
[GUI performance and retention](performance.md).

## Configuration and assets

- `SharpTSEntryPoint` selects the root TS/TSX module.
- `SharpTSTsConfigPath` selects a consumer config inherited by the generated overlay.
- `SharpTSVerifyIL=true` verifies the compiled hosted guest.
- `SharpTSGuiIncludeSourcePayload=false` creates compiled-only directory output.
- `SharpTSGuiPublishMode=Directory` selects framework-dependent directory output for a RID publish.
- Files under `Assets` are embedded under stable `asset:///relative/path` names.
- `SharpTSGuiRemoteAsset` adds a build-time HTTP(S) asset with a required logical name and SHA-256
  pin.

## Native interop and custom controls

Raw Avalonia interop is an internal host capability, not a stable GUI API 1 application surface.
Application code must not retain or mutate `AvaloniaObject` or `Control` instances directly.
Native creation, setters, child collections, event attachment, refs, dialogs, clipboard work, and
recovery run on the Avalonia dispatcher. Off-thread notifications enter through the hosted
dispatcher; synchronous return-valued off-thread callbacks are rejected.

The generated built-in descriptor contract uses reviewed named adapters without runtime
reflection. There is no public third-party descriptor-registration or custom-control loading API.
The descriptor manifest and its C#/TypeScript adapters are maintainer-only implementation details,
have no compatibility promise, and are not supported application extension points. Internal
changes must preserve dispatcher ownership, rollback, cleanup, trimming, and schema-hash rules.

See the [TSX API reference](tsx-api.md) for application behavior and current control limitations,
and the [compatibility and support policy](support-policy.md) for versioning rules.
