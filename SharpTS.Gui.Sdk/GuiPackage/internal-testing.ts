import { DesktopConformanceBridge } from "dotnet:SharpTS.Gui";

// Conformance-only helpers. This subpath is intentionally separate from the
// supported @sharpts/gui entry point.
export function closeWindow(): void {
    DesktopConformanceBridge.CloseWindow();
}

export function cancelNextWindowClose(): void {
    DesktopConformanceBridge.CancelNextWindowClose();
}

export function trace(stage: string): void {
    DesktopConformanceBridge.Trace(stage);
}

export async function completeOffThread(): Promise<void> {
    await DesktopConformanceBridge.CompleteOffThreadAsync();
}

export function queueMicrotask(callback: () => void): void {
    DesktopConformanceBridge.QueueMicrotask(callback);
}

export function beginOffThreadTask(callback: () => void): void {
    DesktopConformanceBridge.BeginOffThreadTask(callback);
}

export function traceControlIdentities(stage: string): void {
    DesktopConformanceBridge.TraceControlIdentities(stage);
}

export function click(key: string): void {
    DesktopConformanceBridge.Click(key);
}

export function pressKey(key: string): void {
    DesktopConformanceBridge.PressKey(key);
}

export function getText(key: string): string {
    return DesktopConformanceBridge.GetText(key);
}

export function getProperty(key: string, property: string): string {
    return DesktopConformanceBridge.GetProperty(key, property);
}

export function getIdentity(key: string): number {
    return DesktopConformanceBridge.GetIdentity(key);
}

export function getActiveSubscriptionCount(): number {
    return DesktopConformanceBridge.GetActiveSubscriptionCount();
}

export function failNextNativeSetter(key: string): void {
    DesktopConformanceBridge.FailNextNativeSetter(key);
}

export function isRefAttached(reference: unknown): boolean {
    return DesktopConformanceBridge.IsRefAttached(reference as any);
}

export function setTextBoxValue(key: string, value: string): void {
    DesktopConformanceBridge.SetTextBoxValue(key, value);
}

export function setCheckBoxValue(key: string, value: boolean): void {
    DesktopConformanceBridge.SetCheckBoxValue(key, value);
}

export function setComboBoxIndex(key: string, value: number): void {
    DesktopConformanceBridge.SetComboBoxIndex(key, value);
}

export function setSliderValue(key: string, value: number): void {
    DesktopConformanceBridge.SetSliderValue(key, value);
}

export function dropText(key: string, value: string): string {
    return DesktopConformanceBridge.DropText(key, value);
}
