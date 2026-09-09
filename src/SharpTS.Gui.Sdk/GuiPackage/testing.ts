import { DesktopTestingBridge } from "dotnet:SharpTS.Gui";
import type { DesktopWindow } from "./runtime.ts";

/** Native property that can be read through DesktopTestDriver. @category Testing */
export type DesktopTestProperty = "isChecked" | "value" | "width" | "height" |
    "automationName" | "background" | "foreground" | "toolTip" | "isEnabled" | "isVisible";

/** Headless driver for locating and interacting with controls by their key. @category Testing */
export interface DesktopTestDriver {
    /** Compares this window with a reviewed PNG baseline; exact by default. An explicit pixel budget permits minor native raster variance. Update explicitly replaces the baseline. */
    assertSnapshot(baselinePath: string, update?: boolean, maxDifferentPixels?: number): string;
    /** True when the entire arranged control is visible inside its window and scrolling ancestors. */
    isInViewport(key: string): boolean;
    /** Captures this driver's window, including an owned dialog, and returns the PNG hash. */
    captureSnapshot(path: string): string;
    /** Finds an open owned dialog by its exact title. Throws when missing or ambiguous. */
    ownedWindow(title: string): DesktopTestDriver;
    /** Sends wheel input at a control-local point through native hit testing. */
    wheel(key: string, x: number, y: number, deltaX: number, deltaY: number, ctrl?: boolean): void;
    /** Sets an exact numeric value; this does not simulate typing or an edit gesture. */
    setNumericValue(key: string, value: number): void;
    /** Runs a callback after pending async event, reactive render, and native commit work completes. */
    afterRender(callback: () => void): void;
    /** Activates the keyed control. */
    click(key: string): void;
    /** Invokes a keyed menu item through the native routed-click path. */
    clickMenuItem(key: string): void;
    /** Queues the result consumed by the next native message dialog. */
    queueMessageDialogResult(result: "ok" | "cancel" | "yes" | "no" | "save" | "discard"): void;
    /** Queues local paths returned by the next native open-file dialog. */
    queueOpenFileDialogResult(paths: readonly string[]): void;
    /** Queues a local path, or null cancellation, for the next native save-file dialog. */
    queueSaveFileDialogResult(path: string | null): void;
    /** Queues a local path, or null cancellation, for the next native folder dialog. */
    queueFolderDialogResult(path: string | null): void;
    /** Sends a key or gesture such as Ctrl+S or Shift+Tab through native focused-control routing. */
    pressKey(key: string): void;
    /** Focuses a keyed native control. */
    focus(key: string): boolean;
    /** Reports whether the keyed control or one of its native parts has keyboard focus. */
    isFocused(key: string): boolean;
    /** Sends committed text input to the native focused control (including IME text). */
    typeText(text: string): void;
    /** Simulates the window moving to a display with a different DPI scale. */
    setRenderScaling(scaling: number): void;
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
    /** Moves an unpressed mouse pointer; supports hover and pointer-enter/leave checks. */
    hoverPointer(key: string, point: { readonly x: number; readonly y: number }): void;
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
    return driverForRoot(managed);
}

function driverForRoot(managed: any): DesktopTestDriver {
    return {
        assertSnapshot(path: string, update: boolean = false, maxDifferentPixels: number = 0): string { return DesktopTestingBridge.AssertSnapshot(managed, path, update, maxDifferentPixels); },
        isInViewport(key: string): boolean { return DesktopTestingBridge.IsInViewport(managed, key); },
        captureSnapshot(path: string): string { return DesktopTestingBridge.CaptureSnapshot(managed, path); },
        ownedWindow(title: string): DesktopTestDriver { return driverForRoot(DesktopTestingBridge.FindOwnedWindow(managed, title)); },
        wheel(key: string, x: number, y: number, deltaX: number, deltaY: number, ctrl: boolean = false): void {
            DesktopTestingBridge.Wheel(managed, key, x, y, deltaX, deltaY, ctrl);
        },
        setNumericValue(key: string, value: number): void { DesktopTestingBridge.SetNumericValue(managed, key, value); },
        afterRender(callback: () => void): void { DesktopTestingBridge.AfterRender(managed, callback); },
        click(key: string): void { DesktopTestingBridge.Click(managed, key); },
        clickMenuItem(key: string): void { DesktopTestingBridge.ClickMenuItem(managed, key); },
        queueMessageDialogResult(result: "ok" | "cancel" | "yes" | "no" | "save" | "discard"): void {
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
        focus(key: string): boolean { return DesktopTestingBridge.Focus(managed, key); },
        isFocused(key: string): boolean { return DesktopTestingBridge.IsFocused(managed, key); },
        typeText(text: string): void { DesktopTestingBridge.TypeText(managed, text); },
        setRenderScaling(scaling: number): void { DesktopTestingBridge.SetRenderScaling(managed, scaling); },
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
        hoverPointer(key: string, point: { readonly x: number; readonly y: number }): void {
            DesktopTestingBridge.HoverPointer(managed, key, point.x, point.y);
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
