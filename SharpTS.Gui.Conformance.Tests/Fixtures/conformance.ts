import { DesktopConformanceSupportBridge } from "dotnet:SharpTS.Gui.ConformanceSupport";
import type { DesktopRoot } from "@sharpts/gui";

function managed(root: DesktopRoot): any {
    const value: any = (root as any).__managedRoot;
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

export function traceControlIdentities(root: DesktopRoot, stage: string): void {
    DesktopConformanceSupportBridge.TraceControlIdentities(managed(root), stage);
}

export function getIdentity(root: DesktopRoot, key: string): number {
    return DesktopConformanceSupportBridge.GetIdentity(managed(root), key);
}

export function getActiveSubscriptionCount(root: DesktopRoot): number {
    return DesktopConformanceSupportBridge.GetActiveSubscriptionCount(managed(root));
}

export function failNextNativeSetter(root: DesktopRoot, key: string): void {
    DesktopConformanceSupportBridge.FailNextNativeSetter(managed(root), key);
}
