import {
    Border,
    Button,
    ButtonHandle,
    CommonProps,
    Fragment,
    Grid,
    DrawingCanvas,
    RichTextBlock,
    TextBlock,
    Window,
    createDesktopApplication,
    defineCustomControl,
    createTree,
    createVirtualDataGrid,
    createVirtualList,
    getDesktopPlatformInfo,
    getLaunchArguments,
    openExternal,
    printFile,
    showItemInFolder,
    useControlRef,
} from "@sharpts/gui";
import { CalculatorButton, CalculatorButtonDefinition } from "../../../Examples/Calculator/CalculatorApp";

const definition: CalculatorButtonDefinition = {
    id: "test", label: "1", automationName: "One", shortcut: "1",
    role: "digit", row: 0, column: 0, action: { type: "digit", digit: "1" },
};
const buttonRef = useControlRef<ButtonHandle>();
interface BadgeProps extends CommonProps<unknown> { label: string; }
const Badge = defineCustomControl<BadgeProps>("example.widgets.Badge");

export const positive = (
    <Grid>
        <CalculatorButton key="typed-component" definition={definition} active={false} onPress={() => {}} />
        <TextBlock>{["recursive", 1, false, null]}</TextBlock>
        <Button ref={buttonRef}>Text only</Button>
        <Border allowDrop onDragOver={event => event.files.length > 0 ? "copy" : "none"}
            onDrop={event => { void event.text; }}><TextBlock>One logical child</TextBlock></Border>
        <Badge label="custom" automationName="Custom badge" />
        <Fragment><TextBlock>A</TextBlock><TextBlock>B</TextBlock></Fragment>
    </Grid>
);

const application = createDesktopApplication({
    shutdownMode: "onMainWindowClose",
    resources: { accent: "#336699", spacing: 8 },
    styles: [{
        selector: { control: "Button", classes: ["primary"] },
        setters: { background: { resource: "accent" }, padding: { resource: "spacing" } },
    }],
    onUnhandledError: (_error, failedWindow) => failedWindow.dispose(),
});
const mainWindow = application.createWindow(
    <Window title="Main"><Button classes={["primary"]}>Main</Button></Window>,
    { main: true },
);
const modalWindow = application.createWindow(
    <Window title="Dialog"><TextBlock>Dialog</TextBlock></Window>,
    { owner: mainWindow, modal: true },
);
modalWindow.activate();
const tray = application.createTrayIcon({
    icon: "asset:///icon.ico",
    toolTip: "SharpTS",
    menu: [{ id: "open", label: "Open" }, { separator: true }, { id: "quit", label: "Quit" }],
    onMenuItemClick: id => { void id; },
});
tray.update({ icon: "asset:///icon.ico", toolTip: "Updated" });
tray.dispose();
const accent: string | number | boolean | readonly number[] | null = mainWindow.findResource("accent");
void modalWindow.closed;
void getDesktopPlatformInfo().applicationDirectory;
void getLaunchArguments();
void openExternal("https://example.com");
void showItemInFolder("document.txt");
void printFile("document.txt");
application.shutdown(0);

const records = [{ id: 1, name: "One", children: [] as any[] }];
const virtualList = createVirtualList({ items: records, itemKey: item => item.id,
    renderItem: item => <TextBlock>{item.name}</TextBlock>, startIndex: 0, visibleCount: 10 });
const tree = createTree({ items: records, itemKey: item => item.id, itemLabel: item => item.name,
    childrenOf: item => item.children });
const dataGrid = createVirtualDataGrid({ items: records, rowKey: item => item.id,
    startIndex: 0, visibleCount: 10,
    columns: [{ key: "name", header: "Name", renderCell: item => <TextBlock>{item.name}</TextBlock> }] });
export const genericSurfaces = (
    <Grid>
        {virtualList}{tree}{dataGrid}
        <RichTextBlock runs={[{ text: "Rich", fontWeight: "bold" }]} />
        <DrawingCanvas commands={[{ kind: "ellipse", centerX: 10, centerY: 10, radiusX: 5, radiusY: 5, fill: "red" }]} />
    </Grid>
);
