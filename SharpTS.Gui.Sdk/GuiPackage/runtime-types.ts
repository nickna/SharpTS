export type Thickness = number | readonly [number, number] | readonly [number, number, number, number];
export type HorizontalAlignment = "left" | "center" | "right" | "stretch";
export type VerticalAlignment = "top" | "center" | "bottom" | "stretch";
export type Orientation = "horizontal" | "vertical";
export type ScrollBarVisibility = "auto" | "visible" | "hidden" | "disabled";
export type Theme = "system" | "light" | "dark";
export type Stretch = "none" | "fill" | "uniform" | "uniformToFill";
export type SelectionMode = "single" | "multiple";
export type Dock = "left" | "top" | "right" | "bottom";
export type FontWeight = "normal" | "medium" | "semibold" | "bold";
export type TextAlignment = "left" | "center" | "right" | "justify";

export interface SourceInfo { fileName: string; lineNumber: number; columnNumber: number; }
export interface GuiElement {
    readonly __guiElement: true;
    readonly type: any;
    readonly props: any;
    readonly key: string | null;
    readonly source: SourceInfo | null;
}
export type GuiChild = GuiElement | string | number | boolean | null | undefined | readonly GuiChild[];
export interface TextualChildArray { readonly length: number; readonly [index: number]: TextualChild; }
export type TextualChild = string | number | boolean | null | undefined | TextualChildArray;
export type Component<P = {}> = (props: Readonly<P & { children?: GuiChild }>) => GuiChild;
export type SignalSetter<T> = (value: T | ((previous: T) => T)) => void;
export type StateSetter<T> = SignalSetter<T>;
export type Dispatch<A> = (action: A) => void;
/** Catches render/effect failures and native commit failures only after the previous native tree is restored. */
export interface ErrorBoundaryProps {
    readonly children?: GuiChild;
    readonly fallback: (error: unknown, reset: () => void) => GuiChild;
}

export interface MutableRef<T> { current: T; }
export interface ControlRef<THandle> {
    readonly __controlHandle: THandle;
    readonly isAttached: boolean;
    focus(): boolean;
}
export type WindowHandle = { readonly __windowHandle: never };
export type StackPanelHandle = { readonly __stackPanelHandle: never };
export type GridHandle = { readonly __gridHandle: never };
export type BorderHandle = { readonly __borderHandle: never };
export type TextBlockHandle = { readonly __textBlockHandle: never };
export type ButtonHandle = { readonly __buttonHandle: never };
export type TextBoxHandle = { readonly __textBoxHandle: never };

export interface KeyEvent {
    readonly key: string;
    readonly ctrl: boolean;
    readonly alt: boolean;
    readonly shift: boolean;
    readonly meta: boolean;
    readonly repeat: boolean;
}
export interface CommonProps<THandle = unknown> {
    ref?: ControlRef<THandle>;
    width?: number; height?: number;
    minWidth?: number; minHeight?: number; maxWidth?: number; maxHeight?: number;
    margin?: Thickness;
    horizontalAlignment?: HorizontalAlignment;
    verticalAlignment?: VerticalAlignment;
    isVisible?: boolean; isEnabled?: boolean; opacity?: number;
    toolTip?: string; automationName?: string;
    gridRow?: number; gridColumn?: number; gridRowSpan?: number; gridColumnSpan?: number;
    dock?: Dock;
    onKeyDown?: (event: KeyEvent) => boolean;
    onKeyUp?: (event: KeyEvent) => boolean;
}
export interface TextStyleProps {
    foreground?: string; fontFamily?: string; fontSize?: number;
    fontWeight?: FontWeight; fontStyle?: "normal" | "italic";
    textAlignment?: TextAlignment;
}
export interface ContentStyleProps extends TextStyleProps {
    background?: string; padding?: Thickness;
    cornerRadius?: number;
    horizontalContentAlignment?: HorizontalAlignment;
    verticalContentAlignment?: VerticalAlignment;
}
