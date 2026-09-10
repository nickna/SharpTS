import {
    Border,
    Button,
    CheckBox,
    ColorPicker,
    ComboBox,
    Grid,
    NumericUpDown,
    StackPanel,
    TextBlock,
    TextBox,
    ToggleButton,
    WrapPanel,
    normalizeHexColor,
    useTextDraft
} from "@sharpts/gui";
import { AppAction, AppState, COLORS, FONT_FAMILIES, toolLabel } from "./editor-state";
import { Palette } from "./controls";
import { validColor } from "./document";

export function ToolOptions(props: {
    state: AppState;
    palette: Palette;
    dispatch: (action: AppAction) => void;
    commitText: () => void;
}): JSX.Element {
    const { state, palette, dispatch } = props;
    const text = state.tool === "text",
        fill = state.tool === "fill",
        picker = state.tool === "picker";
    return (
        <WrapPanel spacing={8}>
            <TextBlock foreground={palette.text} fontWeight="semibold" verticalAlignment="center">
                {toolLabel(state.tool)}
            </TextBlock>
            {picker ? (
                <TextBlock foreground={palette.muted} verticalAlignment="center">
                    Click the artwork to sample its visible color
                </TextBlock>
            ) : (
                <>
                    <TextBlock foreground={palette.muted} verticalAlignment="center">
                        {text ? "Size" : fill ? "Tolerance" : "Size"}
                    </TextBlock>
                    <NumericUpDown
                        key={text ? "text-size-number" : fill ? "fill-tolerance-number" : "brush-size-number"}
                        automationName={text ? "Text size" : fill ? "Fill tolerance" : "Brush size"}
                        showButtonSpinner={false}
                        formatString="0"
                        width={60}
                        height={30}
                        minimum={text ? 6 : fill ? 0 : 1}
                        maximum={text ? 144 : fill ? 100 : 64}
                        value={
                            text ? state.textSize : fill ? Math.round(state.fillTolerance * 100) : state.size
                        }
                        onValueChanged={(value) => {
                            if (value !== null)
                                dispatch(
                                    text
                                        ? { type: "textSize", value }
                                        : fill
                                          ? { type: "fillTolerance", value: value / 100 }
                                          : { type: "size", size: value }
                                );
                        }}
                    />
                    <TextBlock foreground={palette.muted} verticalAlignment="center">
                        {fill ? "%" : "px"}
                    </TextBlock>
                </>
            )}
            <CheckBox
                isVisible={state.tool === "rectangle" || state.tool === "ellipse"}
                isChecked={state.filled}
                onCheckedChanged={(filled) => dispatch({ type: "filled", filled })}
            >
                <TextBlock key="filled-label">Fill</TextBlock>
            </CheckBox>
            <ComboBox
                key="font-family"
                automationName="Text font family"
                isVisible={text}
                width={130}
                items={FONT_FAMILIES}
                selectedIndex={FONT_FAMILIES.indexOf(state.fontFamily)}
                onSelectionChanged={(index) => {
                    if (index >= 0) dispatch({ type: "fontFamily", value: FONT_FAMILIES[index] });
                }}
            />
            <ToggleButton
                key="text-bold"
                automationName="Bold"
                toolTip="Bold"
                width={30}
                fontWeight="bold"
                isVisible={text}
                isChecked={state.textBold}
                onCheckedChanged={(value) => dispatch({ type: "textBold", value })}
            >
                B
            </ToggleButton>
            <ToggleButton
                key="text-italic"
                automationName="Italic"
                toolTip="Italic"
                width={30}
                fontStyle="italic"
                isVisible={text}
                isChecked={state.textItalic}
                onCheckedChanged={(value) => dispatch({ type: "textItalic", value })}
            >
                I
            </ToggleButton>
            <Button
                key="apply-text"
                classes={["primary"]}
                isVisible={state.textDraft?.editing === true}
                onClick={props.commitText}
                toolTip="Apply text · Ctrl+Enter"
            >
                Apply
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
                fontSize={12}
                isVisible={state.windowWidth >= 1000 && !text && !picker}
            >
                {state.tool === "line" || state.tool === "rectangle" || state.tool === "ellipse"
                    ? "Shift to constrain · Space to pan"
                    : "Space to pan · Ctrl+wheel to zoom"}
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
    const choose = (color: string): void => dispatch({ type: "color", color: normalizeHexColor(color) });
    const edit = useTextDraft(state.color.toUpperCase(), choose, (value) =>
        validColor(value) ? null : "Use #RRGGBB or #AARRGGBB."
    );
    const alpha = state.color.length === 9 ? Number.parseInt(state.color.slice(1, 3), 16) : 255;
    const rgb = state.color.length === 9 ? state.color.slice(3) : state.color.slice(1);
    return (
        <Grid columns="64,6,64,30,*,132,8,64" rows="auto,auto">
            <ColorPicker
                key="foreground-color"
                automationName="Foreground color"
                toolTip="Foreground color"
                color={state.color}
                onColorChanged={choose}
                width={64}
                height={32}
                verticalAlignment="center"
            />
            <ColorPicker
                key="background-color"
                gridColumn={2}
                automationName="Background color"
                toolTip="Background color"
                color={state.backgroundColor}
                onColorChanged={(color) =>
                    dispatch({ type: "backgroundColor", color: normalizeHexColor(color) })
                }
                width={64}
                height={32}
                verticalAlignment="center"
            />
            <Button
                gridColumn={3}
                key="swap-colors"
                classes={["subtle"]}
                automationName="Swap foreground and background"
                toolTip="Swap colors · X"
                onClick={() => dispatch({ type: "swapColors" })}
            >
                ⇄
            </Button>
            <StackPanel gridColumn={4} spacing={3} verticalAlignment="center" margin={[8, 0] as const}>
                <WrapPanel spacing={3}>
                    {(state.windowWidth < 900 ? COLORS.slice(0, 8) : COLORS).map((color) => (
                        <Button
                            key={"swatch-" + color}
                            width={22}
                            height={22}
                            minHeight={22}
                            padding={0}
                            automationName={"Color " + color}
                            toolTip={color.toUpperCase()}
                            onClick={() => choose(color)}
                        >
                            <Border
                                width={20}
                                height={20}
                                background={color}
                                borderBrush={state.color === color ? palette.accent : palette.border}
                                borderThickness={state.color === color ? 3 : 1}
                                cornerRadius={3}
                            />
                        </Button>
                    ))}
                </WrapPanel>
                <StackPanel orientation="horizontal" spacing={4} isVisible={state.recentColors.length > 0}>
                    <TextBlock fontSize={11} foreground={palette.muted} verticalAlignment="center">
                        Recent
                    </TextBlock>
                    {state.recentColors.map((color) => (
                        <Button
                            key={"recent-" + color}
                            width={18}
                            height={18}
                            minHeight={18}
                            padding={0}
                            automationName={"Recent color " + color}
                            toolTip={color.toUpperCase()}
                            onClick={() => choose(color)}
                        >
                            <Border
                                width={16}
                                height={16}
                                background={color}
                                borderBrush={palette.border}
                                borderThickness={1}
                                cornerRadius={2}
                            />
                        </Button>
                    ))}
                </StackPanel>
            </StackPanel>
            <StackPanel gridColumn={5} spacing={2}>
                <TextBlock fontSize={11} foreground={palette.muted}>
                    Hex color
                </TextBlock>
                <TextBox
                    key="custom-color"
                    automationName="Hex color RGB or ARGB"
                    text={edit.draft}
                    maxLength={9}
                    onTextChanged={edit.change}
                    onBlur={() => {
                        edit.commit();
                    }}
                    onKeyDown={(event) => {
                        if (event.key === "Enter") {
                            edit.commit();
                            return true;
                        }
                        if (event.key === "Escape") {
                            edit.cancel();
                            return true;
                        }
                        return false;
                    }}
                />
            </StackPanel>
            <StackPanel gridColumn={7} spacing={2}>
                <TextBlock fontSize={11} foreground={palette.muted}>
                    Alpha %
                </TextBlock>
                <NumericUpDown
                    key="color-alpha"
                    automationName="Color alpha"
                    showButtonSpinner={false}
                    formatString="0"
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
            <TextBlock
                gridRow={1}
                gridColumn={5}
                gridColumnSpan={3}
                key="custom-color-error"
                isVisible={edit.error !== ""}
                fontSize={11}
                foreground={palette.danger}
                textWrapping="wrap"
            >
                {edit.error}
            </TextBlock>
        </Grid>
    );
}
