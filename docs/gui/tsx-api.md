# SharpTS GUI TSX API

GUI API version 2 is an application-capable, retained TSX surface for Windows Avalonia apps.
`renderDesktop` remains the concise, compatible entry point for an application with one window:

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

Applications that own more than one window use an explicit application session:

```tsx
import { Window, TextBlock, createDesktopApplication } from "@sharpts/gui";

const app = createDesktopApplication({
    shutdownMode: "onMainWindowClose",
    onUnhandledError: (error, failedWindow) => {
        console.error(error);
        failedWindow.dispose();
    },
});
const main = app.createWindow(
    <Window title="Main"><TextBlock>Main window</TextBlock></Window>,
    { main: true },
);
const dialog = app.createWindow(
    <Window title="Details"><TextBlock>Owned window</TextBlock></Window>,
    { owner: main, modal: true },
);
dialog.activate();
await dialog.closed;
```

The shutdown modes are `onLastWindowClose` (default), `onMainWindowClose`, and `explicit`.
`app.shutdown(exitCode)` performs an explicit ordered guest/host shutdown. Closing or disposing a
non-terminating window releases only its native tree, refs, subscriptions, hooks, and effects.
Owned and modal windows must belong to the same application. An uncaught asynchronous render or
effect failure invokes the window handler first and disposes only that window; event-handler and
detached asynchronous errors remain host-level failures. Initial mount errors are thrown by
`createWindow` before it returns.

Application options also accept primitive `resources` and native Avalonia `styles`. Selectors are
allow-listed built-in control kinds plus optional class names; setters are a reviewed property set
and may refer to resources. This remains trimming-safe and does not use runtime reflection:

```tsx
const app = createDesktopApplication({
    resources: { accent: "#336699", commandPadding: 8 },
    styles: [{
        selector: { control: "Button", classes: ["primary"] },
        setters: {
            background: { resource: "accent" },
            padding: { resource: "commandPadding" },
        },
    }],
});
const window = app.createWindow(
    <Window><Button classes={["primary"]}>Save</Button></Window>,
);
console.log(window.findResource("accent"));
```

Resources and styles are immutable after the first window is created. Written JSX props have
normal Avalonia local-value precedence; omitted props continue to inherit matching styles after
reactive updates. Window `theme="system" | "light" | "dark"` selects the native theme variant.

Function components may return an element, primitive text/number, fragment, nested array, or
`null`/`undefined`/boolean. Components use `useState`, `useReducer`, `useEffect`, `useMemo`,
`useCallback`, `useRef`, and `useControlRef`. Hooks must be called unconditionally in the same
order. Effects run after the renderer leaves render mode and after a successful native commit, so
effect setup and changed-effect cleanup may queue state updates. Cleanup runs before a changed
effect, when a component is removed, and when the root is disposed. `createSignal` is retained for
state owned outside the component tree.

`ErrorBoundary` catches descendant render failures and effect setup/cleanup failures. Its
`fallback(error, reset)` callback returns the recovery UI; calling `reset()` retries the protected
subtree on a subsequent render. Event-handler and detached asynchronous failures remain host-level
errors.

Natural children are supported. Text-bearing controls (`TextBlock`, `Button`, `CheckBox`,
`RadioButton`, and `ToggleSwitch`) accept string/number children; container controls accept
elements, fragments, arrays, and conditional children. Stable `key` values preserve the complete
logical component or fragment subtree, including native identity and hook state, when siblings
move. Fragments are layout-transparent and never insert a native panel. Duplicate keys at any
sibling level are rejected before native mutation.

Built-in descriptors validate parsed values before mutation. If a native setter fails during an
update, the renderer reverses the commit to the last VNode tree. A second failure during recovery
disposes the damaged window root and reports a combined fatal host error.

## Built-in controls

- Layout: `Window`, `StackPanel`, `WrapPanel`, `DockPanel`, `Grid`, `Border`, `ScrollViewer`,
  `ToolBar`, `StatusBar`, `Separator`, `Fragment`.
- Display/actions: `TextBlock`, `Image`, `Button`.
- Forms: `TextBox`, `PasswordBox`, `CheckBox`, `RadioButton`, `ToggleSwitch`, `ComboBox`,
  `ListBox`, `NumericUpDown`, `DatePicker`, `TimePicker`, `Slider`, `ProgressBar`.
- Navigation/commands: `TabControl`, `TabItem`, `Menu`, `MenuItem`.
- Data/rendering: `ItemsControl`, `VirtualizingList`, `TreeView`, `TreeViewItem`, `Canvas`,
  `RichTextBlock`, and `DrawingCanvas`.

Props are direct and typed rather than style objects. Common props include size constraints,
per-edge `margin`/`padding` tuples, alignment, visibility, enabled/opacity state, Grid and Dock
placement, tooltip and automation names. Text/content controls add colors, font family/size/style/
weight, alignment, and corner radius where supported. Colors accept Avalonia color strings.

`useControlRef<T>()` returns a stable typed ref with `isAttached` and `focus()`. `onKeyDown` and
`onKeyUp` receive normalized key names and Ctrl/Alt/Shift/Meta/repeat flags; returning `true` marks
the native event handled.

## Typed item templates and drawing

`createVirtualList`, `createTree`, and `createVirtualDataGrid` infer the item type from `items` and
accept typed key/template callbacks. They return ordinary `GuiElement` values, so they can be
embedded in JSX expressions. The list uses a native virtualizing `ListBox`; list and grid factories
materialize only the requested visible range plus `overscan`, preserving keyed native identity
while a caller advances `startIndex`. Tree nodes use native `TreeViewItem` expansion events.

`RichTextBlock` accepts independently styled text runs. `Canvas` supports `canvasLeft` and
`canvasTop` attached props. `DrawingCanvas` retains validated line, rectangle, and ellipse commands
and redraws only when its command contract changes. All of these controls remain in the generated
descriptor/hash/documentation contract and are available to completion and hover.

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

`getDesktopDisplays()` requires a mounted window and reports every display visible to that window:
name, primary state, pixel bounds, working area, orientation, and scaling factor. This is the
supported basis for high-DPI and multiple-monitor layout decisions. Common `automationName`,
keyboard handlers, typed ref `focus()`, committed text-change events (including IME commits), and
the Window `system`/`light`/`dark` theme selector map directly to Avalonia native behavior.

## Current version 2 boundaries

Each window still requires exactly one `Window` root and built-in descriptors. Combo/list data is
string-backed. Public custom controls, arbitrary Avalonia control templates, a full editing
`DataGrid`, and macOS are not yet supported. Typed item templates, a windowed virtual grid, native
list/tree hosts, rich text, canvas/drawing, resources, class/type selectors, styles, theme variants,
and resource lookup are supported. Multi-window orchestration is available through
`createDesktopApplication`; `renderDesktop` remains the one-window convenience API.
API 1 manifests are rejected with a migration diagnostic; see
[Migrating GUI API 1 to 2](migrating-api-1-to-2.md). The complete proof application is in
`Examples/Calculator`.
