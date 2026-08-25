import {
    Border,
    Button,
    CheckBox,
    DockPanel,
    DrawingCanvas,
    ErrorBoundary,
    Grid,
    KeyEvent,
    Menu,
    MenuItem,
    PointerEvent,
    ScrollViewer,
    Slider,
    StackPanel,
    StatusBar,
    TextBlock,
    TextBox,
    WrapPanel,
    Window,
    getImageDimensions,
    renderDrawingToPng,
    showMessageDialog,
    showOpenFileDialog,
    showSaveFileDialog,
    useReducer,
    useRef,
} from "@sharpts/gui";
import type { DrawingCommand, DrawingDocument, WindowMetricsEvent } from "@sharpts/gui";
import { readFileSync, statSync, writeFileSync } from "fs";
import { basename, extname } from "path";
import {
    DocumentHistory,
    PaintDocument,
    PaintDraft,
    PaintLayer,
    PaintTool,
    addLayer,
    appendCommand,
    beginDraft,
    clampPoint,
    commandForDraft,
    commitDocument,
    createDocument,
    createHistory,
    createImportedDocument,
    deleteLayer,
    duplicateLayer,
    extendDraft,
    moveLayer,
    parseProject,
    redo,
    renameLayer,
    serializeProject,
    setLayerOpacity,
    setLayerVisibility,
    undo,
    validColor,
} from "./document";

const DEFAULT_COLOR = "#111827";
const EMPTY_DRAWING_COMMANDS: readonly DrawingCommand[] = [];
const COLORS = [
    DEFAULT_COLOR, "#ffffff", "#ef4444", "#f97316", "#f59e0b", "#eab308", "#22c55e", "#14b8a6",
    "#06b6d4", "#3b82f6", "#6366f1", "#8b5cf6", "#d946ef", "#ec4899", "#78716c", "#94a3b8",
];

interface AppState {
    readonly history: DocumentHistory;
    readonly selectedLayerId: string;
    readonly tool: PaintTool;
    readonly color: string;
    readonly size: number;
    readonly filled: boolean;
    readonly zoom: number;
    readonly draft: PaintDraft | null;
    readonly draftColor: string;
    readonly draftSize: number;
    readonly draftFilled: boolean;
    readonly cursor: { readonly x: number; readonly y: number } | null;
    readonly filePath: string | null;
    readonly status: string;
    readonly newDialog: boolean;
    readonly newWidth: string;
    readonly newHeight: string;
    readonly windowWidth: number;
    readonly windowHeight: number;
    readonly layersPaneOpen: boolean;
}

type AppAction =
    | { type: "tool"; tool: PaintTool }
    | { type: "color"; color: string }
    | { type: "size"; size: number }
    | { type: "filled"; filled: boolean }
    | { type: "zoom"; zoom: number }
    | { type: "pointerDown"; point: { x: number; y: number } }
    | { type: "pointerMove"; point: { x: number; y: number } }
    | { type: "pointerUp"; point: { x: number; y: number } }
    | { type: "pointerCancel" }
    | { type: "undo" } | { type: "redo" }
    | { type: "selectLayer"; layerId: string }
    | { type: "addLayer" } | { type: "duplicateLayer" } | { type: "deleteLayer" }
    | { type: "moveLayer"; direction: -1 | 1 }
    | { type: "renameLayer"; name: string }
    | { type: "visibility"; layerId: string; value: boolean }
    | { type: "opacity"; value: number }
    | { type: "load"; document: PaintDocument; filePath: string | null; status: string }
    | { type: "saved"; filePath: string; status: string }
    | { type: "status"; status: string }
    | { type: "showNew"; value: boolean }
    | { type: "newWidth"; value: string }
    | { type: "newHeight"; value: string }
    | { type: "metrics"; width: number; height: number }
    | { type: "toggleLayers"; value: boolean };

function initialState(): AppState {
    const document = createDocument();
    return {
        history: createHistory(document),
        selectedLayerId: document.layers[0].id,
        tool: "brush",
        color: DEFAULT_COLOR,
        size: 8,
        filled: false,
        zoom: 0.75,
        draft: null,
        draftColor: DEFAULT_COLOR,
        draftSize: 8,
        draftFilled: false,
        cursor: null,
        filePath: null,
        status: "Ready · Draw with the Brush tool",
        newDialog: false,
        newWidth: "1024",
        newHeight: "768",
        windowWidth: 1120,
        windowHeight: 700,
        layersPaneOpen: false,
    };
}

function updateDocument(state: AppState, document: PaintDocument, selectedLayerId: string = state.selectedLayerId): AppState {
    return { ...state, history: commitDocument(state.history, document), selectedLayerId };
}

function appReducer(state: AppState, action: any): AppState {
    switch (action.type) {
        case "tool": {
            const tool = action.tool as PaintTool;
            return { ...state, tool, draft: null, status: toolLabel(tool) + " selected" };
        }
        case "color": {
            const color = action.color as string;
            return validColor(color) ? { ...state, color: color.toLowerCase(), status: "Color " + color.toUpperCase() } : state;
        }
        case "size": return { ...state, size: Math.round(Math.max(1, Math.min(64, action.size as number))) };
        case "filled": return { ...state, filled: action.filled as boolean };
        case "zoom": return { ...state, zoom: Math.max(0.25, Math.min(4, action.zoom as number)) };
        case "pointerDown": {
            const point = clampPoint(state.history.document, action.point as { x: number; y: number });
            return {
                ...state,
                draft: beginDraft(state.tool, point),
                draftColor: state.color,
                draftSize: state.size,
                draftFilled: state.filled,
                cursor: point,
                status: "Drawing " + toolLabel(state.tool).toLowerCase(),
            };
        }
        case "pointerMove": {
            const point = clampPoint(state.history.document, action.point as { x: number; y: number });
            return { ...state, cursor: point, draft: state.draft === null ? null : extendDraft(state.draft, point) };
        }
        case "pointerUp": {
            if (state.draft === null) return state;
            const pointerPoint = action.point as { x: number; y: number };
            const clampedPoint = clampPoint(state.history.document, pointerPoint);
            const draft = extendDraft(state.draft, clampedPoint);
            const command = commandForDraft(draft, state.draftColor, state.draftSize, state.draftFilled);
            const document = appendCommand(state.history.document, state.selectedLayerId, command);
            return { ...updateDocument(state, document), draft: null, cursor: clampedPoint, status: toolLabel(state.tool) + " committed" };
        }
        case "pointerCancel": return { ...state, draft: null, status: "Gesture canceled" };
        case "undo": return { ...state, history: undo(state.history), draft: null, status: state.history.past.length === 0 ? "Nothing to undo" : "Undid document change" };
        case "redo": return { ...state, history: redo(state.history), draft: null, status: state.history.future.length === 0 ? "Nothing to redo" : "Redid document change" };
        case "selectLayer": return { ...state, selectedLayerId: action.layerId as string, draft: null };
        case "addLayer": {
            const result = addLayer(state.history.document, state.selectedLayerId);
            return { ...updateDocument(state, result.document, result.layerId), status: "Added layer" };
        }
        case "duplicateLayer": {
            const result = duplicateLayer(state.history.document, state.selectedLayerId);
            return { ...updateDocument(state, result.document, result.layerId), status: "Duplicated layer" };
        }
        case "deleteLayer": {
            const result = deleteLayer(state.history.document, state.selectedLayerId);
            return { ...updateDocument(state, result.document, result.layerId), status: "Deleted layer" };
        }
        case "moveLayer": {
            const direction = action.direction as -1 | 1;
            return { ...updateDocument(state, moveLayer(state.history.document, state.selectedLayerId, direction)), status: direction > 0 ? "Raised layer" : "Lowered layer" };
        }
        case "renameLayer": return updateDocument(state, renameLayer(state.history.document, state.selectedLayerId, action.name as string));
        case "visibility": return updateDocument(state, setLayerVisibility(state.history.document, action.layerId as string, action.value as boolean));
        case "opacity": return updateDocument(state, setLayerOpacity(state.history.document, state.selectedLayerId, action.value as number));
        case "load": {
            const loaded = action.document as PaintDocument;
            return { ...state, history: createHistory(loaded), selectedLayerId: loaded.layers[loaded.layers.length - 1].id, filePath: action.filePath as string | null, draft: null, status: action.status as string, newDialog: false };
        }
        case "saved": return { ...state, history: createHistory(state.history.document), filePath: action.filePath as string, status: action.status as string };
        case "status": return { ...state, status: action.status as string };
        case "showNew": return { ...state, newDialog: action.value as boolean };
        case "newWidth": return { ...state, newWidth: action.value as string };
        case "newHeight": return { ...state, newHeight: action.value as string };
        case "metrics": {
            const windowWidth = Math.max(1, Math.round(action.width as number));
            const windowHeight = Math.max(1, Math.round(action.height as number));
            if (windowWidth === state.windowWidth && windowHeight === state.windowHeight) return state;
            return { ...state, windowWidth, windowHeight };
        }
        case "toggleLayers": return { ...state, layersPaneOpen: action.value as boolean };
    }
    return state;
}

export interface SharpPaintAppProps { readonly requestClose: () => void; }

export function SharpPaintApp(props: SharpPaintAppProps): JSX.Element {
    const statePair = useReducer<AppState, any>(appReducer, initialState());
    const state = statePair[0];
    const dispatch = statePair[1];
    const closeState = useRef({ bypass: false, promptActive: false });
    const document = state.history.document;
    const selectedLayer = (document.layers.find(layer => layer.id === state.selectedLayerId) || document.layers[0]) as PaintLayer;
    const scaledWidth = Math.max(1, document.width * state.zoom);
    const scaledHeight = Math.max(1, document.height * state.zoom);
    const compact = state.windowWidth < 1120;
    const narrow = state.windowWidth < 900;
    const short = state.windowHeight < 650;
    const showLayers = !narrow || state.layersPaneOpen;
    const toolRailWidth = compact ? 70 : 132;
    const layersWidth = compact ? 220 : 250;
    const swatchSize = compact ? 26 : 30;

    const pointOf = (event: PointerEvent): { x: number; y: number } => ({ x: event.x / state.zoom, y: event.y / state.zoom });
    const onPointerDown = (event: PointerEvent): boolean => {
        if (event.button !== "left" && (event.buttons & 1) === 0) return false;
        dispatch({ type: "pointerDown", point: pointOf(event) });
        return true;
    };
    const onPointerMove = (event: PointerEvent): boolean => {
        dispatch({ type: "pointerMove", point: pointOf(event) });
        return state.draft !== null || (event.buttons & 1) !== 0;
    };
    const onPointerUp = (event: PointerEvent): boolean => {
        dispatch({ type: "pointerUp", point: pointOf(event) });
        return true;
    };

    const confirmDiscard = async (): Promise<boolean> => {
        if (!state.history.dirty) return true;
        const result = await showMessageDialog({ title: "Unsaved SharpPaint document", message: "Discard the changes to this document?", buttons: "yesNo" });
        return result === "yes";
    };
    const requestNew = async (): Promise<void> => {
        try {
            if (await confirmDiscard()) dispatch({ type: "showNew", value: true });
        } catch (error) {
            dispatch({ type: "status", status: "Could not create a new document: " + String(error) });
        }
    };
    const createNew = (): void => {
        const width = Number.parseInt(state.newWidth, 10);
        const height = Number.parseInt(state.newHeight, 10);
        try {
            const next = createDocument(width, height);
            dispatch({ type: "load", document: next, filePath: null, status: "Created " + width + " × " + height + " document" });
        } catch (error) {
            dispatch({ type: "status", status: String(error) });
        }
    };
    const openPath = async (file: string): Promise<void> => {
        const extension = extname(file).toLowerCase();
        if (extension === ".png") {
            if (statSync(file).size > 25 * 1024 * 1024) throw new Error("Imported PNG files are limited to 25 MiB.");
            const dimensions = await getImageDimensions(file);
            if (dimensions.width > 8192 || dimensions.height > 8192) throw new Error("Imported PNG dimensions are limited to 8192 × 8192.");
            const bytes: any = readFileSync(file);
            const dataUri = "data:image/png;base64," + bytes.toString("base64");
            dispatch({ type: "load", document: createImportedDocument(dimensions.width, dimensions.height, dataUri), filePath: null, status: "Imported " + basename(file) });
            return;
        }
        if (statSync(file).size > 100 * 1024 * 1024) throw new Error("SharpPaint projects are limited to 100 MiB.");
        const json = readFileSync(file, "utf8") as string;
        const project = parseProject(json);
        const validatedImages: string[] = [];
        for (const layer of project.layers) {
            for (const command of layer.commands) {
                if (command.kind !== "image") continue;
                const source = command.source as string;
                if (validatedImages.indexOf(source) >= 0) continue;
                const dimensions = await getImageDimensions(source);
                if (dimensions.width > 8192 || dimensions.height > 8192) throw new Error("Embedded PNG dimensions are limited to 8192 × 8192.");
                validatedImages.push(source);
            }
        }
        dispatch({ type: "load", document: project, filePath: file, status: "Opened " + basename(file) });
    };
    const requestOpen = async (): Promise<void> => {
        try {
            if (!(await confirmDiscard())) return;
            const files = await showOpenFileDialog({
                title: "Open SharpPaint project or PNG",
                filters: [{ name: "SharpPaint projects", patterns: ["*.sharpaint"] }, { name: "PNG images", patterns: ["*.png"] }],
            });
            if (files.length === 0) return;
            await openPath(files[0]);
        } catch (error) {
            dispatch({ type: "status", status: "Could not open file: " + String(error) });
        }
    };
    const saveProject = async (forceDialog: boolean = false): Promise<boolean> => {
        try {
            let file = forceDialog ? null : state.filePath;
            if (file === null) file = await showSaveFileDialog({ title: "Save SharpPaint project", suggestedFileName: "Untitled.sharpaint", defaultExtension: "sharpaint", filters: [{ name: "SharpPaint projects", patterns: ["*.sharpaint"] }] });
            if (file === null) return false;
            if (extname(file).toLowerCase() !== ".sharpaint") file += ".sharpaint";
            writeFileSync(file, serializeProject(document), "utf8");
            dispatch({ type: "saved", filePath: file, status: "Saved " + basename(file) });
            return true;
        } catch (error) {
            dispatch({ type: "status", status: "Could not save project: " + String(error) });
            return false;
        }
    };
    const exportPng = async (): Promise<void> => {
        try {
            let file = await showSaveFileDialog({ title: "Export flattened PNG", suggestedFileName: "SharpPaint.png", defaultExtension: "png", filters: [{ name: "PNG images", patterns: ["*.png"] }] });
            if (file === null) return;
            if (extname(file).toLowerCase() !== ".png") file += ".png";
            const drawing: DrawingDocument = {
                width: document.width,
                height: document.height,
                layers: document.layers.map(layer => ({ isVisible: layer.isVisible, opacity: layer.opacity, commands: layer.commands as readonly DrawingCommand[] })),
            };
            await renderDrawingToPng(drawing, file);
            dispatch({ type: "status", status: "Exported " + basename(file) });
        } catch (error) {
            dispatch({ type: "status", status: "Could not export PNG: " + String(error) });
        }
    };
    const onCloseRequested = (): boolean => {
        if (closeState.current.bypass || !state.history.dirty) return false;
        if (closeState.current.promptActive) return true;
        closeState.current.promptActive = true;
        void confirmDiscard().then(confirmed => {
            closeState.current.promptActive = false;
            if (!confirmed) return;
            closeState.current.bypass = true;
            try { props.requestClose(); }
            finally { closeState.current.bypass = false; }
        }, error => {
            closeState.current.promptActive = false;
            dispatch({ type: "status", status: "Could not confirm close: " + String(error) });
        });
        return true;
    };
    const onKeyDown = (event: KeyEvent): boolean => {
        const key = event.key.toLowerCase();
        if (event.ctrl && key === "z") { dispatch({ type: event.shift ? "redo" : "undo" }); return true; }
        if (event.ctrl && key === "y") { dispatch({ type: "redo" }); return true; }
        if (event.ctrl && key === "n") { void requestNew(); return true; }
        if (event.ctrl && key === "o") { void requestOpen(); return true; }
        if (event.ctrl && key === "s") { void saveProject(event.shift); return true; }
        if (event.ctrl && key === "e") { void exportPng(); return true; }
        if (event.ctrl || event.alt || event.meta) return false;
        if (key === "b") dispatch({ type: "tool", tool: "brush" });
        else if (key === "e") dispatch({ type: "tool", tool: "eraser" });
        else if (key === "l") dispatch({ type: "tool", tool: "line" });
        else if (key === "r") dispatch({ type: "tool", tool: "rectangle" });
        else if (key === "o") dispatch({ type: "tool", tool: "ellipse" });
        else if (event.key === "+") dispatch({ type: "zoom", zoom: state.zoom + 0.25 });
        else if (event.key === "-") dispatch({ type: "zoom", zoom: state.zoom - 0.25 });
        else return false;
        return true;
    };
    const openDroppedFile = async (file: string): Promise<void> => {
        try {
            if (!(await confirmDiscard())) return;
            await openPath(file);
        } catch (error) {
            dispatch({ type: "status", status: "Could not open dropped file: " + String(error) });
        }
    };
    const onDrop = async (event: { readonly files: readonly string[] }): Promise<void> => {
        if (event.files.length === 0) return;
        await openDroppedFile(event.files[0]);
    };
    const previewCommand = state.draft === null
        ? null
        : commandForDraft(state.draft, state.draftColor, state.draftSize, state.draftFilled) as DrawingCommand;
    // DrawingCommand and PaintCommand are structurally compatible. Keep this
    // adapter dynamic to avoid materializing SharpTS's object-union wrapper.
    const layerCommands = (layer: PaintLayer): any => {
        if (layer.id !== state.selectedLayerId || previewCommand === null) return layer.commands as any;
        const committed: any = layer.commands;
        const combined: any[] = [];
        for (let index = 0; index < committed.length; index++) combined.push(committed[index]);
        combined.push(previewCommand as any);
        return combined;
    };
    const commandCount = document.layers.reduce((sum, layer) => sum + layer.commands.length, 0);

    return (
        <Window title={(state.history.dirty ? "● " : "") + "SharpPaint" + (state.filePath === null ? " · Untitled" : " · " + basename(state.filePath))}
            width={1120} height={700} minWidth={720} minHeight={480} canResize={true} theme="light"
            onMetricsChanged={(event: WindowMetricsEvent) => dispatch({ type: "metrics", width: event.clientWidth, height: event.clientHeight })}
            onKeyDown={onKeyDown} onCloseRequested={onCloseRequested} allowDrop={true}
            onDragOver={event => event.files.length > 0 ? "copy" : "none"} onDrop={onDrop}>
            <Grid rows="*" columns="*">
                <DockPanel lastChildFill={true}>
                    <Menu dock="top">
                        <MenuItem header="File">
                            <MenuItem key="menu-new" header="New…     Ctrl+N" onClick={requestNew} />
                            <MenuItem key="menu-open" header="Open…     Ctrl+O" onClick={requestOpen} />
                            <MenuItem key="menu-save" header="Save     Ctrl+S" onClick={() => saveProject(false)} />
                            <MenuItem key="menu-save-as" header="Save As…     Ctrl+Shift+S" onClick={() => saveProject(true)} />
                            <MenuItem key="menu-export" header="Export PNG…     Ctrl+E" onClick={exportPng} />
                        </MenuItem>
                        <MenuItem header="Edit">
                            <MenuItem header="Undo     Ctrl+Z" isEnabled={state.history.past.length > 0} onClick={() => dispatch({ type: "undo" })} />
                            <MenuItem header="Redo     Ctrl+Y" isEnabled={state.history.future.length > 0} onClick={() => dispatch({ type: "redo" })} />
                            <MenuItem header="Selection & transforms · planned" isEnabled={false} toolTip="Needs selection geometry, transforms, and handles in SharpTS.Gui" />
                        </MenuItem>
                        <MenuItem header="Effects">
                            <MenuItem header="Blur · planned" isEnabled={false} toolTip="Needs a pixel filter pipeline" />
                            <MenuItem header="Color adjustments · planned" isEnabled={false} toolTip="Needs pixel readback and filter primitives" />
                        </MenuItem>
                    </Menu>
                    <Border dock="top" padding={8} background="#ffffff" borderBrush="#e2e8f0" borderThickness={1}>
                        <WrapPanel spacing={8} orientation="horizontal">
                            <Button key="new" automationName="New document" toolTip="New document · Ctrl+N" onClick={requestNew}>New</Button>
                            <Button key="open" automationName="Open document" toolTip="Open · Ctrl+O" onClick={requestOpen}>Open</Button>
                            <Button key="save" automationName="Save document" toolTip="Save · Ctrl+S" onClick={() => saveProject(false)}>Save</Button>
                            <Button key="undo" automationName="Undo" isEnabled={state.history.past.length > 0} onClick={() => dispatch({ type: "undo" })}>↶</Button>
                            <Button key="redo" automationName="Redo" isEnabled={state.history.future.length > 0} onClick={() => dispatch({ type: "redo" })}>↷</Button>
                            <Button key="layers-toggle" automationName="Show layers" isVisible={narrow} onClick={() => dispatch({ type: "toggleLayers", value: true })}>Layers</Button>
                            <TextBlock margin={8} foreground="#475569">Size</TextBlock>
                            <Slider key="brush-size" automationName="Brush size" width={compact ? 110 : 150} minimum={1} maximum={64} value={state.size} onValueChanged={value => dispatch({ type: "size", size: value })} />
                            <TextBlock margin={5} minWidth={42}>{state.size + " px"}</TextBlock>
                            <CheckBox isChecked={state.filled} onCheckedChanged={value => dispatch({ type: "filled", filled: value })} toolTip="Fill rectangles and ellipses">
                                <TextBlock key="filled-label" foreground="#1f2937">{compact ? "Filled" : "Filled shapes"}</TextBlock>
                            </CheckBox>
                        </WrapPanel>
                    </Border>
                    <StatusBar dock="bottom" padding={8} background="#f8fafc" borderBrush="#cbd5e1" borderThickness={1}>
                        <Grid columns={narrow ? "*,0,0,54" : compact ? "*,auto,120,60" : "*,auto,150,64"} rows="auto">
                            <TextBlock key="status" automationName="Status" foreground="#334155">{state.status}</TextBlock>
                            <TextBlock gridColumn={1} margin={8} isVisible={!narrow} foreground="#64748b">{state.cursor === null ? "—" : Math.round(state.cursor.x) + ", " + Math.round(state.cursor.y)}</TextBlock>
                            <Slider key="zoom" automationName="Zoom" gridColumn={2} isVisible={!narrow} minimum={0.25} maximum={4} value={state.zoom} onValueChanged={value => dispatch({ type: "zoom", zoom: value })} />
                            <TextBlock gridColumn={3} horizontalAlignment="right">{Math.round(state.zoom * 100) + "%"}</TextBlock>
                            <TextBlock key="layout-mode" isVisible={false}>{narrow ? "narrow" : compact ? "compact" : short ? "short" : "wide"}</TextBlock>
                        </Grid>
                    </StatusBar>
                    <Border dock="bottom" background="#ffffff" borderBrush="#cbd5e1" borderThickness={1} padding={9}>
                        <ScrollViewer horizontalScrollBarVisibility="auto" verticalScrollBarVisibility="disabled">
                            <StackPanel orientation="horizontal" spacing={7}>
                                <Border width={swatchSize + 4} height={swatchSize + 4} background={state.color} borderBrush="#0f172a" borderThickness={2} cornerRadius={5}><TextBlock> </TextBlock></Border>
                                {COLORS.map(color => <Button key={color} automationName={"Color " + color} toolTip={color} width={swatchSize} height={swatchSize} background={color} onClick={() => dispatch({ type: "color", color })}> </Button>)}
                                <TextBox key="custom-color" automationName="Custom color" width={104} text={state.color} maxLength={9} onTextChanged={value => { if (validColor(value)) dispatch({ type: "color", color: value }); }} />
                            </StackPanel>
                        </ScrollViewer>
                    </Border>
                    <Border dock="left" width={toolRailWidth} background="#f8fafc" borderBrush="#cbd5e1" borderThickness={1} padding={compact ? 7 : 10}>
                        <ScrollViewer verticalScrollBarVisibility="auto" horizontalScrollBarVisibility="disabled">
                            <StackPanel spacing={8}>
                                <TextBlock fontSize={12} fontWeight="bold" horizontalAlignment={compact ? "center" : "left"} foreground="#64748b">{compact ? "" : "TOOLS"}</TextBlock>
                                {toolButton("brush", "✎", "Brush · B", compact, state, dispatch)}
                                {toolButton("eraser", "⌫", "Eraser · E · removes pixels", compact, state, dispatch)}
                                {toolButton("line", "╱", "Line · L", compact, state, dispatch)}
                                {toolButton("rectangle", "□", "Rectangle · R", compact, state, dispatch)}
                                {toolButton("ellipse", "○", "Ellipse · O", compact, state, dispatch)}
                                <Button isEnabled={false} toolTip="Planned: needs pixel readback">{compact ? "▣" : "Fill"}</Button>
                                <Button isEnabled={false} toolTip="Planned: needs pixel sampling">{compact ? "◎" : "Picker"}</Button>
                                <Button isEnabled={false} toolTip="Planned: needs editable text overlays">{compact ? "T" : "Text"}</Button>
                            </StackPanel>
                        </ScrollViewer>
                    </Border>
                    <Border key="layers-panel" dock="right" width={layersWidth} isVisible={showLayers} background="#f8fafc" borderBrush="#cbd5e1" borderThickness={1} padding={12}>
                        <Grid rows="auto,auto,*,auto" columns="*">
                            <Grid columns="*,auto" rows="auto">
                                <TextBlock fontSize={13} fontWeight="bold" foreground="#475569">LAYERS</TextBlock>
                                <Button gridColumn={1} isVisible={narrow} automationName="Close layers" onClick={() => dispatch({ type: "toggleLayers", value: false })}>×</Button>
                            </Grid>
                            <WrapPanel gridRow={1} spacing={5} orientation="horizontal" margin={8}>
                                <Button key="add-layer" automationName="Add layer" onClick={() => dispatch({ type: "addLayer" })}>＋</Button>
                                <Button key="duplicate-layer" automationName="Duplicate layer" onClick={() => dispatch({ type: "duplicateLayer" })}>⧉</Button>
                                <Button key="delete-layer" automationName="Delete layer" onClick={() => dispatch({ type: "deleteLayer" })}>−</Button>
                                <Button key="raise-layer" automationName="Raise layer" onClick={() => dispatch({ type: "moveLayer", direction: 1 })}>↑</Button>
                                <Button key="lower-layer" automationName="Lower layer" onClick={() => dispatch({ type: "moveLayer", direction: -1 })}>↓</Button>
                            </WrapPanel>
                            <ScrollViewer gridRow={2} verticalScrollBarVisibility="auto" horizontalScrollBarVisibility="disabled">
                                <StackPanel spacing={5}>
                                    {document.layers.slice().reverse().map(layer => layerRow(layer, state.selectedLayerId, dispatch))}
                                </StackPanel>
                            </ScrollViewer>
                            <ScrollViewer gridRow={3} maxHeight={short ? 132 : 210} verticalScrollBarVisibility="auto" horizontalScrollBarVisibility="disabled">
                                <StackPanel spacing={8} margin={8}>
                                    <TextBlock fontSize={12} foreground="#64748b">Layer name</TextBlock>
                                    <TextBox key="layer-name" automationName="Layer name" text={selectedLayer.name} maxLength={80} onTextChanged={name => dispatch({ type: "renameLayer", name })} />
                                    <TextBlock fontSize={12} foreground="#64748b">Opacity · {Math.round(selectedLayer.opacity * 100) + "%"}</TextBlock>
                                    <Slider key="layer-opacity" automationName="Layer opacity" minimum={0} maximum={1} value={selectedLayer.opacity} onValueChanged={value => dispatch({ type: "opacity", value })} />
                                    <Button key="merge-layer" isVisible={!short} isEnabled={false} toolTip="Planned: requires layer flattening without losing eraser semantics">Merge down · planned</Button>
                                    <TextBlock key="command-count" automationName="Command count" fontSize={11} foreground="#94a3b8">{commandCount + " commands · " + document.layers.length + " layers"}</TextBlock>
                                </StackPanel>
                            </ScrollViewer>
                        </Grid>
                    </Border>
                    <ScrollViewer horizontalScrollBarVisibility="auto" verticalScrollBarVisibility="auto">
                        <Border margin={28} padding={0} background="#ffffff" borderBrush="#64748b" borderThickness={1} width={scaledWidth} height={scaledHeight}>
                            <Grid width={scaledWidth} height={scaledHeight}>
                                {document.layers.map(layer => (
                                    <DrawingCanvas key={layer.id} width={scaledWidth} height={scaledHeight}
                                        coordinateWidth={document.width} coordinateHeight={document.height}
                                        isVisible={layer.isVisible} opacity={layer.opacity}
                                        commands={layerCommands(layer)} />
                                ))}
                                <DrawingCanvas key="paint-surface" automationName="Paint surface" width={scaledWidth} height={scaledHeight}
                                    coordinateWidth={document.width} coordinateHeight={document.height}
                                    commands={EMPTY_DRAWING_COMMANDS} capturePointerOnPress={true}
                                    onPointerDown={onPointerDown} onPointerMove={onPointerMove} onPointerUp={onPointerUp}
                                    onPointerCancel={() => { dispatch({ type: "pointerCancel" }); return true; }} />
                            </Grid>
                        </Border>
                    </ScrollViewer>
                </DockPanel>
                {state.newDialog ? (
                    <Border background="#cc0f172a" padding={40}>
                        <Border width={420} horizontalAlignment="center" verticalAlignment="center" padding={24} background="#ffffff" cornerRadius={12} borderBrush="#cbd5e1" borderThickness={1}>
                            <StackPanel spacing={13}>
                                <TextBlock fontSize={24} fontWeight="bold" foreground="#0f172a">New document</TextBlock>
                                <TextBlock foreground="#64748b">Choose pixel dimensions from 1 to 8192.</TextBlock>
                                <Grid columns="110,*" rows="auto,auto">
                                    <TextBlock margin={6}>Width</TextBlock>
                                    <TextBox key="new-width" gridColumn={1} text={state.newWidth} maxLength={4} onTextChanged={value => dispatch({ type: "newWidth", value })} />
                                    <TextBlock gridRow={1} margin={6}>Height</TextBlock>
                                    <TextBox key="new-height" gridRow={1} gridColumn={1} text={state.newHeight} maxLength={4} onTextChanged={value => dispatch({ type: "newHeight", value })} />
                                </Grid>
                                <StackPanel orientation="horizontal" spacing={8} horizontalAlignment="right">
                                    <Button key="cancel-new" onClick={() => dispatch({ type: "showNew", value: false })}>Cancel</Button>
                                    <Button key="create-new" automationName="Create document" background="#2563eb" foreground="#ffffff" onClick={createNew}>Create</Button>
                                </StackPanel>
                            </StackPanel>
                        </Border>
                    </Border>
                ) : null}
            </Grid>
        </Window>
    );
}

function toolButton(tool: PaintTool, glyph: string, tip: string, compact: boolean, state: AppState, dispatch: (action: AppAction) => void): JSX.Element {
    const active = state.tool === tool;
    const content = compact
        ? <TextBlock key={tool + "-glyph"} fontSize={18} foreground={active ? "#1d4ed8" : "#334155"}>{glyph}</TextBlock>
        : <StackPanel orientation="horizontal" spacing={8}>
            <TextBlock key={tool + "-glyph"} width={20} textAlignment="center" fontSize={17} foreground={active ? "#1d4ed8" : "#334155"}>{glyph}</TextBlock>
            <TextBlock key={tool + "-label"} verticalAlignment="center" foreground={active ? "#1d4ed8" : "#334155"}>{toolLabel(tool)}</TextBlock>
        </StackPanel>;
    return <Button key={tool} automationName={toolLabel(tool) + " tool"} toolTip={tip} height={46} fontSize={12}
        horizontalContentAlignment={compact ? "center" : "left"}
        background={active ? "#dbeafe" : "#ffffff"} foreground={active ? "#1d4ed8" : "#334155"}
        onClick={() => dispatch({ type: "tool", tool })}>{content}</Button>;
}
function toolLabel(tool: PaintTool): string {
    switch (tool) { case "brush": return "Brush"; case "eraser": return "Eraser"; case "line": return "Line"; case "rectangle": return "Rectangle"; case "ellipse": return "Ellipse"; }
    return "Tool";
}
function layerRow(layer: PaintLayer, selectedLayerId: string, dispatch: (action: AppAction) => void): JSX.Element {
    const selected = layer.id === selectedLayerId;
    return (
        <Border key={layer.id + "-row"} background={selected ? "#dbeafe" : "#ffffff"} cornerRadius={5}>
            <Grid columns="34,*" rows="auto">
                <CheckBox key={layer.id + "-visible"} automationName={"Show " + layer.name} isChecked={layer.isVisible}
                    onCheckedChanged={value => dispatch({ type: "visibility", layerId: layer.id, value })}> </CheckBox>
                <Button key={layer.id + "-select"} automationName={"Select " + layer.name} gridColumn={1} horizontalContentAlignment="left"
                    background={selected ? "#dbeafe" : "#ffffff"} foreground="#1e293b" onClick={() => dispatch({ type: "selectLayer", layerId: layer.id })}>
                    {layer.name + " · " + Math.round(layer.opacity * 100) + "%"}
                </Button>
            </Grid>
        </Border>
    );
}

export function SharpPaintShowcase(props: SharpPaintAppProps): JSX.Element {
    return (
        <ErrorBoundary fallback={(error: unknown, reset: () => void) => (
            <Window title="SharpPaint · Recovery" width={520} height={300} theme="light">
                <Border padding={28} background="#fff7ed">
                    <StackPanel spacing={14}>
                        <TextBlock fontSize={24} fontWeight="bold" foreground="#9a3412">SharpPaint hit a snag</TextBlock>
                        <TextBlock textWrapping="wrap" foreground="#7c2d12">{String(error)}</TextBlock>
                        <Button automationName="Retry SharpPaint" background="#2563eb" foreground="#ffffff" onClick={reset}>Retry editor</Button>
                    </StackPanel>
                </Border>
            </Window>
        )}>
            <SharpPaintApp requestClose={props.requestClose} />
        </ErrorBoundary>
    );
}
