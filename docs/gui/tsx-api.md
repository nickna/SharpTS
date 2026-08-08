# SharpTS GUI TSX API

GUI API version 1 is an application-capable, retained TSX surface for Windows Avalonia apps. A
project renders one `Window` root from a function component:

```tsx
import { Button, StackPanel, TextBlock, Window, renderDesktop, useState } from "@sharpts/gui";

function App(): JSX.Element {
    const [count, setCount] = useState(0);
    return (
        <Window title="Counter" width={360} height={220}>
            <StackPanel margin={20} spacing={12}>
                <TextBlock fontSize={28}>Count: {count}</TextBlock>
                <Button onClick={() => setCount(value => value + 1)}>Increment</Button>
            </StackPanel>
        </Window>
    );
}

const root = renderDesktop(<App />);
```

Function components may return an element, primitive text/number, fragment, nested array, or
`null`/`undefined`/boolean. Components use `useState`, `useReducer`, `useEffect`, `useMemo`,
`useCallback`, `useRef`, and `useControlRef`. Hooks must be called unconditionally in the same
order. Effects run after a successful native commit; cleanup runs before a changed effect, when a
component is removed, and when the root is disposed. `createSignal` is retained for state owned
outside the component tree.

Natural children are supported. Text-bearing controls (`TextBlock`, `Button`, `CheckBox`,
`RadioButton`, and `ToggleSwitch`) accept string/number children; container controls accept
elements, fragments, arrays, and conditional children. Stable `key` values preserve native
identity when siblings move.

## Built-in controls

- Layout: `Window`, `StackPanel`, `WrapPanel`, `DockPanel`, `Grid`, `Border`, `ScrollViewer`,
  `ToolBar`, `StatusBar`, `Separator`, `Fragment`.
- Display/actions: `TextBlock`, `Image`, `Button`.
- Forms: `TextBox`, `PasswordBox`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `ComboBox`,
  `ListBox`, `NumericUpDown`, `DatePicker`, `TimePicker`, `Slider`, `ProgressBar`.
- Navigation/commands: `TabControl`, `TabItem`, `Menu`, `MenuItem`.

Props are direct and typed rather than style objects. Common props include size constraints,
per-edge `margin`/`padding` tuples, alignment, visibility, enabled/opacity state, Grid and Dock
placement, tooltip and automation names. Text/content controls add colors, font family/size/style/
weight, alignment, and corner radius where supported. Colors accept Avalonia color strings.

`useControlRef<T>()` returns a stable typed ref with `isAttached` and `focus()`. `onKeyDown` and
`onKeyUp` receive normalized key names and Ctrl/Alt/Shift/Meta/repeat flags; returning `true` marks
the native event handled.

## Assets and desktop services

Files under `Assets` are embedded automatically and used with an `asset:///` URI:

```tsx
<Image source="asset:///icons/app.png" stretch="uniform" />
```

URL inputs are downloaded at build time only, capped at 25 MiB, and require a stable logical name
and SHA-256 pin:

```xml
<ItemGroup>
  <SharpTSGuiRemoteAsset Include="https://example.test/app.png"
                         LogicalName="icons/app.png"
                         Sha256="0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef" />
</ItemGroup>
```

`showMessageDialog`, `showOpenFileDialog`, `showSaveFileDialog`, `showFolderDialog`,
`readClipboardText`, and `writeClipboardText` require a mounted, non-Headless window.

## Version 1 boundaries

The API intentionally supports one `Window` root and built-in descriptors. Combo/list data is
string-backed. Public theme/resource dictionaries, custom controls, item templates, data grids,
trees, drawing/canvas, rich text, multi-window orchestration, and macOS are not part of GUI API 1.
The complete proof application is in `Examples/Calculator`.
