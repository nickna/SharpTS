import { Button, CheckBox, ScrollViewer, StackPanel, TextBlock } from "@sharpts/gui";
import { AppAction, AppState, effectTitle } from "./editor-state";
import { NumberField, Palette } from "./controls";

export function EffectPanel(props: {
    state: AppState;
    palette: Palette;
    dispatch: (action: AppAction) => void;
    before: boolean;
    setBefore: (value: boolean) => void;
    apply: () => void;
    cancel: () => void;
}): JSX.Element {
    const { state, palette, dispatch } = props;
    const dialog = state.effectDialog!;
    const blur = dialog.kind === "gaussianBlur";
    const hue = dialog.kind === "hueSaturation";
    return (
        <ScrollViewer verticalScrollBarVisibility="auto" horizontalScrollBarVisibility="disabled">
            <StackPanel key="effect-dialog" spacing={14} margin={14}>
                <TextBlock fontSize={16} fontWeight="semibold" foreground={palette.text}>
                    {effectTitle(dialog.kind)}
                </TextBlock>
                <TextBlock foreground={palette.muted} textWrapping="wrap">
                    Preview updates as you edit. Apply replaces the selected layer with the rendered result;
                    Undo restores its editable commands.
                </TextBlock>
                <NumberField
                    id="effect-first"
                    label={blur ? "Radius (px)" : hue ? "Hue (degrees)" : "Brightness"}
                    value={dialog.first}
                    minimum={blur ? 0 : hue ? -180 : -1}
                    maximum={blur ? 64 : hue ? 180 : 1}
                    step={blur || hue ? 1 : 0.05}
                    palette={palette}
                    onChange={(first) => dispatch({ type: "effectParameter", first })}
                />
                {blur ? null : (
                    <NumberField
                        id="effect-second"
                        label={hue ? "Saturation" : "Contrast"}
                        value={dialog.second}
                        minimum={-1}
                        maximum={1}
                        step={0.05}
                        palette={palette}
                        onChange={(second) => dispatch({ type: "effectParameter", second })}
                    />
                )}
                <CheckBox key="effect-before" isChecked={props.before} onCheckedChanged={props.setBefore}>
                    Show original
                </CheckBox>
                <Button key="apply-effect" isEnabled={state.busy === null} onClick={props.apply}>
                    Apply effect
                </Button>
                <Button key="cancel-effect" onClick={props.cancel}>
                    Cancel
                </Button>
            </StackPanel>
        </ScrollViewer>
    );
}
