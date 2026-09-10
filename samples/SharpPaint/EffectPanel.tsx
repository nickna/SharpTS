import { Button, CheckBox, Grid, Inspector, StackPanel, TextBlock } from "@sharpts/gui";

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

    const blur = dialog.kind === "gaussianBlur",
        hue = dialog.kind === "hueSaturation";

    return (
        <Inspector
            title={effectTitle(dialog.kind)}
            actions={
                <Grid columns="*,8,*" rows="32">
                    <Button key="cancel-effect" onClick={props.cancel}>
                        Cancel
                    </Button>

                    <Button
                        key="apply-effect"
                        gridColumn={2}
                        classes={["primary"]}
                        isEnabled={state.busy === null}
                        onClick={props.apply}
                    >
                        Apply
                    </Button>
                </Grid>
            }
        >
            <StackPanel key="effect-dialog" spacing={16}>
                <TextBlock foreground={palette.muted} textWrapping="wrap" fontSize={12}>
                    Preview on the selected layer. Undo restores the original.
                </TextBlock>

                <NumberField
                    id="effect-first"
                    label={blur ? "Radius (px)" : hue ? "Hue (°)" : "Brightness (%)"}
                    value={blur || hue ? dialog.first : Math.round(dialog.first * 100)}
                    minimum={blur ? 0 : hue ? -180 : -100}
                    maximum={blur ? 64 : hue ? 180 : 100}
                    step={1}
                    onChange={(first) =>
                        dispatch({ type: "effectParameter", first: blur || hue ? first : first / 100 })
                    }
                />

                {blur ? null : (
                    <NumberField
                        id="effect-second"
                        label={hue ? "Saturation (%)" : "Contrast (%)"}
                        value={Math.round(dialog.second * 100)}
                        minimum={-100}
                        maximum={100}
                        step={1}
                        onChange={(second) => dispatch({ type: "effectParameter", second: second / 100 })}
                    />
                )}

                <CheckBox key="effect-before" isChecked={props.before} onCheckedChanged={props.setBefore}>
                    Show original
                </CheckBox>

                <TextBlock foreground={palette.muted} fontSize={12}>
                    Ctrl+Enter to apply · Esc to cancel
                </TextBlock>
            </StackPanel>
        </Inspector>
    );
}
