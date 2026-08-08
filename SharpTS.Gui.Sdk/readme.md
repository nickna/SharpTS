# SharpTS.Gui.Sdk

Public-preview MSBuild SDK for building retained, reactive Windows desktop applications from
SharpTS TSX and Avalonia.

```xml
<Project Sdk="SharpTS.Gui.Sdk/0.1.0-preview.1">
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

Publish a self-contained compiled application as one distributable Windows executable:

```powershell
dotnet publish -c Release -r win-x64
# or: dotnet publish -c Release -r win-arm64
```

Single-file publish is the default whenever a runtime identifier is supplied. It embeds the
compiled guest and does not support `--mode interpreted`. To retain both guest modes in a
framework-dependent directory instead, publish with:

```powershell
dotnet publish -c Release -r win-x64 --self-contained false `
  -p:SharpTSGuiPublishMode=Directory
```

Published applications use the Windows GUI subsystem. Fatal startup/runtime failures are appended
to `%LOCALAPPDATA%\SharpTS.Gui\<application>.log`; an interactive launch with no attached console
also displays a minimal native error dialog.

Applications mount a typed element tree with `renderDesktop(<App />)`. Function components can
return elements, primitive text, fragments, arrays, or `null`. The standard state/lifecycle API is
`useState`, `useReducer`, `useEffect`, `useMemo`, `useCallback`, `useRef`, and `useControlRef`;
`createSignal` remains available for external reactive state.

- Layout: `Window`, `StackPanel`, `WrapPanel`, `DockPanel`, `Grid`, `Border`, `ScrollViewer`,
  `ToolBar`, `StatusBar`, `Separator`, and `Fragment`.
- Display and actions: `TextBlock`, `Image`, and `Button`.
- Forms: `TextBox`, `PasswordBox`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `ComboBox`,
  `ListBox`, `NumericUpDown`, `DatePicker`, `TimePicker`, `Slider`, and `ProgressBar`.
- Navigation and commands: `TabControl`, `TabItem`, `Menu`, and `MenuItem`.

Controls expose typed direct props for layout, styling, accessibility names, focus refs, keyboard
events, values, and callbacks. Keys preserve native identity through sibling insertion, removal,
and reordering. Controlled input commits suppress callback echoes while real user changes dispatch
the latest guest callback through one stable native subscription.

The module also provides message, file, folder, and save dialogs plus clipboard read/write helpers.
Files under a project's `Assets` directory are embedded automatically and referenced as
`asset:///relative/path.png`. Reproducible URL assets may be declared with `SharpTSGuiRemoteAsset`
items that supply `LogicalName` and a required SHA-256 digest.

`SharpTSTsConfigPath` may name an existing consumer tsconfig. The SDK inherits it through a
generated overlay while reserving the JSX runtime and `@sharpts/gui` module mappings. Set
`SharpTSVerifyIL` to `true` to verify the persisted hosted guest during compilation.

Current preview boundaries: Windows `win-x64` and `win-arm64` only, one Window root, built-in
controls only, string-backed list/combo items, and no public theme-resource, custom-control,
drawing, data-grid/tree, or multi-window API. macOS support is intentionally deferred and is not
claimed by this preview. See `Examples/Calculator` in the SharpTS repository for a complete TSX
application.
