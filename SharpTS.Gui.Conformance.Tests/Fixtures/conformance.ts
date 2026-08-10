import { DesktopConformanceSupportBridge } from "dotnet:SharpTS.Gui.ConformanceSupport";
import type { DesktopWindow } from "@sharpts/gui";

function managed(window: DesktopWindow): any {
    const value: any = (window as any).__managedRoot;
    if (value === undefined || value === null)
        throw new Error("The supplied value is not an active SharpTS desktop root.");
    return value;
}

export function cancelNextWindowClose(): void {
    DesktopConformanceSupportBridge.CancelNextWindowClose();
}

export function trace(stage: string): void {
    DesktopConformanceSupportBridge.Trace(stage);
}

export async function completeOffThread(): Promise<void> {
    await DesktopConformanceSupportBridge.CompleteOffThreadAsync();
}

export function queueMicrotask(callback: () => void): void {
    DesktopConformanceSupportBridge.QueueMicrotask(callback);
}

export function afterTrace(stage: string, callback: () => void): void {
    DesktopConformanceSupportBridge.AfterTrace(stage, callback);
}

export function beginOffThreadTask(callback: () => void): void {
    DesktopConformanceSupportBridge.BeginOffThreadTask(callback);
}

export function traceControlIdentities(window: DesktopWindow, stage: string): void {
    DesktopConformanceSupportBridge.TraceControlIdentities(managed(window), stage);
}

export function getIdentity(window: DesktopWindow, key: string): number {
    return DesktopConformanceSupportBridge.GetIdentity(managed(window), key);
}

export function getActiveSubscriptionCount(window: DesktopWindow): number {
    return DesktopConformanceSupportBridge.GetActiveSubscriptionCount(managed(window));
}

export function failNextNativeSetter(window: DesktopWindow, key: string): void {
    DesktopConformanceSupportBridge.FailNextNativeSetter(managed(window), key);
}
