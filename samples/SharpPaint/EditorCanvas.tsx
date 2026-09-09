import {
    Border,
    Canvas,
    DrawingCanvas,
    Grid,
    ScrollViewer,
    TextBox,
    useViewportNavigation,
    useControlRef,
    useEffect,
    useMemo,
    useState
} from "@sharpts/gui";
import type { BorderHandle, DrawingCommand, GuiElement, PointerEvent, TextBoxHandle } from "@sharpts/gui";
import { commandForDraft, PaintLayer } from "./document";
import { AppAction, AppState } from "./editor-state";
import { Palette } from "./controls";

export function EditorCanvas(props: {
    state: AppState;
    palette: Palette;
    fitRequest: number;
    dispatch: (action: AppAction) => void;
    onDown: (event: PointerEvent) => boolean;
    onMove: (event: PointerEvent) => boolean;
    onUp: (event: PointerEvent) => boolean;
}): GuiElement {
    const state = props.state;
    const document = state.history.document;
    const navigation = useViewportNavigation({
        width: document.width,
        height: document.height,
        zoom: state.zoom,
        fit: state.zoomMode === "fit",
        resetKey: String(state.documentVersion) + ":" + props.fitRequest,
        onZoom: (zoom, automatic) => props.dispatch({ type: "zoom", zoom, automatic })
    });
    const { viewport, offset, width, height, frameWidth, frameHeight, left, top, space } = navigation;
    const [cursor, setCursor] = useState<{ x: number; y: number } | null>(null);
    const textRef = useControlRef<TextBoxHandle>();
    const surfaceRef = useControlRef<BorderHandle>();
    const scale = state.zoom;
    useEffect(() => {
        if (state.textDraft?.editing) textRef.focus();
    }, [state.textDraft?.editing]);
    const checker = useMemo<DrawingCommand[]>(() => {
        const commands: DrawingCommand[] = [
            { kind: "rectangle", x: 0, y: 0, width, height, fill: "#ffffff" }
        ];
        const startX = Math.max(0, Math.floor((viewport.offsetX - left) / 12) * 12);
        const startY = Math.max(0, Math.floor((viewport.offsetY - top) / 12) * 12);
        const endX = Math.min(width, viewport.offsetX - left + viewport.viewportWidth + 12);
        const endY = Math.min(height, viewport.offsetY - top + viewport.viewportHeight + 12);
        for (let y = startY; y < endY; y += 12)
            for (let x = startX; x < endX; x += 12)
                if ((Math.floor(x / 12) + Math.floor(y / 12)) % 2 === 0)
                    commands.push({
                        kind: "rectangle",
                        x,
                        y,
                        width: Math.min(12, width - x),
                        height: Math.min(12, height - y),
                        fill: props.palette.checker
                    });
        return commands;
    }, [
        width,
        height,
        props.palette.checker,
        left,
        top,
        viewport.offsetX,
        viewport.offsetY,
        viewport.viewportWidth,
        viewport.viewportHeight
    ]);
    const layerCommands = (layer: PaintLayer): readonly DrawingCommand[] => {
        if (
            state.effectPreview &&
            state.effectPreview.layerId === layer.id &&
            state.effectPreview.revision === state.revision
        )
            return [state.effectPreview.command];
        if (layer.id !== state.selectedLayerId) return layer.commands;
        let commands = layer.commands;
        if (state.textDraft && state.textDraft.commandIndex >= 0)
            commands = commands.filter((_, index) => index !== state.textDraft!.commandIndex);
        if (state.draft)
            return [
                ...commands,
                commandForDraft(state.draft, state.draftColor, state.draftSize, state.draftFilled)
            ];
        return commands;
    };
    const brush = state.tool === "brush" || state.tool === "eraser";
    const footprint: DrawingCommand[] =
        cursor && brush
            ? [
                  {
                      kind: "ellipse",
                      centerX: cursor.x,
                      centerY: cursor.y,
                      radiusX: (state.size * scale) / 2,
                      radiusY: (state.size * scale) / 2,
                      stroke: "#ffffff",
                      strokeThickness: 3
                  },
                  {
                      kind: "ellipse",
                      centerX: cursor.x,
                      centerY: cursor.y,
                      radiusX: (state.size * scale) / 2,
                      radiusY: (state.size * scale) / 2,
                      stroke: "#172336",
                      strokeThickness: 1
                  }
              ]
            : [];
    return (
        <Border
            key="canvas-workspace"
            background={props.palette.canvas}
            focusable={true}
            capturePointerOnPress={true}
            onPointerDown={navigation.onPointerDown}
            onPointerMove={navigation.onPointerMove}
            onPointerUp={navigation.onPointerUp}
            onPointerCancel={() => {
                navigation.cancel();
                return true;
            }}
            onPointerLeave={() => setCursor(null)}
        >
            <ScrollViewer
                key="canvas-viewport"
                horizontalScrollBarVisibility="auto"
                verticalScrollBarVisibility="auto"
                offsetX={offset.x}
                offsetY={offset.y}
                onScrollChanged={navigation.setViewport}
                onKeyDown={navigation.onKeyDown}
                onKeyUp={navigation.onKeyUp}
                onBlur={navigation.cancel}
                onWheel={(event) => {
                    if (!event.ctrl) return false;
                    navigation.zoomAt(event.deltaY, event.x, event.y);
                    return true;
                }}
            >
                <Canvas width={frameWidth} height={frameHeight}>
                    <Border
                        canvasLeft={left - 1}
                        canvasTop={top - 1}
                        width={width + 2}
                        height={height + 2}
                        borderBrush={props.palette.border}
                        borderThickness={1}
                    >
                        <Grid width={width} height={height}>
                            <DrawingCanvas
                                width={width}
                                height={height}
                                commands={checker}
                                isHitTestVisible={false}
                            />
                            {document.layers.map((layer) => (
                                <DrawingCanvas
                                    key={layer.id}
                                    width={width}
                                    height={height}
                                    coordinateWidth={document.width}
                                    coordinateHeight={document.height}
                                    isVisible={layer.isVisible}
                                    opacity={layer.opacity}
                                    commands={layerCommands(layer)}
                                    isHitTestVisible={false}
                                />
                            ))}
                            <Border
                                key="paint-surface"
                                automationName="Paint surface"
                                background="#00000000"
                                focusable={true}
                                ref={surfaceRef}
                                capturePointerOnPress={true}
                                cursor={space ? "sizeAll" : state.tool === "text" ? "ibeam" : "cross"}
                                onPointerDown={(event) => {
                                    surfaceRef.focus();
                                    if (space || event.button === "middle") return false;
                                    return props.onDown(event);
                                }}
                                onPointerMove={(event) => {
                                    if (space || (event.buttons & 4) !== 0) return false;
                                    setCursor({ x: event.x, y: event.y });
                                    if ((event.buttons & 1) !== 0) return props.onMove(event);
                                    return false;
                                }}
                                onPointerUp={(event) => {
                                    if (space || event.button === "middle") return false;
                                    return props.onUp(event);
                                }}
                                onPointerLeave={() => setCursor(null)}
                                onPointerCancel={() => {
                                    navigation.cancel();
                                    props.dispatch({
                                        type: state.tool === "text" ? "cancelText" : "pointerCancel"
                                    });
                                    return true;
                                }}
                            />
                            <DrawingCanvas
                                width={width}
                                height={height}
                                commands={footprint}
                                isHitTestVisible={false}
                            />
                            <Canvas width={width} height={height} isVisible={state.textDraft !== null}>
                                {state.textDraft === null ? null : state.textDraft.editing ? (
                                    <TextBox
                                        key="text-editor"
                                        appearance="plain"
                                        automationName="Text editor"
                                        ref={textRef}
                                        canvasLeft={state.textDraft.x * scale}
                                        canvasTop={state.textDraft.y * scale}
                                        width={Math.max(1, state.textDraft.width * scale)}
                                        height={Math.max(1, state.textDraft.height * scale)}
                                        text={state.textDraft.text}
                                        acceptsReturn={true}
                                        textWrapping="wrap"
                                        maxLength={65536}
                                        background="#22ffffff"
                                        foreground={state.color}
                                        fontFamily={state.fontFamily}
                                        fontSize={state.textSize * scale}
                                        fontWeight={state.textBold ? "bold" : "normal"}
                                        fontStyle={state.textItalic ? "italic" : "normal"}
                                        onTextChanged={(value) =>
                                            props.dispatch({ type: "textValue", value })
                                        }
                                    />
                                ) : (
                                    <Border
                                        canvasLeft={state.textDraft.x * scale}
                                        canvasTop={state.textDraft.y * scale}
                                        width={Math.max(1, state.textDraft.width * scale)}
                                        height={Math.max(1, state.textDraft.height * scale)}
                                        borderBrush={props.palette.accent}
                                        borderThickness={1}
                                        isHitTestVisible={false}
                                    />
                                )}
                            </Canvas>
                        </Grid>
                    </Border>
                </Canvas>
            </ScrollViewer>
        </Border>
    );
}
