import {
    Border,
    Button,
    CheckBox,
    DrawingCanvas,
    Grid,
    ToggleButton,
    ScrollViewer,
    StackPanel,
    TextBlock
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
    return (
        <Border
            key="layers-panel"
            background={palette.panel}
            borderBrush={palette.border}
            borderThickness={[1, 0, 0, 0] as const}
            padding={12}
        >
            <Grid columns="*" rows="auto,auto,*,1.25*">
                <Grid columns="*,auto" rows="auto">
                    <TextBlock fontSize={14} fontWeight="semibold" foreground={palette.text}>
                        Layers
                    </TextBlock>
                    <Button
                        gridColumn={1}
                        isVisible={state.windowWidth < 900}
                        automationName="Close layers"
                        onClick={() => dispatch({ type: "toggleLayers", value: false })}
                    >
                        Close
                    </Button>
                </Grid>
                <StackPanel gridRow={1} orientation="horizontal" spacing={5} margin={[0, 10, 0, 10] as const}>
                    <IconButton
                        id="add-layer"
                        label="Add layer"
                        icon="add"
                        palette={palette}
                        enabled={document.layers.length < 64}
                        onClick={() => dispatch({ type: "addLayer" })}
                    />
                    <IconButton
                        id="duplicate-layer"
                        label="Duplicate layer"
                        icon="duplicate"
                        palette={palette}
                        enabled={document.layers.length < 64}
                        onClick={() => dispatch({ type: "duplicateLayer" })}
                    />
                    <IconButton
                        id="delete-layer"
                        label="Delete layer"
                        icon="remove"
                        palette={palette}
                        enabled={document.layers.length > 1}
                        onClick={() => dispatch({ type: "deleteLayer" })}
                    />
                    <IconButton
                        id="raise-layer"
                        label="Raise layer"
                        icon="up"
                        palette={palette}
                        enabled={index < document.layers.length - 1}
                        onClick={() => dispatch({ type: "moveLayer", direction: "up" })}
                    />
                    <IconButton
                        id="lower-layer"
                        label="Lower layer"
                        icon="down"
                        palette={palette}
                        enabled={index > 0}
                        onClick={() => dispatch({ type: "moveLayer", direction: "down" })}
                    />
                </StackPanel>
                <ScrollViewer
                    gridRow={2}
                    verticalScrollBarVisibility="auto"
                    horizontalScrollBarVisibility="disabled"
                >
                    <StackPanel spacing={6}>
                        {document.layers
                            .slice()
                            .reverse()
                            .map((layer) => (
                                <Border
                                    key={layer.id + "-row"}
                                    background={layer.id === selected.id ? palette.selected : palette.surface}
                                    cornerRadius={5}
                                    padding={4}
                                >
                                    <Grid columns="26,*" rows="auto">
                                        <CheckBox
                                            key={layer.id + "-visible"}
                                            automationName={"Show " + layer.name}
                                            toolTip={layer.isVisible ? "Hide layer" : "Show layer"}
                                            isChecked={layer.isVisible}
                                            onCheckedChanged={(value) =>
                                                dispatch({ type: "visibility", layerId: layer.id, value })
                                            }
                                        />
                                        <ToggleButton
                                            key={layer.id + "-select"}
                                            gridColumn={1}
                                            automationName={"Select " + layer.name}
                                            isChecked={layer.id === selected.id}
                                            onCheckedChanged={(value) => {
                                                dispatch({ type: "selectLayer", layerId: layer.id });
                                            }}
                                            horizontalContentAlignment="stretch"
                                        >
                                            <Grid columns="44,*" rows="auto">
                                                <Border
                                                    width={38}
                                                    height={30}
                                                    background="#ffffff"
                                                    borderBrush={palette.border}
                                                    borderThickness={1}
                                                >
                                                    <DrawingCanvas
                                                        width={36}
                                                        height={28}
                                                        coordinateWidth={document.width}
                                                        coordinateHeight={document.height}
                                                        commands={layer.commands}
                                                        isHitTestVisible={false}
                                                    />
                                                </Border>
                                                <StackPanel
                                                    gridColumn={1}
                                                    spacing={2}
                                                    margin={[6, 0, 0, 0] as const}
                                                >
                                                    <TextBlock
                                                        foreground={
                                                            layer.id === selected.id
                                                                ? "#ffffff"
                                                                : palette.text
                                                        }
                                                        textWrapping="wrap"
                                                    >
                                                        {layer.name}
                                                    </TextBlock>
                                                    <TextBlock
                                                        fontSize={11}
                                                        foreground={
                                                            layer.id === selected.id
                                                                ? "#ffffff"
                                                                : palette.muted
                                                        }
                                                    >
                                                        {(layer.isVisible ? "" : "Hidden · ") +
                                                            Math.round(layer.opacity * 100) +
                                                            "%"}
                                                    </TextBlock>
                                                </StackPanel>
                                            </Grid>
                                        </ToggleButton>
                                    </Grid>
                                </Border>
                            ))}
                    </StackPanel>
                </ScrollViewer>
                <ScrollViewer
                    gridRow={3}
                    verticalScrollBarVisibility="auto"
                    horizontalScrollBarVisibility="disabled"
                >
                    <StackPanel spacing={8} margin={[0, 6, 0, 0] as const}>
                        <TextField
                            id="layer-name"
                            label="Layer name"
                            value={selected.name}
                            palette={palette}
                            validate={(value) => (value.trim() === "" ? "Give this layer a name." : null)}
                            onCommit={(name) => dispatch({ type: "renameLayer", name })}
                        />
                        <NumberField
                            id="layer-opacity"
                            label="Opacity (%)"
                            value={Math.round(selected.opacity * 100)}
                            minimum={0}
                            maximum={100}
                            palette={palette}
                            onBegin={() => dispatch({ type: "beginOpacity" })}
                            onEnd={() => dispatch({ type: "endOpacity" })}
                            onChange={(value) => dispatch({ type: "opacity", value: value / 100 })}
                        />
                        <Button
                            key="merge-layer"
                            isEnabled={index > 0}
                            toolTip="Combine this layer with the layer below"
                            onClick={props.onMerge}
                        >
                            Merge down
                        </Button>
                        <TextBlock key="command-count" isVisible={false}>
                            {document.layers.reduce((sum, layer) => sum + layer.commands.length, 0) +
                                " commands · " +
                                document.layers.length +
                                " layers"}
                        </TextBlock>
                    </StackPanel>
                </ScrollViewer>
            </Grid>
        </Border>
    );
}
