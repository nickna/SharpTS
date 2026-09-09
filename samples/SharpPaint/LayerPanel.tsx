import {
    Border,
    Button,
    CheckBox,
    DrawingCanvas,
    Grid,
    StackPanel,
    TextBlock,
    VirtualizingList,
    useRef,
    useState
} from "@sharpts/gui";
import type { GuiElement } from "@sharpts/gui";
import { AppAction, AppState } from "./editor-state";
import { IconButton, NumberField, Palette, TextField } from "./controls";

export function LayerPanel(props: {
    state: AppState;
    palette: Palette;
    dispatch: (action: AppAction) => void;
    onMerge: () => void;
}): GuiElement {
    const { state, palette, dispatch } = props;
    const document = state.history.document;
    const index = document.layers.findIndex((layer) => layer.id === state.selectedLayerId);
    const selected = document.layers[Math.max(0, index)];
    const layers = document.layers.slice().reverse();
    const [details, setDetails] = useState<boolean>(false);
    const propertiesVisible = state.windowHeight >= 600 || details;
    const drag = useRef<{ y: number; index: number; id: string } | null>(null);
    const thumbScale = Math.min(38 / document.width, 30 / document.height);
    return (
        <Border
            key="layers-panel"
            isEnabled={state.busy === null}
            background={palette.panel}
            borderBrush={palette.border}
            borderThickness={[1, 0, 0, 0] as const}
        >
            <Grid columns="*" rows="40,38,*,auto">
                <Grid columns="*,auto" rows="auto" margin={[4, 10] as const}>
                    <TextBlock
                        fontSize={14}
                        fontWeight="semibold"
                        foreground={palette.text}
                        verticalAlignment="center"
                    >
                        Layers
                    </TextBlock>
                    <Button
                        gridColumn={1}
                        isVisible={state.windowWidth < 900}
                        classes={["subtle"]}
                        automationName="Close layers"
                        onClick={() => dispatch({ type: "toggleLayers", value: false })}
                    >
                        Close
                    </Button>
                </Grid>
                <StackPanel gridRow={1} orientation="horizontal" spacing={4} margin={[0, 8] as const}>
                    <IconButton
                        id="add-layer"
                        label="Add layer"
                        icon="add"
                        enabled={document.layers.length < 64}
                        onClick={() => dispatch({ type: "addLayer" })}
                    />
                    <IconButton
                        id="duplicate-layer"
                        label="Duplicate layer"
                        icon="duplicate"
                        enabled={document.layers.length < 64}
                        onClick={() => dispatch({ type: "duplicateLayer" })}
                    />
                    <IconButton
                        id="delete-layer"
                        label="Delete layer"
                        icon="remove"
                        enabled={document.layers.length > 1}
                        onClick={() => dispatch({ type: "deleteLayer" })}
                    />
                    <IconButton
                        id="raise-layer"
                        label="Raise layer · Alt+Up"
                        icon="up"
                        enabled={index < document.layers.length - 1}
                        onClick={() => dispatch({ type: "moveLayer", direction: "up" })}
                    />
                    <IconButton
                        id="lower-layer"
                        label="Lower layer · Alt+Down"
                        icon="down"
                        enabled={index > 0}
                        onClick={() => dispatch({ type: "moveLayer", direction: "down" })}
                    />
                    <IconButton
                        id="merge-layer"
                        label="Merge down"
                        icon="merge"
                        enabled={index > 0}
                        onClick={props.onMerge}
                    />
                </StackPanel>
                <VirtualizingList
                    key="layer-list"
                    focusable={true}
                    gridRow={2}
                    automationName="Layers"
                    selectedIndices={[layers.findIndex((layer) => layer.id === selected.id)]}
                    onSelectionChanged={(indices) => {
                        if (indices.length) dispatch({ type: "selectLayer", layerId: layers[indices[0]].id });
                    }}
                    onKeyDown={(event) => {
                        if (event.alt && (event.key === "Up" || event.key === "Down")) {
                            dispatch({ type: "moveLayer", direction: event.key === "Up" ? "up" : "down" });
                            return true;
                        }
                        return false;
                    }}
                >
                    {layers.map((layer) => (
                        <Grid key={layer.id + "-row"} columns="26,46,*" rows="48" margin={2}>
                            <CheckBox
                                key={layer.id + "-visible"}
                                automationName={"Show " + layer.name}
                                toolTip={layer.isVisible ? "Hide layer" : "Show layer"}
                                isChecked={layer.isVisible}
                                onCheckedChanged={(value) =>
                                    dispatch({ type: "visibility", layerId: layer.id, value })
                                }
                            />
                            <Border
                                gridColumn={1}
                                width={40}
                                height={32}
                                verticalAlignment="center"
                                background="#ffffff"
                                borderBrush={palette.border}
                                borderThickness={1}
                                capturePointerOnPress={true}
                                cursor="sizeAll"
                                toolTip="Drag thumbnail to reorder · Alt+Up / Alt+Down"
                                onPointerDown={(event) => {
                                    if (event.button !== "left") return false;
                                    drag.current = {
                                        y: event.y,
                                        index: document.layers.findIndex((item) => item.id === layer.id),
                                        id: layer.id
                                    };
                                    dispatch({ type: "selectLayer", layerId: layer.id });
                                    return true;
                                }}
                                onPointerUp={(event) => {
                                    const start = drag.current;
                                    drag.current = null;
                                    if (start)
                                        dispatch({
                                            type: "reorderLayer",
                                            layerId: start.id,
                                            index: start.index - Math.round((event.y - start.y) / 52)
                                        });
                                    return true;
                                }}
                                onPointerCancel={() => {
                                    drag.current = null;
                                    return true;
                                }}
                            >
                                <Grid>
                                    <DrawingCanvas
                                        width={38}
                                        height={30}
                                        isHitTestVisible={false}
                                        commands={[
                                            {
                                                kind: "rectangle",
                                                x: 0,
                                                y: 0,
                                                width: 19,
                                                height: 15,
                                                fill: palette.checker
                                            },
                                            {
                                                kind: "rectangle",
                                                x: 19,
                                                y: 15,
                                                width: 19,
                                                height: 15,
                                                fill: palette.checker
                                            }
                                        ]}
                                    />
                                    <DrawingCanvas
                                        width={document.width * thumbScale}
                                        height={document.height * thumbScale}
                                        horizontalAlignment="center"
                                        verticalAlignment="center"
                                        coordinateWidth={document.width}
                                        coordinateHeight={document.height}
                                        commands={layer.commands}
                                        opacity={layer.opacity}
                                        isHitTestVisible={false}
                                    />
                                </Grid>
                            </Border>
                            <StackPanel
                                gridColumn={2}
                                spacing={2}
                                verticalAlignment="center"
                                margin={[0, 6] as const}
                            >
                                <TextBlock toolTip={layer.name}>
                                    {layer.name.length > 20 ? layer.name.slice(0, 19) + "…" : layer.name}
                                </TextBlock>
                                <TextBlock fontSize={11} opacity={0.75}>
                                    {(layer.isVisible ? "" : "Hidden · ") +
                                        Math.round(layer.opacity * 100) +
                                        "%"}
                                </TextBlock>
                            </StackPanel>
                        </Grid>
                    ))}
                </VirtualizingList>
                <Border
                    gridRow={3}
                    padding={10}
                    borderBrush={palette.border}
                    borderThickness={[0, 1, 0, 0] as const}
                >
                    <StackPanel spacing={8}>
                        <Button
                            key="layer-properties"
                            isVisible={state.windowHeight < 600}
                            classes={["subtle"]}
                            onClick={() => setDetails(!details)}
                        >
                            {details ? "Hide layer properties" : "Layer properties…"}
                        </Button>
                        <StackPanel spacing={8} isVisible={propertiesVisible}>
                            <TextField
                                key={selected.id + "-name-field"}
                                id="layer-name"
                                label="Layer name"
                                value={selected.name}
                                validate={(value) => (value.trim() === "" ? "Give this layer a name." : null)}
                                onCommit={(name) =>
                                    dispatch({ type: "renameLayer", layerId: selected.id, name })
                                }
                            />
                            <NumberField
                                id="layer-opacity"
                                label="Opacity (%)"
                                value={Math.round(selected.opacity * 100)}
                                minimum={0}
                                maximum={100}
                                onBegin={() => dispatch({ type: "beginOpacity" })}
                                onEnd={() => dispatch({ type: "endOpacity" })}
                                onChange={(value) => dispatch({ type: "opacity", value: value / 100 })}
                            />
                        </StackPanel>
                        <TextBlock key="command-count" isVisible={false}>
                            {document.layers.reduce((sum, layer) => sum + layer.commands.length, 0) +
                                " commands · " +
                                document.layers.length +
                                " layers"}
                        </TextBlock>
                    </StackPanel>
                </Border>
            </Grid>
        </Border>
    );
}
