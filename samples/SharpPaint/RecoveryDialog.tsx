import { Button, Inspector, StackPanel, TextBlock, useDesktopPalette } from "@sharpts/gui";
import type { DialogContext } from "@sharpts/gui";

export interface RecoveryCopy {
    path: string;
    name: string;
    savedAt: number;
}

export function RecoveryDialog(props: {
    copies: RecoveryCopy[];
    dialog: DialogContext<string>;
}): JSX.Element {
    const palette = useDesktopPalette();
    return (
        <Inspector
            title="Recover your work"
            actions={
                <StackPanel orientation="horizontal" spacing={8} horizontalAlignment="right">
                    <Button key="browse-recovery" onClick={() => props.dialog.complete("")}>
                        Browse…
                    </Button>
                    <Button isCancel={true} onClick={props.dialog.cancel}>
                        Cancel
                    </Button>
                </StackPanel>
            }
        >
            <StackPanel spacing={12}>
                <TextBlock foreground={palette.muted} textWrapping="wrap">
                    Open a recovery copy, then Save As to keep it. The original copy remains available.
                </TextBlock>
                {props.copies.map((copy, index) => (
                    <Button
                        key={"recovery-copy-" + index}
                        horizontalContentAlignment="stretch"
                        onClick={() => props.dialog.complete(copy.path)}
                    >
                        <StackPanel spacing={3}>
                            <TextBlock fontWeight="semibold" textWrapping="wrap">
                                {copy.name}
                            </TextBlock>
                            <TextBlock fontSize={12} opacity={0.75}>
                                {new Date(copy.savedAt).toLocaleString()}
                            </TextBlock>
                        </StackPanel>
                    </Button>
                ))}
            </StackPanel>
        </Inspector>
    );
}
