import { DesktopTestingBridge } from "dotnet:SharpTS.Gui";
import type { DesktopWindow } from "./runtime.ts";

/** Native property that can be read through DesktopTestDriver. @category Testing */
export type DesktopTestProperty =
    "automationName" | "background" | "foreground" | "toolTip" | "isEnabled" | "isVisible";

/** Headless driver for locating and interacting with controls by their key. @category Testing */
export interface DesktopTestDriver {
    /** Runs a callback after pending async event, reactive render, and native commit work completes. */
    afterRender(callback: () => void): void;
    /** Activates the keyed control. */
    click(key: string): void;
    /** Invokes a keyed menu item through the native routed-click path. */
    clickMenuItem(key: string): void;
    /** Queues the result consumed by the next native message dialog. */
    queueMessageDialogResult(result: "ok" | "cancel" | "yes" | "no"): void;
    /** Queues local paths returned by the next native open-file dialog. */
    queueOpenFileDialogResult(paths: readonly string[]): void;
    /** Queues a local path, or null cancellation, for the next native save-file dialog. */
    queueSaveFileDialogResult(path: string | null): void;
    /** Queues a local path, or null cancellation, for the next native folder dialog. */
    queueFolderDialogResult(path: string | null): void;
    /** Sends a normalized key press to the keyed control. */
    pressKey(key: string): void;
    /** Reads the keyed control's text content. @returns The normalized text content. */
    getText(key: string): string;
    /** Reads a supported native property from the keyed control. @returns The normalized property value. */
    getProperty(key: string, property: DesktopTestProperty): string;
    /** Replaces the value of a keyed TextBox. */
    setTextBoxValue(key: string, value: string): void;
    /** Replaces the checked state of a keyed CheckBox. */
    setCheckBoxValue(key: string, value: boolean): void;
    /** Selects an item in a keyed ComboBox. */
    setComboBoxIndex(key: string, value: number): void;
    /** Replaces the numeric value of a keyed Slider. */
    setSliderValue(key: string, value: number): void;
    /** Replaces the headless window client size in device-independent pixels. */
    setWindowClientSize(width: number, height: number): void;
    /** Presses the primary mouse pointer at local coordinates on a keyed control. */
    pressPointer(key: string, point: { readonly x: number; readonly y: number }): void;
    /** Moves an active primary mouse pointer to local coordinates on its keyed control. */
    movePointer(key: string, point: { readonly x: number; readonly y: number }): void;
    /** Releases an active primary mouse pointer at local coordinates on its keyed control. */
    releasePointer(key: string, point: { readonly x: number; readonly y: number }): void;
    /** Cancels an active captured primary mouse pointer on its keyed control. */
    cancelPointer(key: string): void;
    /** Drags the primary mouse pointer through local x/y coordinate pairs on a keyed control. */
    dragPointer(key: string, points: readonly { readonly x: number; readonly y: number }[]): void;
    /** Simulates dropping text on a keyed control. @returns The drop effect selected by the control. */
    dropText(key: string, value: string): string;
}

/**
 * Creates a headless test driver for an active desktop window.
 * @param window - Live window whose native tree will be driven.
 * @returns A key-based desktop test driver.
 * @category Testing
 */
export function createDesktopTestDriver(window: DesktopWindow): DesktopTestDriver {
    const managed: any = (window as any).__managedRoot;
    if (managed === undefined || managed === null)
        throw new Error("The supplied value is not an active SharpTS desktop root.");
    return {
        afterRender(callback: () => void): void { DesktopTestingBridge.AfterRender(managed, callback); },
        click(key: string): void { DesktopTestingBridge.Click(managed, key); },
        clickMenuItem(key: string): void { DesktopTestingBridge.ClickMenuItem(managed, key); },
        queueMessageDialogResult(result: "ok" | "cancel" | "yes" | "no"): void {
            DesktopTestingBridge.QueueMessageDialogResult(managed, result);
        },
        queueOpenFileDialogResult(paths: readonly string[]): void {
            DesktopTestingBridge.QueueOpenFileDialogResult(managed, paths.slice());
        },
        queueSaveFileDialogResult(path: string | null): void {
            DesktopTestingBridge.QueueSaveFileDialogResult(managed, path);
        },
        queueFolderDialogResult(path: string | null): void {
            DesktopTestingBridge.QueueFolderDialogResult(managed, path);
        },
        pressKey(key: string): void { DesktopTestingBridge.PressKey(managed, key); },
        getText(key: string): string { return DesktopTestingBridge.GetText(managed, key); },
        getProperty(key: string, property: DesktopTestProperty): string {
            return DesktopTestingBridge.GetProperty(managed, key, property);
        },
        setTextBoxValue(key: string, value: string): void {
            DesktopTestingBridge.SetTextBoxValue(managed, key, value);
        },
        setCheckBoxValue(key: string, value: boolean): void {
            DesktopTestingBridge.SetCheckBoxValue(managed, key, value);
        },
        setComboBoxIndex(key: string, value: number): void {
            DesktopTestingBridge.SetComboBoxIndex(managed, key, value);
        },
        setSliderValue(key: string, value: number): void {
            DesktopTestingBridge.SetSliderValue(managed, key, value);
        },
        setWindowClientSize(width: number, height: number): void {
            DesktopTestingBridge.SetWindowClientSize(managed, width, height);
        },
        pressPointer(key: string, point: { readonly x: number; readonly y: number }): void {
            DesktopTestingBridge.PressPointer(managed, key, point.x, point.y);
        },
        movePointer(key: string, point: { readonly x: number; readonly y: number }): void {
            DesktopTestingBridge.MovePointer(managed, key, point.x, point.y);
        },
        releasePointer(key: string, point: { readonly x: number; readonly y: number }): void {
            DesktopTestingBridge.ReleasePointer(managed, key, point.x, point.y);
        },
        cancelPointer(key: string): void { DesktopTestingBridge.CancelPointer(managed, key); },
        dragPointer(key: string, points: readonly { readonly x: number; readonly y: number }[]): void {
            const coordinates: number[] = [];
            for (const point of points) { coordinates.push(point.x); coordinates.push(point.y); }
            DesktopTestingBridge.DragPointer(managed, key, coordinates);
        },
        dropText(key: string, value: string): string {
            return DesktopTestingBridge.DropText(managed, key, value);
        },
    };
}
