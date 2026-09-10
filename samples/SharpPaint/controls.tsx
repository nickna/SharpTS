import { PathIcon, IconButton as DesktopIconButton, DESKTOP_STYLES } from "@sharpts/gui";
import type { DesktopPalette, GuiElement } from "@sharpts/gui";
export { TextField, NumberField } from "@sharpts/gui";
export type Palette = DesktopPalette;
export const PAINT_STYLES = DESKTOP_STYLES;

// Paint-specific geometry remains in the sample. The SDK supplies rendering,
// native foreground inheritance, accessible button chrome, and state styling.
export const ICONS: { [name: string]: string } = {
    brush: "M3 14 L13 4 L16 7 L6 17 L2 18 Z M14 2 L18 6 L16 8 L12 4 Z",
    eraser: "M2 12 L11 3 L18 10 L10 18 L7 18 Z M5 12 L8 15 L11 12 L8 9 Z M10 18 L19 18 L19 20 L8 20 Z",
    line: "M2 17 L17 2 L19 4 L4 19 Z",
    rectangle: "M2 4 L18 4 L18 17 L2 17 Z M4 6 L4 15 L16 15 L16 6 Z",
    ellipse: "M10 3 A8 7 0 1 1 10 17 A8 7 0 1 1 10 3 Z M10 5 A6 5 0 1 0 10 15 A6 5 0 1 0 10 5 Z",
    fill: "M3 10 L10 3 L17 10 L10 17 Z M6 10 L14 10 L10 6 Z M17 13 Q22 19 17 20 Q13 19 17 13 Z",
    picker: "M2 18 L4 13 L13 4 L16 7 L7 16 Z M12 2 L18 8 L20 6 L14 0 Z",
    text: "M2 3 L18 3 L18 6 L12 6 L12 17 L15 17 L15 19 L5 19 L5 17 L8 17 L8 6 L2 6 Z",
    add: "M9 2 L11 2 L11 9 L18 9 L18 11 L11 11 L11 18 L9 18 L9 11 L2 11 L2 9 L9 9 Z",
    remove: "M4 6 L16 6 L15 18 L5 18 Z M2 3 L8 3 L8 1 L12 1 L12 3 L18 3 L18 5 L2 5 Z",
    duplicate: "M3 2 L15 2 L15 4 L5 4 L5 14 L3 14 Z M7 6 L19 6 L19 19 L7 19 Z M9 8 L9 17 L17 17 L17 8 Z",
    up: "M2 10 L10 2 L18 10 L16 12 L11 7 L11 19 L9 19 L9 7 L4 12 Z",
    down: "M2 10 L4 8 L9 13 L9 1 L11 1 L11 13 L16 8 L18 10 L10 18 Z",
    undo: "M2 7 L8 1 L8 5 L13 5 Q19 5 19 12 L19 17 L16 17 L16 12 Q16 8 12 8 L8 8 L8 12 Z",
    redo: "M18 7 L12 1 L12 5 L7 5 Q1 5 1 12 L1 17 L4 17 L4 12 Q4 8 8 8 L12 8 L12 12 Z",
    merge: "M4 1 L6 1 L6 7 L10 11 L14 7 L14 1 L16 1 L16 8 L11 13 L11 16 L15 16 L10 21 L5 16 L9 16 L9 13 L4 8 Z"
};
export function Icon(props: { name: string }): GuiElement {
    return (
        <PathIcon width={17} height={17} data={ICONS[props.name] || ICONS.brush} isHitTestVisible={false} />
    );
}
export function IconButton(props: {
    id: string;
    label: string;
    icon: string;
    enabled?: boolean;
    onClick: () => void;
}): GuiElement {
    return (
        <DesktopIconButton
            id={props.id}
            label={props.label}
            data={ICONS[props.icon]}
            enabled={props.enabled !== false}
            onClick={props.onClick}
        />
    );
}
