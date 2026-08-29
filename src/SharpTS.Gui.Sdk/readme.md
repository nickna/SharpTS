# SharpTS.Gui.Sdk

`SharpTS.Gui.Sdk` builds retained Avalonia desktop applications whose application code, components,
and UI are written in TypeScript/TSX. The package contains the matching compiler, host, generated
GUI declarations, MSBuild tasks, native assets, launcher, and templates.

Windows x64 and ARM64 are the supported targets. Apple Silicon macOS is an experimental
candidate that requires native execution, signing, and notarization evidence before it is a support
claim. macOS Intel is not supported.

## Create an application

With the SharpTS tool installed:

```powershell
sharpts new desktop -n CounterApp
cd CounterApp
sharpts app run
sharpts app run --mode compiled
```

The generated application contains TypeScript/TSX, assets, `tsconfig.json`, and `sharpts.json`; the
CLI drives an internal SDK project.

For an explicit MSBuild project, install the template without embedding a version in source:

```powershell
dotnet new install SharpTS.Gui.Sdk::<version>
dotnet new sharpts-gui -n CounterApp
cd CounterApp
dotnet run -- --mode interpreted
```

Use a central SDK pin in `global.json`:

```json
{
  "msbuild-sdks": {
    "SharpTS.Gui.Sdk": "<version>"
  }
}
```

```xml
<Project Sdk="SharpTS.Gui.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>main.tsx</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

Replace `<version>` with the package version selected by the application.

## Public application surface

Applications create a session with `createDesktopApplication` and mount windows with
`application.createWindow`. Public component APIs include state and lifecycle hooks, fragments,
error boundaries, resources/styles, assets, dialogs, clipboard/display services, typed item
templates, drawing, retained content controls, reactive window metrics for adaptive DIP-based
layouts, and the generated built-in control catalog.

The drawing surface supports retained geometry, bounded text, images, opacity, and isolated
erasing. Typed asynchronous services render a drawing document to a file or portable PNG source,
sample composited pixels, perform contiguous flood fills, and apply the supported blur and color
effect pipeline through the same validated renderer.

The `@sharpts/gui/testing` subpath provides a window-scoped Headless interaction driver, including
deterministic logical window resizing for responsive-layout tests.
`@sharpts/gui/devtools` provides read-only tree inspection and deterministic Headless PNG snapshots.
Repository fault injection, scheduler manipulation, trace staging, renderer identity, and
subscription counters are not application APIs.

Applications interact with native controls through generated props, events, refs, and services.
There is no supported public third-party custom-control provider, descriptor-registration, raw
Avalonia object, or dynamic control-loading API. Internal provider seams are private implementation
details with no compatibility promise and must not be packaged as application extensions.

## Develop and publish

Interpreted watch mode validates a changed graph before replacing the mounted application:

```powershell
dotnet run -- --mode interpreted --watch
```

A valid reload intentionally creates new runtime/component state. Compiled and embedded
single-file applications do not support watch mode.

Framework-dependent directory output can retain interpreted and compiled guests:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:SharpTSGuiPublishMode=Directory
```

A RID publish defaults to compiled self-contained single-file output. Native AOT compiled-only
Windows x64 output uses:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

Use `win-arm64` for Windows ARM64 and `osx-arm64` only for the experimental Apple Silicon candidate.
Cross-publishing is not native certification.

Key build properties include `SharpTSEntryPoint`, `SharpTSTsConfigPath`, `SharpTSVerifyIL`,
`SharpTSGcProfile` (`workstation`, `adaptive`, or `throughput`), `SharpTSGuiPublishMode`, and
`SharpTSGuiIncludeSourcePayload`. Files under `Assets` are embedded at
stable `asset:///` paths; remote build-time assets require a logical name and integrity digest.

The repository [GUI documentation](https://github.com/nickna/SharpTS/blob/main/docs/gui/README.md)
covers the TSX API, testing, platform status, performance, distribution, and compatibility policy.
[`samples/Calculator`](https://github.com/nickna/SharpTS/tree/main/samples/Calculator) is a complete
application. SDK maintainers can also inspect the [template-specific runbook](Templates/sharpts-gui/README.md).
