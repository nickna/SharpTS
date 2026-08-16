import { DesktopDevtoolsBridge } from "dotnet:SharpTS.Gui";

/** Source location associated with an inspected GUI node. @category Devtools */
export interface InspectorSourceLocation {
    /** Source file path. */
    file: string;
    /** One-based source line. */
    line: number;
    /** One-based source column. */
    column: number;
}

/** Native control bounds in window coordinate space. @category Devtools */
export interface InspectorBounds {
    /** Horizontal origin. */
    x: number;
    /** Vertical origin. */
    y: number;
    /** Bounds width. */
    width: number;
    /** Bounds height. */
    height: number;
}

/** One node in a desktop inspector snapshot. @category Devtools */
export interface InspectorNode {
    /** SharpTS control kind. */
    kind: string;
    /** Reconciliation key, when provided. */
    key: string | null;
    /** Managed native control type name. */
    nativeType: string;
    /** TSX source location, when available. */
    source: InspectorSourceLocation | null;
    /** Native layout bounds. */
    bounds: InspectorBounds;
    /** Whether the native control is visible. */
    isVisible: boolean;
    /** Whether the native control accepts interaction. */
    isEnabled: boolean;
    /** Avalonia style classes applied to the control. */
    classes: string[];
    /** Selected commonly inspected prop values. */
    props: {
        /** Text content, when supported by the control. */
        text: string | null;
        /** Title content, when supported by the control. */
        title: string | null;
        /** Explicit width, when present. */
        width: number | null;
        /** Explicit height, when present. */
        height: number | null;
    };
    /** Inspected native child nodes. */
    children: InspectorNode[];
}

/** Inspector snapshot containing all live desktop windows. @category Devtools */
export interface DesktopInspectorSnapshot {
    /** Root inspector node for each live window. */
    windows: InspectorNode[];
}

/** Returns a snapshot of every live desktop native tree. @returns The current inspector snapshot. @category Devtools */
export function inspectDesktopTree(): DesktopInspectorSnapshot {
    return JSON.parse(DesktopDevtoolsBridge.InspectDesktopTreeJson()) as DesktopInspectorSnapshot;
}

/**
 * Captures a headless rendering snapshot at a local path.
 * @param path - Destination snapshot path.
 * @returns The resolved snapshot path.
 * @category Devtools
 */
export function captureHeadlessSnapshot(path: string): string {
    return DesktopDevtoolsBridge.CaptureHeadlessSnapshot(path);
}

/**
 * Compares a headless rendering with a stored snapshot, optionally updating it.
 * @param path - Stored snapshot path.
 * @param update - Whether a missing or changed snapshot should be replaced.
 * @returns The resolved snapshot path after comparison or update.
 * @category Devtools
 */
export function assertHeadlessSnapshot(path: string, update: boolean = false): string {
    return DesktopDevtoolsBridge.AssertHeadlessSnapshot(path, update);
}
