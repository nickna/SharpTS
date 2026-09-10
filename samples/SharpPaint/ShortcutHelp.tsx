import { Button, Inspector, StackPanel, TextBlock } from "@sharpts/gui";

export function ShortcutHelp(props: { close: () => void }): JSX.Element {
    return (
        <Inspector
            title="Make it your canvas"
            actions={
                <Button isDefault={true} isCancel={true} onClick={props.close}>
                    Done
                </Button>
            }
        >
            <StackPanel spacing={16}>
                <TextBlock textWrapping="wrap">
                    Brush B · Eraser E · Line L · Rectangle R · Ellipse O · Fill F · Picker I · Text T
                </TextBlock>
                <TextBlock textWrapping="wrap">
                    {
                        "Ctrl+N / O / S — New, open, save\nCtrl+Shift+S — Save As\nCtrl+E — Export PNG\nCtrl+Z / Y — Undo / redo"
                    }
                </TextBlock>
                <TextBlock textWrapping="wrap">
                    {
                        "Ctrl+wheel — Zoom around the pointer\nSpace-drag / middle-drag — Pan\nShift — Constrain shapes\nX — Swap foreground and background"
                    }
                </TextBlock>
                <TextBlock textWrapping="wrap">
                    {
                        "Ctrl+Enter — Apply text or an effect\nEscape — Cancel the current edit\nArrow keys — Select a layer\nAlt+Up / Down — Reorder the selected layer"
                    }
                </TextBlock>
                <TextBlock textWrapping="wrap">
                    Open Demo artwork from File to explore editable text, transparency, and layered
                    illustration. Switching tools keeps your text edits.
                </TextBlock>
            </StackPanel>
        </Inspector>
    );
}
