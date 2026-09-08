import {
    Border,
    Button,
    Grid,
    NumericUpDown,
    StackPanel,
    TextBlock,
    useControlRef,
    useEffect,
    useState
} from "@sharpts/gui";
import type { DialogContext, GuiElement } from "@sharpts/gui";

export interface DocumentSize {
    width: number;
    height: number;
}
export function NewDocumentDialog(props: { dialog: DialogContext<DocumentSize> }): GuiElement {
    const [width, setWidth] = useState<number | null>(1024);
    const [height, setHeight] = useState<number | null>(768);
    const focus = useControlRef<unknown>();
    useEffect(() => {
        focus.focus();
    }, []);
    const valid =
        width !== null &&
        height !== null &&
        width >= 1 &&
        width <= 8192 &&
        height >= 1 &&
        height <= 8192 &&
        Number.isInteger(width) &&
        Number.isInteger(height);

    return (
        <Border padding={24}>
            <StackPanel spacing={16}>
                <TextBlock fontSize={22} fontWeight="semibold">
                    New document
                </TextBlock>
                <TextBlock textWrapping="wrap">Choose the canvas size in pixels.</TextBlock>
                <Grid columns="92,*" rows="auto,auto">
                    <TextBlock verticalAlignment="center">Width</TextBlock>
                    <NumericUpDown
                        key="new-width"
                        ref={focus}
                        gridColumn={1}
                        automationName="Document width"
                        minimum={1}
                        maximum={8192}
                        increment={1}
                        value={width}
                        onValueChanged={setWidth}
                    />
                    <TextBlock gridRow={1} verticalAlignment="center">
                        Height
                    </TextBlock>
                    <NumericUpDown
                        key="new-height"
                        gridRow={1}
                        gridColumn={1}
                        automationName="Document height"
                        minimum={1}
                        maximum={8192}
                        increment={1}
                        value={height}
                        onValueChanged={setHeight}
                    />
                </Grid>
                <TextBlock isVisible={!valid} textWrapping="wrap">
                    Enter whole numbers from 1 to 8192.
                </TextBlock>
                <StackPanel orientation="horizontal" spacing={8} horizontalAlignment="right">
                    <Button key="cancel-new" isCancel={true} onClick={props.dialog.cancel}>
                        Cancel
                    </Button>
                    <Button
                        key="create-new"
                        automationName="Create document"
                        isDefault={true}
                        isEnabled={valid}
                        onClick={() => {
                            if (valid) props.dialog.complete({ width: width!, height: height! });
                        }}
                    >
                        Create
                    </Button>
                </StackPanel>
            </StackPanel>
        </Border>
    );
}
