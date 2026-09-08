import {
    Border,
    Button,
    CheckBox,
    ComboBox,
    Grid,
    NumericUpDown,
    StackPanel,
    TextBlock,
    WrapPanel,
    useState
} from "@sharpts/gui";
import { AppAction, AppState, COLORS, FONT_FAMILIES } from "./editor-state";
import { Palette, TextField } from "./controls";
import { validColor } from "./document";

export function ToolOptions(props: {
    state: AppState;
    palette: Palette;
    dispatch: (action: AppAction) => void;
    commitText: () => void;
}): JSX.Element {
    const { state, palette, dispatch } = props;
    const text = state.tool === "text";
    return (
        <WrapPanel spacing={8}>
            <TextBlock foreground={palette.muted} verticalAlignment="center">
                {text ? "Text size" : state.tool === "fill" ? "Tolerance (%)" : "Size (px)"}
            </TextBlock>
            <NumericUpDown
                showButtonSpinner={false}
                key={
                    text
                        ? "text-size-number"
                        : state.tool === "fill"
                          ? "fill-tolerance-number"
                          : "brush-size-number"
                }
                automationName={text ? "Text size" : state.tool === "fill" ? "Fill tolerance" : "Brush size"}
                width={88}
                minimum={text ? 6 : 0}
                maximum={text ? 144 : state.tool === "fill" ? 100 : 64}
                isVisible={state.tool !== "picker"}
                value={
                    text
                        ? state.textSize
                        : state.tool === "fill"
                          ? Math.round(state.fillTolerance * 100)
                          : state.size
                }
                onValueChanged={(value) => {
                    if (value !== null)
                        dispatch(
                            text
                                ? { type: "textSize", value }
                                : state.tool === "fill"
                                  ? { type: "fillTolerance", value: value / 100 }
                                  : { type: "size", size: value }
                        );
                }}
            />
            <CheckBox
                isVisible={state.tool === "rectangle" || state.tool === "ellipse"}
                isChecked={state.filled}
                onCheckedChanged={(filled) => dispatch({ type: "filled", filled })}
            >
                <TextBlock key="filled-label">Filled shapes</TextBlock>
            </CheckBox>
            <ComboBox
                key="font-family"
                automationName="Text font family"
                isVisible={text}
                width={120}
                items={FONT_FAMILIES}
                selectedIndex={FONT_FAMILIES.indexOf(state.fontFamily)}
                onSelectionChanged={(index) => {
                    if (index >= 0) dispatch({ type: "fontFamily", value: FONT_FAMILIES[index] });
                }}
            />
            <CheckBox
                key="text-bold"
                isVisible={text}
                isChecked={state.textBold}
                onCheckedChanged={(value) => dispatch({ type: "textBold", value })}
            >
                Bold
            </CheckBox>
            <CheckBox
                key="text-italic"
                isVisible={text}
                isChecked={state.textItalic}
                onCheckedChanged={(value) => dispatch({ type: "textItalic", value })}
            >
                Italic
            </CheckBox>
            <Button
                key="apply-text"
                isVisible={state.textDraft?.editing === true}
                onClick={props.commitText}
                toolTip="Apply text · Ctrl+Enter"
            >
                Apply text
            </Button>
            <Button
                key="cancel-text"
                isVisible={state.textDraft?.editing === true}
                onClick={() => dispatch({ type: "cancelText" })}
            >
                Cancel
            </Button>
            <TextBlock
                foreground={palette.muted}
                verticalAlignment="center"
                isVisible={!text && state.tool !== "fill"}
            >
                Shift constrains shapes · Space to pan
            </TextBlock>
        </WrapPanel>
    );
}

export function ColorPalette(props: {
    state: AppState;
    palette: Palette;
    dispatch: (action: AppAction) => void;
}): JSX.Element {
    const { state, palette, dispatch } = props;
    const [recent, setRecent] = useState<string[]>([]);
    const choose = (color: string): void => {
        dispatch({ type: "color", color });
        setRecent([color, ...recent.filter((value) => value !== color)].slice(0, 6));
    };
    const alpha = state.color.length === 9 ? Number.parseInt(state.color.slice(1, 3), 16) : 255;
    const rgb = state.color.length === 9 ? state.color.slice(3) : state.color.slice(1);
    return (
        <Grid columns="*,156,96" rows="auto">
            <StackPanel spacing={5} verticalAlignment="center">
                <WrapPanel spacing={5}>
                    {COLORS.map((color) => (
                        <Button
                            key={"swatch-" + color}
                            width={24}
                            height={24}
                            padding={0}
                            background={color}
                            automationName={"Color " + color}
                            toolTip={color.toUpperCase()}
                            onClick={() => choose(color)}
                        >
                            <Border
                                width={20}
                                height={20}
                                background={color}
                                borderBrush={state.color === color ? palette.accent : "#75849a"}
                                borderThickness={state.color === color ? 3 : 1}
                            />
                        </Button>
                    ))}
                </WrapPanel>
                <WrapPanel spacing={5} isVisible={recent.length > 0}>
                    <TextBlock fontSize={11} foreground={palette.muted}>
                        Recent
                    </TextBlock>
                    {recent.map((color) => (
                        <Button
                            key={"recent-" + color}
                            width={18}
                            height={18}
                            padding={0}
                            background={color}
                            automationName={"Recent color " + color}
                            toolTip={color.toUpperCase()}
                            onClick={() => choose(color)}
                        >
                            <Border
                                width={16}
                                height={16}
                                background={color}
                                borderBrush="#75849a"
                                borderThickness={1}
                            />
                        </Button>
                    ))}
                </WrapPanel>
            </StackPanel>
            <Border gridColumn={1} margin={[10, 0, 8, 0] as const}>
                <TextField
                    id="custom-color"
                    label="Hex (RGB / ARGB)"
                    maxLength={9}
                    value={state.color.toUpperCase()}
                    palette={palette}
                    validate={(value) => (validColor(value) ? null : "Use six RGB or eight ARGB hex digits.")}
                    onCommit={choose}
                />
            </Border>
            <StackPanel gridColumn={2} spacing={4}>
                <TextBlock fontSize={12} foreground={palette.muted}>
                    Alpha (%)
                </TextBlock>
                <NumericUpDown
                    showButtonSpinner={false}
                    key="color-alpha"
                    automationName="Color alpha"
                    minimum={0}
                    maximum={100}
                    value={Math.round((alpha / 255) * 100)}
                    onValueChanged={(value) => {
                        if (value !== null)
                            choose(
                                "#" +
                                    Math.round((value / 100) * 255)
                                        .toString(16)
                                        .padStart(2, "0") +
                                    rgb
                            );
                    }}
                />
            </StackPanel>
        </Grid>
    );
}
