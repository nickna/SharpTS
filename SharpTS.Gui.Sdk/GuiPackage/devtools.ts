import { DesktopDevtoolsBridge } from "dotnet:SharpTS.Gui";

export interface InspectorSourceLocation {
    file: string;
    line: number;
    column: number;
}

export interface InspectorBounds {
    x: number;
    y: number;
    width: number;
    height: number;
}

export interface InspectorNode {
    kind: string;
    key: string | null;
    nativeType: string;
    source: InspectorSourceLocation | null;
    bounds: InspectorBounds;
    isVisible: boolean;
    isEnabled: boolean;
    classes: string[];
    props: {
        text: string | null;
        title: string | null;
        width: number | null;
        height: number | null;
    };
    children: InspectorNode[];
}

export interface DesktopInspectorSnapshot {
    windows: InspectorNode[];
}

export function inspectDesktopTree(): DesktopInspectorSnapshot {
    return JSON.parse(DesktopDevtoolsBridge.InspectDesktopTreeJson()) as DesktopInspectorSnapshot;
}

export function captureHeadlessSnapshot(path: string): string {
    return DesktopDevtoolsBridge.CaptureHeadlessSnapshot(path);
}

export function assertHeadlessSnapshot(path: string, update: boolean = false): string {
    return DesktopDevtoolsBridge.AssertHeadlessSnapshot(path, update);
}
