import {
    Border,
    Button,
    DockPanel,
    ErrorBoundary,
    Grid,
    Menu,
    MenuItem,
    NumericUpDown,
    ProgressBar,
    ToggleButton,
    ScrollViewer,
    StackPanel,
    TextBlock,
    Window,
    commandButton,
    commandMenu,
    findCommand,
    useReducer,
    useState
} from "@sharpts/gui";
import type { DesktopCommand, KeyEvent, PointerEvent } from "@sharpts/gui";
import { basename } from "path";
import { PaintDocument, PaintTool, createDocument, createTextCommand } from "./document";
import { AppAction, AppState, appReducer, initialState, toolLabel } from "./editor-state";
import { DARK, LIGHT, Icon } from "./controls";
import { EditorCanvas } from "./EditorCanvas";
import { LayerPanel } from "./LayerPanel";
import { EffectPanel } from "./EffectPanel";
import { ColorPalette, ToolOptions } from "./ToolOptions";
import { DocumentSession, useDocumentSession } from "./document-session";
import { useGraphicsSession } from "./graphics-session";

export interface SharpPaintAppProps {
    readonly requestClose: () => void;
    readonly initialDocument?: PaintDocument;
    /** Override persistent storage for tests or portable installations. */
    readonly storageDirectory?: string;
}
const TOOLS: PaintTool[] = ["brush", "eraser", "line", "rectangle", "ellipse", "fill", "picker", "text"];
const SHORTCUTS = ["B", "E", "L", "R", "O", "F", "I", "T"];

function Editor(
    props: SharpPaintAppProps & {
        state: AppState;
        dispatch: (action: AppAction) => void;
        session: DocumentSession;
    }
): JSX.Element {
    const { state, dispatch } = props;
    const document = state.history.document;
    const palette = state.theme === "dark" ? DARK : LIGHT;
    const narrow = state.windowWidth < 900;
    const compact = state.windowWidth < 1120;
    const showLayers = !narrow || state.layersPaneOpen;
    const selected = document.layers.find((layer) => layer.id === state.selectedLayerId)!;
    const [fitRequest, setFitRequest] = useState<number>(0);
    const [before, setBefore] = useState<boolean>(false);
    const [showErrorDetails, setShowErrorDetails] = useState<boolean>(false);
    const session = props.session;
    const graphics = useGraphicsSession(state, dispatch);
    const commitText = (): void => {
        const draft = state.textDraft;
        if (draft === null || !draft.editing) return;
        if (!draft.text) {
            dispatch({ type: "cancelText" });
            return;
        }
        dispatch({
            type: "commitText",
            command: createTextCommand(
                draft.text,
                draft,
                state.color,
                state.fontFamily,
                state.textSize,
                state.textBold,
                state.textItalic
            )
        });
    };
    const enabled = state.busy === null && state.effectDialog === null;
    const commands: DesktopCommand[] = [
        { id: "new", label: "New…", shortcut: "Ctrl+N", enabled, execute: session.createNew },
        { id: "open", label: "Open…", shortcut: "Ctrl+O", enabled, execute: () => session.open() },
        {
            id: "save",
            label: "Save",
            shortcut: "Ctrl+S",
            enabled,
            allowInTextInput: true,
            execute: () => session.saveProject()
        },
        {
            id: "save-as",
            label: "Save As…",
            shortcut: "Ctrl+Shift+S",
            enabled,
            execute: () => session.saveProject(true)
        },
        { id: "export", label: "Export PNG…", shortcut: "Ctrl+E", enabled, execute: session.exportPng },
        {
            id: "undo",
            label:
                "Undo" +
                (state.history.pastLabels.length
                    ? " " + state.history.pastLabels[state.history.pastLabels.length - 1]
                    : ""),
            shortcut: "Ctrl+Z",
            enabled: enabled && state.history.past.length > 0,
            execute: () => dispatch({ type: "undo" })
        },
        {
            id: "redo",
            label: "Redo" + (state.history.futureLabels.length ? " " + state.history.futureLabels[0] : ""),
            shortcut: "Ctrl+Y",
            enabled: enabled && state.history.future.length > 0,
            execute: () => dispatch({ type: "redo" })
        },
        ...TOOLS.map((tool, index) => ({
            id: tool,
            label: toolLabel(tool),
            shortcut: SHORTCUTS[index],
            enabled,
            checked: state.tool === tool,
            execute: () => dispatch({ type: "tool", tool })
        }))
    ];
    const onKeyDown = (event: KeyEvent): boolean => {
        if (state.textDraft?.editing) {
            if (event.ctrl && event.key === "Enter") {
                commitText();
                return true;
            }
            if (event.key === "Escape") {
                dispatch({ type: "cancelText" });
                return true;
            }
        }
        if (event.key === "Escape" && !event.isTextInput && (state.effectDialog || state.busy)) {
            graphics.cancel();
            dispatch({ type: "cancelEffect" });
            return true;
        }
        const command = findCommand(commands, event);
        if (!command) return false;
        void command.execute();
        return true;
    };
    const point = (event: PointerEvent): { x: number; y: number } => {
        let x = event.x / state.zoom,
            y = event.y / state.zoom;
        if (
            event.shift &&
            state.draft &&
            (state.tool === "line" || state.tool === "rectangle" || state.tool === "ellipse")
        ) {
            const start = state.draft.points[0];
            const dx = x - start.x,
                dy = y - start.y;
            if (state.tool === "line") {
                const angle = (Math.round(Math.atan2(dy, dx) / (Math.PI / 4)) * Math.PI) / 4;
                const length = Math.sqrt(dx * dx + dy * dy);
                x = start.x + Math.cos(angle) * length;
                y = start.y + Math.sin(angle) * length;
            } else {
                const size = Math.max(Math.abs(dx), Math.abs(dy));
                x = start.x + (dx < 0 ? -size : size);
                y = start.y + (dy < 0 ? -size : size);
            }
        }
        return { x, y };
    };
    const onDown = (event: PointerEvent): boolean => {
        if (event.button !== "left" || !enabled) return false;
        const p = point(event);
        if (state.tool === "picker") {
            void graphics.pick(p.x, p.y);
            return true;
        }
        if (!selected.isVisible || selected.opacity === 0) {
            dispatch({ type: "status", status: "Show the selected layer before drawing" });
            return true;
        }
        if (state.tool === "fill") graphics.fill(p.x, p.y);
        else
            dispatch(
                state.tool === "text" ? { type: "textStart", point: p } : { type: "pointerDown", point: p }
            );
        return true;
    };
    const cancelEffect = (): void => {
        graphics.cancel();
        setBefore(false);
        dispatch({ type: "cancelEffect" });
    };
    return (
        <Window
            title={
                (state.history.dirty ? "● " : "") +
                (state.filePath ? basename(state.filePath) : "Untitled") +
                " · SharpPaint"
            }
            width={1120}
            height={700}
            minWidth={720}
            minHeight={480}
            theme="system"
            onMetricsChanged={(event) => {
                dispatch({ type: "metrics", width: event.clientWidth, height: event.clientHeight });
                if (state.theme !== event.theme) dispatch({ type: "theme", theme: event.theme });
            }}
            onKeyDown={onKeyDown}
            keyDownRouting="tunnel"
            onCloseRequested={session.requestWindowClose}
            allowDrop={true}
            onDragOver={(event) => (enabled && event.files.length > 0 ? "copy" : "none")}
            onDrop={(event) => {
                if (enabled && event.files.length) void session.open(event.files[0]);
            }}
        >
            <DockPanel lastChildFill={true}>
                <Menu dock="top">
                    <MenuItem header="File">
                        {commands.slice(0, 5).map((command) => (
                            <MenuItem key={"menu-" + command.id} {...commandMenu(command)} />
                        ))}
                        <MenuItem header="Open recent" isEnabled={session.recent.length > 0}>
                            {session.recent.map((file) => (
                                <MenuItem
                                    header={basename(file)}
                                    toolTip={file}
                                    onClick={() => session.open(file)}
                                />
                            ))}
                        </MenuItem>
                        <MenuItem
                            key="recover"
                            header={
                                "Recover document…" +
                                (session.recoveryCount > 0 ? " (" + session.recoveryCount + ")" : "")
                            }
                            onClick={session.recover}
                        />
                    </MenuItem>
                    <MenuItem header="Edit">
                        {commands.slice(5, 7).map((command) => (
                            <MenuItem key={"menu-" + command.id} {...commandMenu(command)} />
                        ))}
                    </MenuItem>
                    <MenuItem header="View">
                        <MenuItem header="Fit canvas" onClick={() => setFitRequest(fitRequest + 1)} />
                        <MenuItem header="Actual size" onClick={() => dispatch({ type: "zoom", zoom: 1 })} />
                    </MenuItem>
                    <MenuItem header="Effects" isEnabled={enabled}>
                        <MenuItem
                            key="effect-blur"
                            header="Gaussian blur…"
                            onClick={() => dispatch({ type: "showEffect", kind: "gaussianBlur" })}
                        />
                        <MenuItem
                            key="effect-brightness"
                            header="Brightness / contrast…"
                            onClick={() => dispatch({ type: "showEffect", kind: "brightnessContrast" })}
                        />
                        <MenuItem
                            key="effect-hue"
                            header="Hue / saturation…"
                            onClick={() => dispatch({ type: "showEffect", kind: "hueSaturation" })}
                        />
                        <MenuItem
                            key="effect-grayscale"
                            header="Grayscale"
                            onClick={() => graphics.effect({ kind: "grayscale" }, false)}
                        />
                        <MenuItem
                            key="effect-invert"
                            header="Invert"
                            onClick={() => graphics.effect({ kind: "invert" }, false)}
                        />
                    </MenuItem>
                </Menu>
                <Border
                    dock="top"
                    background={palette.panel}
                    borderBrush={palette.border}
                    borderThickness={[0, 0, 0, 1] as const}
                    padding={[10, 6] as const}
                >
                    <StackPanel orientation="horizontal" spacing={6}>
                        {commands.slice(0, 3).map((command) => (
                            <Button key={command.id} {...commandButton(command)}>
                                {command.id === "new" ? "New" : command.id === "open" ? "Open" : "Save"}
                            </Button>
                        ))}
                        {commands.slice(5, 7).map((command) => (
                            <Button
                                key={command.id}
                                {...commandButton(command)}
                                width={32}
                                height={32}
                                padding={6}
                            >
                                <Icon name={command.id} color={palette.text} />
                            </Button>
                        ))}
                        <Button
                            key="layers-toggle"
                            isVisible={narrow}
                            onClick={() => dispatch({ type: "toggleLayers", value: !state.layersPaneOpen })}
                        >
                            Layers
                        </Button>
                        <TextBlock
                            foreground={palette.muted}
                            verticalAlignment="center"
                            margin={[12, 0, 0, 0] as const}
                        >
                            {document.width + " × " + document.height + " px"}
                        </TextBlock>
                    </StackPanel>
                </Border>
                <Border dock="top" background={palette.surface} padding={[10, 6] as const}>
                    <ToolOptions
                        state={state}
                        palette={palette}
                        dispatch={dispatch}
                        commitText={commitText}
                    />
                </Border>
                <Border dock="top" isVisible={session.error !== ""} background={palette.panel} padding={10}>
                    <Grid columns="*,auto,auto" rows="auto,auto">
                        <TextBlock key="document-error" foreground={palette.danger} textWrapping="wrap">
                            {session.error}
                        </TextBlock>
                        <Button gridColumn={1} onClick={() => setShowErrorDetails(!showErrorDetails)}>
                            {showErrorDetails ? "Hide details" : "Details"}
                        </Button>
                        <Button
                            gridColumn={2}
                            onClick={() => {
                                session.clearError();
                                setShowErrorDetails(false);
                            }}
                        >
                            Dismiss
                        </Button>
                        <ScrollViewer
                            gridRow={1}
                            gridColumnSpan={3}
                            isVisible={showErrorDetails}
                            maxHeight={90}
                            horizontalScrollBarVisibility="disabled"
                            verticalScrollBarVisibility="auto"
                        >
                            <TextBlock
                                key="document-error-details"
                                foreground={palette.muted}
                                textWrapping="wrap"
                            >
                                {session.errorDetails}
                            </TextBlock>
                        </ScrollViewer>
                    </Grid>
                </Border>
                <Border dock="bottom" background={palette.panel} padding={[10, 5] as const}>
                    <Grid columns="*,auto,auto,auto" rows="auto">
                        <TextBlock
                            key="status"
                            automationName="Status"
                            foreground={palette.muted}
                            verticalAlignment="center"
                        >
                            {state.status}
                        </TextBlock>
                        <Button
                            key="fit"
                            gridColumn={1}
                            onClick={() => setFitRequest(fitRequest + 1)}
                            margin={[8, 0] as const}
                        >
                            Fit
                        </Button>
                        <Button
                            key="actual-size"
                            gridColumn={2}
                            onClick={() => dispatch({ type: "zoom", zoom: 1 })}
                            margin={[0, 0, 6, 0] as const}
                        >
                            100%
                        </Button>
                        <NumericUpDown
                            showButtonSpinner={false}
                            key="zoom-number"
                            gridColumn={3}
                            automationName="Zoom percent"
                            width={92}
                            minimum={1}
                            maximum={800}
                            value={Math.round(state.zoom * 100)}
                            onValueChanged={(value) => {
                                if (value !== null) dispatch({ type: "zoom", zoom: value / 100 });
                            }}
                        />
                        <TextBlock key="layout-mode" isVisible={false}>
                            {narrow
                                ? "narrow"
                                : compact
                                  ? "compact"
                                  : state.windowHeight < 650
                                    ? "short"
                                    : "wide"}
                        </TextBlock>
                    </Grid>
                </Border>
                <Border
                    dock="bottom"
                    background={palette.surface}
                    borderBrush={palette.border}
                    borderThickness={[0, 1, 0, 0] as const}
                    padding={[10, 8] as const}
                >
                    <ColorPalette state={state} palette={palette} dispatch={dispatch} />
                </Border>
                <Border
                    dock="bottom"
                    isVisible={state.busy !== null}
                    background={palette.panel}
                    padding={[12, 6] as const}
                >
                    <Grid columns="*,120,auto" rows="auto">
                        <TextBlock foreground={palette.text} verticalAlignment="center">
                            {state.busy || ""}
                        </TextBlock>
                        <ProgressBar
                            gridColumn={1}
                            minimum={0}
                            maximum={1}
                            value={graphics.progress}
                            margin={8}
                        />
                        <Button
                            key="cancel-operation"
                            gridColumn={2}
                            onClick={() => {
                                graphics.cancel();
                                dispatch({ type: "status", status: "Operation canceled" });
                            }}
                        >
                            Cancel
                        </Button>
                    </Grid>
                </Border>
                <Grid
                    columns={
                        "54,*," + (state.effectDialog !== null ? 260 : showLayers ? (compact ? 230 : 260) : 0)
                    }
                    rows="*"
                >
                    <Border
                        background={palette.panel}
                        borderBrush={palette.border}
                        borderThickness={[0, 0, 1, 0] as const}
                    >
                        <ScrollViewer
                            verticalScrollBarVisibility="auto"
                            horizontalScrollBarVisibility="disabled"
                        >
                            <StackPanel spacing={5} margin={[6, 8] as const}>
                                {TOOLS.map((tool, index) => (
                                    <ToggleButton
                                        key={tool + "-tool"}
                                        isChecked={state.tool === tool}
                                        automationName={toolLabel(tool)}
                                        toolTip={toolLabel(tool) + " · " + SHORTCUTS[index]}
                                        width={40}
                                        height={36}
                                        background={state.tool === tool ? palette.selected : palette.panel}
                                        isEnabled={enabled}
                                        onCheckedChanged={(value) => {
                                            dispatch({ type: "tool", tool });
                                        }}
                                    >
                                        <Icon
                                            name={tool}
                                            color={state.tool === tool ? "#ffffff" : palette.text}
                                        />
                                    </ToggleButton>
                                ))}
                            </StackPanel>
                        </ScrollViewer>
                    </Border>
                    <Border gridColumn={1}>
                        <EditorCanvas
                            state={before ? { ...state, effectPreview: null } : state}
                            palette={palette}
                            fitRequest={fitRequest}
                            dispatch={dispatch}
                            onDown={onDown}
                            onMove={(event) => {
                                dispatch(
                                    state.tool === "text"
                                        ? { type: "textMove", point: point(event) }
                                        : { type: "pointerMove", point: point(event) }
                                );
                                return true;
                            }}
                            onUp={(event) => {
                                if (state.tool === "text")
                                    dispatch({ type: "textEdit", point: point(event) });
                                else if (state.tool !== "fill" && state.tool !== "picker")
                                    dispatch({ type: "pointerUp", point: point(event) });
                                return true;
                            }}
                        />
                    </Border>
                    <Border
                        gridColumn={2}
                        isVisible={state.effectDialog !== null || showLayers}
                        background={palette.panel}
                    >
                        {state.effectDialog !== null ? (
                            <EffectPanel
                                state={state}
                                palette={palette}
                                dispatch={dispatch}
                                before={before}
                                setBefore={setBefore}
                                apply={graphics.apply}
                                cancel={cancelEffect}
                            />
                        ) : (
                            <LayerPanel
                                state={state}
                                palette={palette}
                                dispatch={dispatch}
                                onMerge={graphics.merge}
                            />
                        )}
                    </Border>
                </Grid>
            </DockPanel>
        </Window>
    );
}

/** Document state lives above the presentation boundary, so Retry preserves the working revision and history. */
export function SharpPaintShowcase(props: SharpPaintAppProps): JSX.Element {
    const [state, dispatch] = useReducer<AppState, AppAction>(
        appReducer,
        initialState(props.initialDocument || createDocument())
    );
    const session = useDocumentSession(state, dispatch, props.requestClose, props.storageDirectory);
    return (
        <ErrorBoundary
            fallback={(error: unknown, reset: () => void) => (
                <Window
                    title="SharpPaint · Recovery"
                    width={520}
                    height={360}
                    theme="system"
                    onCloseRequested={session.requestWindowClose}
                >
                    <Border padding={28}>
                        <StackPanel spacing={14}>
                            <TextBlock fontSize={24} fontWeight="semibold">
                                The editor hit a snag
                            </TextBlock>
                            <TextBlock textWrapping="wrap">
                                Your document and undo history are still held in memory. Retry to reopen the
                                editor.
                            </TextBlock>
                            <TextBlock key="fatal-error" textWrapping="wrap">
                                {String(error)}
                            </TextBlock>
                            <Button automationName="Retry SharpPaint" onClick={reset}>
                                Retry editor
                            </Button>
                            <Button onClick={() => session.saveProject(true)}>Save recovery copy…</Button>
                        </StackPanel>
                    </Border>
                </Window>
            )}
        >
            <Editor {...props} state={state} dispatch={dispatch} session={session} />
        </ErrorBoundary>
    );
}

export function SharpPaintApp(props: SharpPaintAppProps): JSX.Element {
    return <SharpPaintShowcase {...props} />;
}
