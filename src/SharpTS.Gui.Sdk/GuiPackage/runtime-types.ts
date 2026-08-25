/** One, two, or four device-independent values describing edge thickness. @category Core and Composition */
export type Thickness = number | readonly [number, number] | readonly [number, number, number, number];
/** Horizontal placement within a layout slot. @category Core and Composition */
export type HorizontalAlignment = "left" | "center" | "right" | "stretch";
/** Vertical placement within a layout slot. @category Core and Composition */
export type VerticalAlignment = "top" | "center" | "bottom" | "stretch";
/** Direction in which a panel arranges its children. @category Core and Composition */
export type Orientation = "horizontal" | "vertical";
/** Policy controlling when a scroll bar is displayed. @category Core and Composition */
export type ScrollBarVisibility = "auto" | "visible" | "hidden" | "disabled";
/** Window color theme. @category Core and Composition */
export type Theme = "system" | "light" | "dark";
/** Image scaling behavior within the available layout slot. @category Core and Composition */
export type Stretch = "none" | "fill" | "uniform" | "uniformToFill";
/** Single- or multiple-item selection behavior. @category Core and Composition */
export type SelectionMode = "single" | "multiple";
/** Edge used to place a child inside a DockPanel. @category Core and Composition */
export type Dock = "left" | "top" | "right" | "bottom";
/** Supported text font weights. @category Core and Composition */
export type FontWeight = "normal" | "medium" | "semibold" | "bold";
/** Supported horizontal text alignment values. @category Core and Composition */
export type TextAlignment = "left" | "center" | "right" | "justify";
/** Pointer device category reported by native desktop input. @category Core and Composition */
export type PointerType = "mouse" | "pen" | "touch" | "unknown";
/** Button whose state changed for a pointer event. @category Core and Composition */
export type PointerButton = "none" | "left" | "middle" | "right" | "x1" | "x2";
/** Native desktop window presentation state. @category Core and Composition */
export type WindowState = "normal" | "minimized" | "maximized" | "fullScreen";

/** Coalesced post-layout metrics for the containing desktop window. @category Core and Composition */
export interface WindowMetricsEvent {
    /** Arranged client width in device-independent pixels. */
    readonly clientWidth: number;
    /** Arranged client height in device-independent pixels. */
    readonly clientHeight: number;
    /** Native pixels per device-independent pixel on the current display. */
    readonly scaling: number;
    /** Current native presentation state. */
    readonly windowState: WindowState;
    /** Operating-system name of the current display, when available. */
    readonly displayName: string;
    /** Whether the current display is the primary display. */
    readonly isPrimary: boolean;
    /** Current display work-area width in device-independent pixels. */
    readonly workingAreaWidth: number;
    /** Current display work-area height in device-independent pixels. */
    readonly workingAreaHeight: number;
    /** Current display work area in physical desktop pixels. */
    readonly pixelWorkingArea: {
        readonly x: number;
        readonly y: number;
        readonly width: number;
        readonly height: number;
    };
}

/** Normalized native pointer event in local device-independent coordinates. @category Core and Composition */
export interface PointerEvent {
    /** Stable native pointer identifier for the duration of the gesture. */
    readonly pointerId: number;
    /** Device category that produced the event. */
    readonly pointerType: PointerType;
    /** Local horizontal position in device-independent pixels. */
    readonly x: number;
    /** Local vertical position in device-independent pixels. */
    readonly y: number;
    /** Button whose state changed, or none for movement/cancellation. */
    readonly button: PointerButton;
    /** Standard button bitmask: left=1, right=2, middle=4, x1=8, x2=16. */
    readonly buttons: number;
    /** Normalized pressure from zero to one. */
    readonly pressure: number;
    /** Whether Control is pressed. */
    readonly ctrl: boolean;
    /** Whether Alt is pressed. */
    readonly alt: boolean;
    /** Whether Shift is pressed. */
    readonly shift: boolean;
    /** Whether the platform meta key is pressed. */
    readonly meta: boolean;
}

/** An independently formatted run rendered by RichTextBlock. @category Core and Composition */
export interface RichTextRun {
    /** Text contained in the run. */
    text: string;
    /** Optional foreground brush or color. */
    foreground?: string;
    /** Optional font size in device-independent pixels. */
    fontSize?: number;
    /** Optional font weight. */
    fontWeight?: FontWeight;
    /** Optional normal or italic font style. */
    fontStyle?: "normal" | "italic";
}

/** Retained drawing command rendered by DrawingCanvas. @category Core and Composition */
export type DrawingCommand =
    { kind: "line"; x1: number; y1: number; x2: number; y2: number; stroke: string; strokeThickness?: number; opacity?: number; composite?: DrawingCompositeMode } |
    { kind: "rectangle"; x: number; y: number; width: number; height: number; fill?: string; stroke?: string; strokeThickness?: number; opacity?: number; composite?: DrawingCompositeMode } |
    { kind: "ellipse"; centerX: number; centerY: number; radiusX: number; radiusY: number; fill?: string; stroke?: string; strokeThickness?: number; opacity?: number; composite?: DrawingCompositeMode } |
    { kind: "polyline"; points: readonly DrawingPoint[]; stroke: string; strokeThickness?: number; lineCap?: DrawingLineCap; lineJoin?: DrawingLineJoin; opacity?: number; composite?: DrawingCompositeMode } |
    { kind: "image"; source: string; x: number; y: number; width: number; height: number; opacity?: number; composite?: "sourceOver" };
/** A logical point used by polyline drawing commands. @category Core and Composition */
export interface DrawingPoint { readonly x: number; readonly y: number; }
/** Supported stroke end-cap shapes. @category Core and Composition */
export type DrawingLineCap = "butt" | "round" | "square";
/** Supported stroke join shapes. @category Core and Composition */
export type DrawingLineJoin = "miter" | "round" | "bevel";
/** Supported drawing compositing operations. @category Core and Composition */
export type DrawingCompositeMode = "sourceOver" | "destinationOut";

/** @internal */
export interface SourceInfo { fileName: string; lineNumber: number; columnNumber: number; }

/** Immutable virtual element produced by TSX and consumed by the GUI renderer. @category Core and Composition */
export interface GuiElement {
    /** Internal marker distinguishing GUI elements from ordinary objects. */
    readonly __guiElement: true;
    /** Component function or native control tag represented by this element. */
    readonly type: any;
    /** Props supplied when the element was created. */
    readonly props: any;
    /** Stable reconciliation key, when provided. */
    readonly key: string | null;
    /** Development source location, when emitted by the TSX development runtime. */
    readonly source: SourceInfo | null;
}

/** @internal */
export interface GuiChildArray { readonly length: number; readonly [index: number]: GuiChild; }
/** Content accepted by component and control children. @category Core and Composition */
export type GuiChild = GuiElement | string | number | boolean | null | undefined | GuiChildArray;
/** A nested array of textual control children. @category Core and Composition */
export interface TextualChildArray {
    /** Number of values in the array. */
    readonly length: number;
    /** Textual child at the requested index. */
    readonly [index: number]: TextualChild;
}
/** Text-compatible content accepted by controls such as TextBlock and Button. @category Core and Composition */
export type TextualChild = string | number | boolean | null | undefined | TextualChildArray;
/** Function component that returns GUI content. @category Core and Composition */
export type Component<P = {}> = (props: Readonly<P & { children?: GuiChild }>) => GuiChild;
/** Typed TSX tag for a packaged custom-control descriptor. @category Core and Composition */
export type CustomControlComponent<P extends object = {}> =
    (props: Readonly<P>) => GuiElement;
/** Setter returned by createSignal. @category Hooks and State */
export type SignalSetter<T> = (value: T | ((previous: T) => T)) => void;
/** Setter returned by useState. @category Hooks and State */
export type StateSetter<T> = SignalSetter<T>;
/** Reducer action dispatcher returned by useReducer. @category Hooks and State */
export type Dispatch<A> = (action: A) => void;

/** Props accepted by ErrorBoundary. @category Core and Composition */
export interface ErrorBoundaryProps {
    /** GUI content protected by the boundary. */
    readonly children?: GuiChild;
    /** Renders replacement content for an error and receives a function that retries the protected tree. */
    readonly fallback: (error: unknown, reset: () => void) => GuiChild;
}

/** Mutable object whose identity remains stable between renders. @category Hooks and State */
export interface MutableRef<T> {
    /** Current referenced value. */
    current: T;
}

/** Retained reference to a mounted native control. @category Hooks and State */
export interface ControlRef<THandle> {
    /** Phantom field preserving the native handle type. */
    readonly __controlHandle: THandle;
    /** Whether the reference is currently attached to a mounted control. */
    readonly isAttached: boolean;
    /** Moves keyboard focus to the attached control. @returns True when focus was requested successfully. */
    focus(): boolean;
}

/** Opaque handle type for Window controls. @category Core and Composition */
export type WindowHandle = { readonly __windowHandle: never };
/** Opaque handle type for StackPanel controls. @category Core and Composition */
export type StackPanelHandle = { readonly __stackPanelHandle: never };
/** Opaque handle type for Grid controls. @category Core and Composition */
export type GridHandle = { readonly __gridHandle: never };
/** Opaque handle type for Border controls. @category Core and Composition */
export type BorderHandle = { readonly __borderHandle: never };
/** Opaque handle type for TextBlock controls. @category Core and Composition */
export type TextBlockHandle = { readonly __textBlockHandle: never };
/** Opaque handle type for Button controls. @category Core and Composition */
export type ButtonHandle = { readonly __buttonHandle: never };
/** Opaque handle type for TextBox controls. @category Core and Composition */
export type TextBoxHandle = { readonly __textBoxHandle: never };

/** Normalized native keyboard event. @category Core and Composition */
export interface KeyEvent {
    /** Platform-independent key name. */
    readonly key: string;
    /** Whether Control is pressed. */
    readonly ctrl: boolean;
    /** Whether Alt is pressed. */
    readonly alt: boolean;
    /** Whether Shift is pressed. */
    readonly shift: boolean;
    /** Whether the platform meta key is pressed. */
    readonly meta: boolean;
    /** Whether this event repeats while the key is held. */
    readonly repeat: boolean;
}
/** Native drag-and-drop operation selected by a handler. @category Core and Composition */
export type DropEffect = "none" | "copy" | "move" | "link";
/** Normalized native drag-and-drop event. @category Core and Composition */
export interface DropEvent {
    /** Local file paths carried by the drop. */
    readonly files: readonly string[];
    /** Text carried by the drop, when available. */
    readonly text: string | null;
    /** Drag effect proposed by the native platform. */
    readonly effect: DropEffect;
    /** Whether Control is pressed. */
    readonly ctrl: boolean;
    /** Whether Alt is pressed. */
    readonly alt: boolean;
    /** Whether Shift is pressed. */
    readonly shift: boolean;
    /** Whether the platform meta key is pressed. */
    readonly meta: boolean;
}

/** Props shared by all built-in controls. @category Core and Composition */
export interface CommonProps<THandle = unknown> {
    /** Receives the retained native control handle. */
    ref?: ControlRef<THandle>;
    /** Preferred control width in device-independent pixels. */
    width?: number;
    /** Preferred control height in device-independent pixels. */
    height?: number;
    /** Minimum permitted control width. */
    minWidth?: number;
    /** Minimum permitted control height. */
    minHeight?: number;
    /** Maximum permitted control width. */
    maxWidth?: number;
    /** Maximum permitted control height. */
    maxHeight?: number;
    /** Space outside the control. */
    margin?: Thickness;
    /** Horizontal alignment within the parent layout slot. */
    horizontalAlignment?: HorizontalAlignment;
    /** Vertical alignment within the parent layout slot. */
    verticalAlignment?: VerticalAlignment;
    /** Whether the control participates in rendering and layout. */
    isVisible?: boolean;
    /** Whether the control accepts user interaction. */
    isEnabled?: boolean;
    /** Opacity from zero (transparent) to one (opaque). */
    opacity?: number;
    /** Text shown when the pointer pauses over the control. */
    toolTip?: string;
    /** Accessible automation name exposed to assistive technology and test drivers. */
    automationName?: string;
    /** Avalonia style classes applied to the native control. */
    classes?: readonly string[];
    /** Zero-based row index when the control is placed in a Grid. */
    gridRow?: number;
    /** Zero-based column index when the control is placed in a Grid. */
    gridColumn?: number;
    /** Number of Grid rows occupied by the control. */
    gridRowSpan?: number;
    /** Number of Grid columns occupied by the control. */
    gridColumnSpan?: number;
    /** Edge used when the control is placed in a DockPanel. */
    dock?: Dock;
    /** Canvas left coordinate when parented by Canvas. */
    canvasLeft?: number;
    /** Canvas top coordinate when parented by Canvas. */
    canvasTop?: number;
    /** Handles a normalized key-down event. */
    onKeyDown?: (event: KeyEvent) => boolean;
    /** Handles a normalized key-up event. */
    onKeyUp?: (event: KeyEvent) => boolean;
    /** Captures a pressed pointer until release, cancellation, unmount, or disposal. */
    capturePointerOnPress?: boolean;
    /** Handles a normalized pointer press. */
    onPointerDown?: (event: PointerEvent) => boolean;
    /** Handles normalized pointer movement. */
    onPointerMove?: (event: PointerEvent) => boolean;
    /** Handles a normalized pointer release. */
    onPointerUp?: (event: PointerEvent) => boolean;
    /** Handles cancellation or loss of an active pointer capture using its last known local state. */
    onPointerCancel?: (event: PointerEvent) => boolean;
    /** Allows native drag data to be dropped on this control. */
    allowDrop?: boolean;
    /** Chooses the accepted native drag effect. */
    onDragOver?: (event: DropEvent) => DropEffect;
    /** Receives normalized text and local-file drop data. */
    onDrop?: (event: DropEvent) => void | Promise<unknown>;
}

/** @internal */
export interface TextStyleProps {
    /** Foreground brush or color used to render content. */
    foreground?: string;
    /** Font family used to render text. */
    fontFamily?: string;
    /** Font size in device-independent pixels. */
    fontSize?: number;
    /** Weight used to render text. */
    fontWeight?: FontWeight;
    /** Normal or italic font style. */
    fontStyle?: "normal" | "italic";
    /** Horizontal alignment of rendered text. */
    textAlignment?: TextAlignment;
}

/** @internal */
export interface ContentStyleProps extends TextStyleProps {
    /** Background brush or color value. */
    background?: string;
    /** Space between the control border and its content. */
    padding?: Thickness;
    /** Radius applied to the control corners. */
    cornerRadius?: number;
    /** Horizontal alignment of content inside the control. */
    horizontalContentAlignment?: HorizontalAlignment;
    /** Vertical alignment of content inside the control. */
    verticalContentAlignment?: VerticalAlignment;
}
