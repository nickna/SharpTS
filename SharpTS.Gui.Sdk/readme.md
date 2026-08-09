# SharpTS.Gui.Sdk

`ErrorBoundary` handles synchronous component/effect errors and native create, setter,
child-collection, event, and ref commit failures when the host successfully restores the
last committed native tree. Rollback failures, event-handler exceptions, and detached
asynchronous failures remain fatal host errors. Calling the fallback's `reset` callback
retries the protected subtree on a later render.

Preview MSBuild SDK for building retained, reactive Windows and macOS desktop applications from
SharpTS TSX and Avalonia. Windows is the supported preview; macOS payloads are experimental until
their native and Apple distribution workflows pass.

With the SharpTS tool installed, a projectless application uses this SDK internally:

```powershell
sharpts new avalonia -n CounterApp
cd CounterApp
sharpts app run
sharpts app publish --rid win-x64 --self-contained true --single-file true
```

The generated application has no user-authored `.csproj`; `sharpts.json` pins this package and
selects the Avalonia host. The explicit MSBuild SDK workflow remains available below.

Install the package's project template and create an application without C# or AXAML:

```powershell
dotnet new install SharpTS.Gui.Sdk::0.2.0-preview.1
dotnet new sharpts-gui -n CounterApp
cd CounterApp
dotnet build
```

```xml
<Project Sdk="SharpTS.Gui.Sdk/0.2.0-preview.1">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <SharpTSEntryPoint>main.tsx</SharpTSEntryPoint>
  </PropertyGroup>
</Project>
```

Build and run the same application through either hosted ABI 1 guest mode:

```powershell
dotnet build
dotnet run -- --mode interpreted
dotnet run -- --mode compiled
```

During development, interpreted mode can watch project TypeScript and TSX files:

```powershell
dotnet run -- --mode interpreted --watch
```

Each valid edit disposes the current roots and runtime, then mounts a fresh application. Component
state, hooks, effects, subscriptions, refs, and timers do not carry across a reload. An invalid
edit is reported without removing the last successfully mounted UI, so a later edit can retry.
Watch mode is unavailable for compiled and embedded single-file applications.

The `@sharpts/gui/devtools` subpath provides `inspectDesktopTree()` for source-aware logical/native
tree inspection and `captureHeadlessSnapshot()` / `assertHeadlessSnapshot()` for PNG visual tests.
Create or intentionally refresh a committed baseline once with
`assertHeadlessSnapshot("Snapshots/main.png", true)`, then use the default `false` update argument
in normal tests. A mismatch writes `Snapshots/main.actual.png` and reports expected/actual SHA-256
values. Snapshot capture is restricted to `--headless` runs and uses the deterministic Skia-backed
Headless renderer.

`getDesktopDisplays()` returns the current Avalonia display topology with primary-display state,
pixel bounds and working areas, orientation, and per-display scaling. Controls retain native
automation names, tab/focus behavior, keyboard routing, and committed IME text behavior; window
`theme` accepts `system`, `light`, or `dark`.

Publish a self-contained compiled application as one distributable Windows executable:

```powershell
dotnet publish -c Release -r win-x64
# or: dotnet publish -c Release -r win-arm64
# experimental candidates: dotnet publish -c Release -r osx-x64
# or: dotnet publish -c Release -r osx-arm64
```

Single-file publish is the default whenever a runtime identifier is supplied. It embeds the
compiled guest and does not support `--mode interpreted`. To retain both guest modes in a
framework-dependent directory instead, publish with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:SharpTSGuiPublishMode=Directory
```

Directory output includes the TypeScript sources and materialized `@sharpts/gui` package so the
same application can start in either mode. For a smaller compiled-only framework-dependent
directory, set `-p:SharpTSGuiIncludeSourcePayload=false`; the generated launcher then defaults to
compiled mode, and interpreted mode is unavailable because no source payload is distributed.

Windows applications use the GUI subsystem; fatal errors are retained below
`%LOCALAPPDATA%\SharpTS.Gui` and an interactive launch with no console displays a native dialog.
macOS candidates retain fatal logs below `~/Library/Logs/SharpTS.Gui`, traces below
`~/Library/Application Support/SharpTS.Gui/Traces`, and use a native alert after logging.

Applications mount a typed element tree with `renderDesktop(<App />)` or create an explicit
multi-window session with `createDesktopApplication`. The latter supports owned/modeless and
owned/modal windows, activation and close handles, `closed` promises, main/last/explicit shutdown
modes, and per-window render-error isolation. Function components can
return elements, primitive text, fragments, arrays, or `null`. The standard state/lifecycle API is
`useState`, `useReducer`, `useEffect`, `useMemo`, `useCallback`, `useRef`, and `useControlRef`;
`createSignal` remains available for external reactive state.

- Layout: `Window`, `StackPanel`, `WrapPanel`, `DockPanel`, `Grid`, `Border`, `ScrollViewer`,
  `ToolBar`, `StatusBar`, `Separator`, and `Fragment`.
- Display and actions: `TextBlock`, `Image`, and `Button`.
- Forms: `TextBox`, `PasswordBox`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `ComboBox`,
  `ListBox`, `NumericUpDown`, `DatePicker`, `TimePicker`, `Slider`, and `ProgressBar`.
- Navigation and commands: `TabControl`, `TabItem`, `Menu`, and `MenuItem`.
- Data and rendering: `ItemsControl`, `VirtualizingList`, `TreeView`, `TreeViewItem`, `Canvas`,
  `RichTextBlock`, and `DrawingCanvas`.

Controls expose typed direct props for layout, styling, accessibility names, focus refs, keyboard
events, values, callbacks, and normalized text/local-file drag-and-drop. Set `allowDrop` and use
`onDragOver` to select `copy`, `move`, `link`, or `none`; `onDrop` receives the accepted payload.
Keys preserve native identity through sibling insertion, removal,
and reordering. Controlled input commits suppress callback echoes while real user changes dispatch
the latest guest callback through one stable native subscription.

The module also provides message, file, folder, and save dialogs; clipboard read/write helpers;
statically owned system-tray icons and menus; launch/file-association arguments; platform and known
folder information; safe external URI/file launching; Explorer reveal; and Windows shell printing.
Tray resources are application-owned and dispose automatically with their desktop application.
Notification delivery remains outside the preview until the installer can provide a stable Windows
application identity.
Explicit desktop applications accept primitive resource dictionaries and native Avalonia styles
with built-in type/class selectors and a trimming-safe setter allow-list. Controls opt into class
selectors with `classes`, and a window can query its effective resources with `findResource`.
Typed `createVirtualList`, `createTree`, and `createVirtualDataGrid` factories provide keyed item
templates and windowed materialization. Rich inline runs, absolute canvas positioning, and retained
line/rectangle/ellipse drawing commands are part of the generated control contract.

Custom-control NuGet packages may participate through the reviewed, static provider contract. A
package contributes a normal managed reference and a `buildTransitive` target containing an item
such as `<SharpTSGuiControlProvider Include="global::Vendor.Widgets.WidgetProvider" />`. The type
implements `IGuiControlProvider` and returns explicit `NodeDescriptor` instances whose kinds use
its lowercase provider namespace (for example, `vendor.widgets.Chart`). The SDK emits direct
constructor calls into the application launcher; it never scans assemblies or uses reflection.
Provider contract version `1` is checked during launcher registration before guest initialization.
The package's TypeScript wrapper declares `ChartProps extends CommonProps` and uses
`defineCustomControl&lt;ChartProps&gt;("vendor.widgets.Chart")`. Custom prop data is serialized as JSON
in `GuiVNode.CustomPropertiesJson`; provider prop types explicitly include `children?: GuiChild`
when their descriptors accept children. Common layout/style props and refs retain normal
renderer behavior. Packages must pin a compatible SharpTS GUI API and own trimming annotations for
their native controls.
Files under a project's `Assets` directory are embedded automatically and referenced as
`asset:///relative/path.png`. Reproducible URL assets may be declared with `SharpTSGuiRemoteAsset`
items that supply `LogicalName` and a required SHA-256 digest.

`SharpTSTsConfigPath` may name an existing consumer tsconfig. The SDK inherits it through a
generated overlay while reserving the JSX runtime and `@sharpts/gui` module mappings. Set
`SharpTSVerifyIL` to `true` to verify the persisted hosted guest during compilation.

Current supported-preview boundaries: Windows `win-x64` and `win-arm64`, one root element per Window,
statically packaged custom controls only, string-backed legacy list/combo items, and no dynamic
descriptor discovery, arbitrary control-template, or full editing DataGrid API. Native resources/styles/theme variants,
typed item templates, a windowed grid, trees, rich text, and canvas/drawing are supported.
`osx-x64` and `osx-arm64` are experimental build candidates, not support claims; native x64/ARM64
execution plus Developer ID signing and notarization remain mandatory. See `Examples/Calculator`
in the SharpTS repository for a complete TSX application.

For a compiler-free, compiled-only Windows x64 executable, publish with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishAot=true
```

The SDK treats every trim/AOT warning as a release failure, omits interpreter sources and symbols,
and checks the executable and complete shipping-directory size budgets. Windows ARM64 managed
cross-publish is supported; Native AOT ARM64 certification still requires the Visual C++ ARM64
linker workload and execution on Windows ARM64 hardware.

MSIX identity, signing, AppInstaller updates, SPDX/SLSA evidence, enterprise deployment, crash
bundles, compatibility, and servicing policy are maintained in the repository's Windows GUI
distribution and support-policy guides. Packaging is intentionally separate from `dotnet publish`
and never embeds signing credentials in an application project.

The SDK package is approximately 38 MiB compressed after including Windows and universal macOS
native assets; a minimal framework-dependent x64 directory is
approximately 47 MB before application assets. Exact sizes vary with SDK/runtime servicing. Raw
Avalonia objects and descriptor registration are available only to managed provider packages. Native controls must
only be touched on the Avalonia dispatcher; application code should use generated props, events,
refs, and services instead. See `docs/gui/sdk-development.md` in the repository for the complete
threading and extension policy.
