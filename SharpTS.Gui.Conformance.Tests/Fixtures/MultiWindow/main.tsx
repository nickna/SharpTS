import {
    DesktopApplication,
    Button,
    Canvas,
    DrawingCanvas,
    RichTextBlock,
    TextBlock,
    Window,
    createDesktopApplication,
    createSignal,
    createTree,
    createVirtualDataGrid,
    createVirtualList,
    showNotification,
} from "@sharpts/gui";
import { dropText, getProperty, queueMicrotask as queueHostedMicrotask, trace } from "@sharpts/gui/internal-testing";

const [fail, setFail] = createSignal<boolean>(false);
const [stylePhase, setStylePhase] = createSignal<number>(0);
let application: DesktopApplication;

function FailureWindow(): JSX.Element {
    if (fail()) throw new Error("expected isolated window failure");
    return <Window title="Failure probe"><TextBlock>Ready</TextBlock></Window>;
}

function MainWindow(): JSX.Element {
    const items = [{ id: "a", label: "Alpha" }, { id: "b", label: "Beta" }, { id: "c", label: "Gamma" }];
    const treeItems = [{ id: "root", label: "Root", children: [{ id: "leaf", label: "Leaf", children: [] as any[] }] }];
    return <Window title="Main"><Canvas>
        <Button key="styled" classes={["accent"]} canvasLeft={0} canvasTop={0}>{"Main " + stylePhase()}</Button>
        <Button key="drop-target" allowDrop
            onDragOver={event => event.text === "payload" ? "copy" : "none"}
            onDrop={event => { if (event.text !== "payload") throw new Error("Drop payload mismatch."); trace("multi-window-drop"); }}
            canvasLeft={120} canvasTop={0}>Drop</Button>
        {createVirtualList({
            key: "virtual-list", items, itemKey: item => item.id,
            renderItem: item => <TextBlock>{item.label}</TextBlock>,
            startIndex: 0, visibleCount: 2, canvasLeft: 0, canvasTop: 40,
        })}
        {createTree({
            key: "tree", items: treeItems, itemKey: item => item.id, itemLabel: item => item.label,
            childrenOf: item => item.children, canvasLeft: 180, canvasTop: 40,
        })}
        {createVirtualDataGrid({
            key: "grid", items, rowKey: item => item.id, startIndex: 0, visibleCount: 2,
            columns: [{ key: "label", header: "Label", renderCell: item => <TextBlock>{item.label}</TextBlock> }],
            canvasLeft: 360, canvasTop: 40,
        })}
        <RichTextBlock key="rich" runs={[{ text: "Rich", fontWeight: "bold" }, { text: " text", foreground: "#336699" }]} canvasLeft={0} canvasTop={180} />
        <DrawingCanvas key="drawing" automationName="drawing-surface" width={100} height={60}
            commands={[{ kind: "rectangle", x: 2, y: 2, width: 40, height: 20, fill: "#336699" }]}
            canvasLeft={180} canvasTop={180} />
    </Canvas></Window>;
}

application = createDesktopApplication({
    shutdownMode: "onMainWindowClose",
    resources: { accent: "#336699", buttonPadding: 8 },
    styles: [{
        selector: { control: "Button", classes: ["accent"] },
        setters: {
            background: { resource: "accent" },
            padding: { resource: "buttonPadding" },
        },
    }],
    onUnhandledError: (error, window) => {
        if (String(error).indexOf("expected isolated window failure") < 0)
            throw error;
        trace("multi-window-isolated-error");
        if (!window.isDisposed) throw new Error("Failed window was not disposed.");
        if (application.windowCount !== 1) throw new Error("Unrelated window did not retain identity.");
        trace("multi-window-main-retained");
        setTimeout((() => application.shutdown(0)) as any, 10);
    },
});

const mainWindow = application.createWindow(
    <MainWindow />,
    { main: true },
);
const tray = application.createTrayIcon({
    icon: "asset:///headless.ico",
    toolTip: "SharpTS conformance",
    menu: [{ id: "open", label: "Open" }, { separator: true }, { id: "quit", label: "Quit" }],
});
tray.update({ icon: "asset:///headless.ico", toolTip: "Updated" });
tray.dispose();
if (!tray.isDisposed) throw new Error("Tray icon did not dispose.");
trace("multi-window-platform-services");
void showNotification({ title: "SharpTS conformance", message: "Headless delivery", silent: true });
trace("multi-window-notification");
const secondaryWindow = application.createWindow(
    <Window title="Secondary"><TextBlock>Secondary</TextBlock></Window>,
    { owner: mainWindow },
);
application.createWindow(<FailureWindow />);

if (application.windowCount !== 3) throw new Error("Expected three mounted windows.");
if (mainWindow.findResource("accent") !== "#336699") throw new Error("Resource lookup failed.");
trace("multi-window-mounted");
if (dropText("drop-target", "payload") !== "copy") throw new Error("Drag effect mismatch.");
secondaryWindow.close();
if (!secondaryWindow.isDisposed) throw new Error("Secondary close did not dispose its root.");
trace("multi-window-secondary-closed");
setTimeout((() => {
    if (getProperty("styled", "background").indexOf("336699") < 0) throw new Error("Native style did not apply.");
    if (getProperty("drawing", "automationName") !== "drawing-surface") throw new Error("Advanced surface did not mount.");
    trace("multi-window-advanced-surface");
    trace("multi-window-style-applied");
    setStylePhase(1);
    queueHostedMicrotask(() => {
        if (getProperty("styled", "background").indexOf("336699") < 0) throw new Error("Native style was lost after update.");
        trace("multi-window-style-retained");
        setFail(true);
    });
}) as any, 0);
