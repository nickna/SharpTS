import {
    Border,
    ComboBox,
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
    const [preset, setPreset] = useState<number>(1);
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
                <TextBlock textWrapping="wrap">Choose a preset or enter a canvas size in pixels.</TextBlock>
                <ComboBox
                    key="new-preset"
                    horizontalAlignment="stretch"
                    selectedIndex={preset}
                    automationName="Canvas preset"
                    items={[
                        "Custom",
                        "Screen · 1024 × 768",
                        "Square · 1080 × 1080",
                        "Landscape · 1920 × 1080",
                        "Portrait · 1080 × 1920"
                    ]}
                    onSelectionChanged={(index) => {
                        setPreset(index);
                        const sizes = [
                            [1024, 768],
                            [1024, 768],
                            [1080, 1080],
                            [1920, 1080],
                            [1080, 1920]
                        ];
                        if (index > 0) {
                            setWidth(sizes[index][0]);
                            setHeight(sizes[index][1]);
                        }
                    }}
                />
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
                        formatString="0"
                        value={width}
                        onValueChanged={(value) => {
                            setWidth(value);
                            setPreset(0);
                        }}
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
                        formatString="0"
                        value={height}
                        onValueChanged={(value) => {
                            setHeight(value);
                            setPreset(0);
                        }}
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
                        classes={["primary"]}
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
