import {
    Border,
    Button,
    Grid,
    NumericUpDown,
    PathIcon,
    ScrollViewer,
    Slider,
    StackPanel,
    TextBlock,
    TextBox,
    useEffect,
    useRef,
    useState,
    useTheme
} from "./runtime";
import type { DesktopStyle } from "./runtime";
import type { GuiChild, GuiElement } from "./runtime-types";

/** Semantic colors shared by desktop compositions. @category Core and Composition */
export interface DesktopPalette {
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
/** Light semantic desktop colors. @category Core and Composition */
export const LIGHT_PALETTE: DesktopPalette = {
    canvas: "#e5e9ef",
    panel: "#f6f8fb",
    surface: "#ffffff",
    border: "#d8dfe9",
    text: "#1b293c",
    muted: "#596b82",
    accent: "#2563eb",
    selected: "#e4edff",
    danger: "#b42318",
    checker: "#d4d9e0"
};
/** Dark semantic desktop colors. @category Core and Composition */
export const DARK_PALETTE: DesktopPalette = {
    canvas: "#111720",
    panel: "#1b2432",
    surface: "#242f40",
    border: "#3b4a60",
    text: "#edf3fc",
    muted: "#b2c0d4",
    accent: "#91bbff",
    selected: "#2c4668",
    danger: "#ffb4ab",
    checker: "#c0c5cc"
};
/** Resolves semantic colors from the owning window's native theme. @category Hooks and State */
export function useDesktopPalette(): DesktopPalette {
    return useTheme() === "dark" ? DARK_PALETTE : LIGHT_PALETTE;
}

/** Small opt-in desktop style set; use classes rather than overriding every control. @category Core and Composition */
export const DESKTOP_STYLES: DesktopStyle[] = [
    { selector: { control: "ListBoxItem" }, setters: { padding: [2, 8] as const } },
    { selector: { control: "Button" }, setters: { cornerRadius: 5, minHeight: 30 } },
    { selector: { control: "ToggleButton" }, setters: { cornerRadius: 5, minHeight: 30 } },
    {
        selector: { control: "Button", classes: ["subtle"] },
        setters: { background: "Transparent", borderThickness: 0 }
    },
    {
        selector: { control: "Button", classes: ["primary"] },
        setters: { background: "#2563eb", foreground: "#ffffff", borderThickness: 0 }
    },
    { selector: { control: "Button", states: ["disabled"] }, setters: { opacity: 0.5 } },
    { selector: { control: "ToggleButton", states: ["disabled"] }, setters: { opacity: 0.5 } }
];

/** A draft stays editable until validation succeeds; Enter/blur commit and Escape restores the model. @category Hooks and State */
export function useTextDraft(
    value: string,
    onCommit: (value: string) => void,
    validate?: (value: string) => string | null
) {
    const [draft, setDraft] = useState<string>(value);
    const [error, setError] = useState<string>("");
    const current = useRef<string>(value);
    useEffect(() => {
        current.current = value;
        setDraft(value);
        setError("");
    }, [value]);
    const change = (text: string): void => {
        current.current = text;
        setDraft(text);
        setError("");
    };
    const commit = (): boolean => {
        const message = validate ? validate(current.current) : null;
        setError(message || "");
        if (message) return false;
        if (current.current !== value) onCommit(current.current);
        return true;
    };
    const cancel = (): void => {
        change(value);
    };
    return { draft, error, change, commit, cancel };
}

/** Label and validation presentation shared by native editors. @category Components */
export function Field(props: {
    label: string;
    error?: string;
    hint?: string;
    children?: GuiChild;
}): GuiElement {
    const palette = useDesktopPalette();
    return (
        <StackPanel spacing={4}>
            <TextBlock fontSize={12} foreground={palette.muted}>
                {props.label}
            </TextBlock>
            {props.children}
            {props.error ? (
                <TextBlock foreground={palette.danger} textWrapping="wrap" fontSize={12}>
                    {props.error}
                </TextBlock>
            ) : null}
            {props.hint ? (
                <TextBlock foreground={palette.muted} textWrapping="wrap" fontSize={12}>
                    {props.hint}
                </TextBlock>
            ) : null}
        </StackPanel>
    );
}

/** Draft-preserving native text field. @category Components */
export function TextField(props: {
    id: string;
    label: string;
    value: string;
    maxLength?: number;
    validate?: (value: string) => string | null;
    onCommit: (value: string) => void;
}): GuiElement {
    const edit = useTextDraft(props.value, props.onCommit, props.validate);
    return (
        <Field label={props.label} error={edit.error}>
            <TextBox
                key={props.id}
                automationName={props.label}
                text={edit.draft}
                maxLength={props.maxLength === undefined ? 256 : props.maxLength}
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
        </Field>
    );
}

/** Slider and exact editor with one step/precision contract and explicit edit boundaries. @category Components */
export function NumberField(props: {
    id: string;
    label: string;
    value: number;
    minimum: number;
    maximum: number;
    step?: number;
    precision?: number;
    onChange: (value: number) => void;
    onBegin?: () => void;
    onEnd?: () => void;
}): GuiElement {
    const step = props.step === undefined ? 1 : props.step;
    const precision = props.precision === undefined ? (step < 1 ? 2 : 0) : props.precision;
    const change = (value: number): void => {
        const normalized = Math.max(
            props.minimum,
            Math.min(
                props.maximum,
                Number((props.minimum + Math.round((value - props.minimum) / step) * step).toFixed(precision))
            )
        );
        props.onChange(normalized);
    };
    return (
        <Field label={props.label}>
            <Grid columns="*,8,72" rows="32">
                <Slider
                    key={props.id}
                    automationName={props.label}
                    height={28}
                    verticalAlignment="center"
                    minimum={props.minimum}
                    maximum={props.maximum}
                    value={props.value}
                    onValueChanged={change}
                    onEditStarted={() => {
                        if (props.onBegin) props.onBegin();
                    }}
                    onEditCompleted={() => {
                        if (props.onEnd) props.onEnd();
                    }}
                />
                <NumericUpDown
                    key={props.id + "-number"}
                    gridColumn={2}
                    automationName={props.label + " value"}
                    minimum={props.minimum}
                    maximum={props.maximum}
                    value={props.value}
                    increment={step}
                    formatString={precision === 0 ? "0" : "0." + "#".repeat(precision)}
                    showButtonSpinner={false}
                    onValueChanged={(value) => {
                        if (value !== null) change(value);
                    }}
                    onFocus={() => {
                        if (props.onBegin) props.onBegin();
                    }}
                    onBlur={() => {
                        if (props.onEnd) props.onEnd();
                    }}
                />
            </Grid>
        </Field>
    );
}

/** Inspector with a scrolling body and always reachable actions. @category Components */
export function Inspector(props: {
    title: string;
    headerAction?: GuiElement;
    children?: GuiElement;
    actions?: GuiElement;
}): GuiElement {
    const palette = useDesktopPalette();
    return (
        <Grid columns="*" rows="auto,*,auto">
            <Border padding={12} borderBrush={palette.border} borderThickness={[0, 0, 0, 1] as const}>
                <Grid columns="*,auto" rows="auto">
                    <TextBlock fontWeight="semibold" foreground={palette.text} verticalAlignment="center">
                        {props.title}
                    </TextBlock>
                    <Border gridColumn={1}>{props.headerAction}</Border>
                </Grid>
            </Border>
            <ScrollViewer
                gridRow={1}
                horizontalScrollBarVisibility="disabled"
                verticalScrollBarVisibility="auto"
            >
                <Border padding={12}>{props.children}</Border>
            </ScrollViewer>
            {props.actions ? (
                <Border
                    gridRow={2}
                    padding={10}
                    borderBrush={palette.border}
                    borderThickness={[0, 1, 0, 0] as const}
                >
                    {props.actions}
                </Border>
            ) : null}
        </Grid>
    );
}

/** Accessible icon-only command; the path inherits the native control foreground. @category Components */
export function IconButton(props: {
    id: string;
    label: string;
    data: string;
    enabled?: boolean;
    onClick: () => void | Promise<unknown>;
}): GuiElement {
    return (
        <Button
            key={props.id}
            width={30}
            height={30}
            padding={7}
            classes={["subtle"]}
            automationName={props.label}
            toolTip={props.label}
            isEnabled={props.enabled !== false}
            onClick={props.onClick}
        >
            <PathIcon data={props.data} width={16} height={16} isHitTestVisible={false} />
        </Button>
    );
}

/** Canonical RGB/ARGB hex color; opaque ARGB values collapse to RGB. @category Core and Composition */
export function normalizeHexColor(color: string): string {
    const value = color.trim().toLowerCase();
    if (!/^#[0-9a-f]{6}([0-9a-f]{2})?$/.test(value)) throw new Error("Enter #RRGGBB or #AARRGGBB.");
    return value.length === 9 && value.slice(1, 3) === "ff" ? "#" + value.slice(3) : value;
}
