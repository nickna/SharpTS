# SharpTS GUI SDK development workflow

SharpTS.Gui.Sdk is the supported entry point for the Windows desktop preview. It materializes the
matching `@sharpts/gui` package under `obj`, type-checks and compiles the guest, writes the versioned
application manifest, and assembles the Avalonia host. A separate global SharpTS installation,
repository checkout, C# source file, and AXAML file are not required.

## Create and develop an application

```powershell
dotnet new install SharpTS.Gui.Sdk::0.2.0-preview.1
dotnet new sharpts-gui -n CounterApp
cd CounterApp
dotnet restore
dotnet build
dotnet run -- --mode interpreted
dotnet run -- --mode compiled
```

The template includes `headless.tests.tsx`. Run it against either execution mode with ordinary
SDK commands:

```powershell
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode interpreted --headless
dotnet run -p:SharpTSEntryPoint=headless.tests.tsx -- --mode compiled --headless
```

`dotnet clean` removes the generated guest, materialized package, manifest, and copied host output.
Incremental compilation tracks all project TS/TSX files, the inherited tsconfig, generated GUI
package files, the project file, compiler/bridge assemblies, descriptor contract, and compilation
properties such as entry point and IL verification.

## Publish

Framework-dependent directory output retains interpreted and compiled modes:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:SharpTSGuiPublishMode=Directory
```

Use `win-arm64` to cross-publish for Windows ARM64. Set
`-p:SharpTSGuiIncludeSourcePayload=false` to omit `Guest`, the generated tsconfig, and the
materialized TypeScript package. That directory is compiled-only and defaults to compiled mode.

The default RID publish is a self-contained compiled single executable:

```powershell
dotnet publish -c Release -r win-x64
```

The compressed SDK package is approximately 26 MB and a minimal x64 framework-dependent directory
is approximately 47 MB before application assets. These are engineering snapshots, not size gates.
Applications published to a read-only directory do not write beside the executable; fatal logs and
opt-in traces use the per-user local application-data directory.

## Configuration and assets

- `SharpTSEntryPoint` selects the root TS/TSX module.
- `SharpTSTsConfigPath` selects a consumer config inherited by the generated overlay.
- `SharpTSVerifyIL=true` verifies the compiled hosted guest.
- `SharpTSGuiIncludeSourcePayload=false` creates compiled-only directory output.
- `SharpTSGuiPublishMode=Directory` selects framework-dependent directory output for a RID publish.
- Files under `Assets` are embedded under stable `asset:///relative/path` names.
- `SharpTSGuiRemoteAsset` supports build-time HTTP(S) assets with a required SHA-256 pin and logical name.

## Raw Avalonia and custom controls

Raw Avalonia interop is an internal host capability, not a stable application API in GUI API 2.
Application code must not retain or mutate `AvaloniaObject`/`Control` instances directly. Native
creation, setters, child collections, event attachment, refs, dialogs, clipboard work, and recovery
all run on the Avalonia dispatcher. Off-thread notifications enter through the hosted dispatcher;
synchronous return-valued off-thread callbacks are rejected.

The generated built-in descriptor contract deliberately uses reviewed named adapters and no runtime
reflection. There is no public third-party descriptor registration or custom-control loading API.
A private fork can add a manifest entry plus reviewed C#/TypeScript adapters, but that surface has
no compatibility promise and must preserve dispatcher ownership, rollback, cleanup, trimming, and
schema-hash rules. Public custom controls require a future versioned provider model.

## Preview limits

The supported RIDs are `win-x64` and `win-arm64`. The current root model allows one `Window`;
macOS, multi-window APIs, public custom controls, theme resources, drawing, data grids/trees, and
Native AOT certification remain outside the preview contract.
