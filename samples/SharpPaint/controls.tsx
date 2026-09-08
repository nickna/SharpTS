import {
    Border,
    Button,
    DrawingCanvas,
    Grid,
    NumericUpDown,
    Slider,
    StackPanel,
    TextBlock,
    TextBox,
    useEffect,
    useRef,
    useState
} from "@sharpts/gui";
import type { DesktopStyle, DrawingCommand, GuiElement } from "@sharpts/gui";

export const PAINT_STYLES: DesktopStyle[] = [
    { selector: { control: "Button" }, setters: { cornerRadius: 4 } },
    { selector: { control: "ToggleButton" }, setters: { cornerRadius: 4 } }
];

export interface Palette {
    canvas: string;
    panel: string;
    surface: string;
    border: string;
    text: string;
    muted: string;
    accent: string;
    selected: string;
    danger: string;
    checker: string;
}
export const LIGHT: Palette = {
    canvas: "#dce1e8",
    panel: "#f4f6fa",
    surface: "#ffffff",
    border: "#c6cfdb",
    text: "#172336",
    muted: "#536276",
    accent: "#2365d1",
    selected: "#dceaff",
    danger: "#b3261e",
    checker: "#d4d9e0"
};
export const DARK: Palette = {
    canvas: "#10151d",
    panel: "#1a2230",
    surface: "#232e3e",
    border: "#3d4a5d",
    text: "#edf2fa",
    muted: "#b2bfd2",
    accent: "#8cb9ff",
    selected: "#29476c",
    danger: "#ffb4ab",
    checker: "#c0c5cc"
};

/** A draft survives invalid intermediate input; only a valid committed edit reaches the model. */
export function TextField(props: {
    id: string;
    label: string;
    value: string;
    palette: Palette;
    maxLength?: number;
    validate?: (value: string) => string | null;
    onCommit: (value: string) => void;
}): GuiElement {
    const [draft, setDraft] = useState<string>(props.value);
    const [error, setError] = useState<string>("");
    const value = useRef<string>(props.value);
    useEffect(() => {
        value.current = props.value;
        setDraft(props.value);
        setError("");
    }, [props.value]);
    const commit = (): void => {
        const message = props.validate ? props.validate(value.current) : null;
        if (message) {
            setError(message);
            return;
        }
        setError("");
        if (value.current !== props.value) props.onCommit(value.current);
    };
    return (
        <StackPanel spacing={4}>
            <TextBlock fontSize={12} foreground={props.palette.muted} textWrapping="wrap">
                {props.label}
            </TextBlock>
            <TextBox
                key={props.id}
                automationName={props.label}
                text={draft}
                maxLength={props.maxLength || 80}
                onTextChanged={(text) => {
                    value.current = text;
                    setDraft(text);
                }}
                onBlur={commit}
                onKeyDown={(event) => {
                    if (event.key === "Enter") {
                        commit();
                        return true;
                    }
                    if (event.key === "Escape") {
                        value.current = props.value;
                        setDraft(props.value);
                        setError("");
                        return true;
                    }
                    return false;
                }}
            />
            <TextBlock
                key={props.id + "-error"}
                isVisible={error !== ""}
                foreground={props.palette.danger}
                textWrapping="wrap"
            >
                {error}
            </TextBlock>
        </StackPanel>
    );
}

export function NumberField(props: {
    id: string;
    label: string;
    value: number;
    minimum: number;
    maximum: number;
    step?: number;
    palette: Palette;
    onChange: (value: number) => void;
    onBegin?: () => void;
    onEnd?: () => void;
}): GuiElement {
    return (
        <StackPanel spacing={4}>
            <TextBlock fontSize={12} foreground={props.palette.muted}>
                {props.label}
            </TextBlock>
            <Grid columns="*,76" rows="auto">
                <Slider
                    key={props.id}
                    automationName={props.label}
                    minimum={props.minimum}
                    maximum={props.maximum}
                    value={props.value}
                    margin={[0, 0, 10, 0] as const}
                    onValueChanged={props.onChange}
                    onEditStarted={() => {
                        if (props.onBegin) props.onBegin();
                    }}
                    onEditCompleted={() => {
                        if (props.onEnd) props.onEnd();
                    }}
                />
                <NumericUpDown
                    showButtonSpinner={false}
                    key={props.id + "-number"}
                    gridColumn={1}
                    automationName={props.label + " value"}
                    minimum={props.minimum}
                    maximum={props.maximum}
                    increment={props.step || 1}
                    value={props.value}
                    verticalAlignment="center"
                    onFocus={() => {
                        if (props.onBegin) props.onBegin();
                    }}
                    onBlur={() => {
                        if (props.onEnd) props.onEnd();
                    }}
                    onValueChanged={(value) => {
                        if (value !== null) props.onChange(value);
                    }}
                />
            </Grid>
        </StackPanel>
    );
}

/** Small path icons avoid OS-dependent Unicode glyph fallback. */
export function Icon(props: { name: string; color: string }): GuiElement {
    const stroke = props.color;
    const line = (x1: number, y1: number, x2: number, y2: number): DrawingCommand => ({
        kind: "line",
        x1,
        y1,
        x2,
        y2,
        stroke,
        strokeThickness: 1.7
    });
    let commands: DrawingCommand[] = [];
    if (props.name === "brush")
        commands = [line(4, 14, 14, 4), line(3, 15, 6, 15), line(3, 15, 3, 12), line(12, 4, 14, 6)];
    else if (props.name === "eraser")
        commands = [
            line(3, 11, 10, 4),
            line(10, 4, 15, 9),
            line(15, 9, 8, 16),
            line(8, 16, 3, 11),
            line(6, 8, 11, 13),
            line(7, 16, 16, 16)
        ];
    else if (props.name === "line") commands = [line(3, 15, 15, 3)];
    else if (props.name === "rectangle")
        commands = [{ kind: "rectangle", x: 3, y: 4, width: 12, height: 10, stroke, strokeThickness: 1.7 }];
    else if (props.name === "ellipse")
        commands = [
            { kind: "ellipse", centerX: 9, centerY: 9, radiusX: 6, radiusY: 5, stroke, strokeThickness: 1.7 }
        ];
    else if (props.name === "fill")
        commands = [
            line(3, 9, 9, 3),
            line(9, 3, 14, 8),
            line(14, 8, 8, 14),
            line(8, 14, 3, 9),
            line(4, 9, 13, 9),
            line(14, 13, 14, 16)
        ];
    else if (props.name === "picker")
        commands = [line(4, 14, 13, 5), line(10, 3, 15, 8), line(3, 15, 6, 14), line(3, 15, 4, 12)];
    else if (props.name === "text") commands = [line(3, 4, 15, 4), line(9, 4, 9, 15), line(6, 15, 12, 15)];
    else if (props.name === "add") commands = [line(9, 3, 9, 15), line(3, 9, 15, 9)];
    else if (props.name === "remove") commands = [line(3, 9, 15, 9)];
    else if (props.name === "duplicate")
        commands = [
            { kind: "rectangle", x: 6, y: 6, width: 9, height: 9, stroke, strokeThickness: 1.5 },
            line(3, 12, 3, 3),
            line(3, 3, 12, 3)
        ];
    else if (props.name === "up") commands = [line(9, 15, 9, 3), line(9, 3, 4, 8), line(9, 3, 14, 8)];
    else if (props.name === "down") commands = [line(9, 3, 9, 15), line(9, 15, 4, 10), line(9, 15, 14, 10)];
    else if (props.name === "undo")
        commands = [
            line(4, 6, 13, 6),
            line(13, 6, 15, 9),
            line(15, 9, 15, 14),
            line(4, 6, 8, 2),
            line(4, 6, 8, 10)
        ];
    else if (props.name === "redo")
        commands = [
            line(14, 6, 5, 6),
            line(5, 6, 3, 9),
            line(3, 9, 3, 14),
            line(14, 6, 10, 2),
            line(14, 6, 10, 10)
        ];
    return <DrawingCanvas width={18} height={18} commands={commands} isHitTestVisible={false} />;
}

export function IconButton(props: {
    id: string;
    label: string;
    icon: string;
    palette: Palette;
    enabled?: boolean;
    onClick: () => void;
}): GuiElement {
    return (
        <Button
            key={props.id}
            width={32}
            height={32}
            padding={6}
            automationName={props.label}
            toolTip={props.label}
            isEnabled={props.enabled !== false}
            onClick={props.onClick}
        >
            <Icon name={props.icon} color={props.palette.text} />
        </Button>
    );
}
